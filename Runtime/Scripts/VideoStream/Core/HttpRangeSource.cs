using System;
using BS.AudioStream;

namespace BS.VideoStream
{
    /// <summary>
    /// A file read through Range requests on <see cref="StreamFetcher"/>, so every hop passes the fetcher's address checks and a
    /// redirect is followed once.
    /// <para>
    /// A player reads its tracks in time order, and an MP4 keeps each track's samples in its own chunks, so reads alternate
    /// between places in the file. Each reader (lane) therefore has its own window, and a read is served from any window that
    /// already holds it. With one shared window, every switch threw it away: a 20 MB world file cost 1,174 requests and 82 MB.
    /// With windows shared out by position, the tracks stole each other's windows and fetched 1.6 times the file.
    /// </para>
    /// <para>
    /// A window starts small and doubles while its lane keeps moving forward, skipping at most the other tracks' chunks. A
    /// request that stalls or drops keeps what arrived and asks again from there; only repeated requests that bring nothing at
    /// all are a failure. Reads come from the decode thread.
    /// </para>
    /// </summary>
    internal sealed class HttpRangeSource : ILanedByteSource
    {
        const int SmallestWindow = 64 * 1024;
        const int ReadWaitMilliseconds = 250;
        const int MaxEmptyAttempts = 3;
        const int Windows = 4;

        sealed class Window
        {
            public byte[] Bytes;
            public long Start = -1;
            public int Count;
            public int Target = SmallestWindow;

            public long End => Start + Count;

            public bool Holds(long position) => Start >= 0 && position >= Start && position < End;
        }

        readonly StreamFetcher _fetcher;
        readonly int _largestWindow;
        readonly int _stallMilliseconds;
        readonly Window[] _windows = new Window[Windows];
        Uri _uri;
        int _lane;

        public HttpRangeSource(StreamFetcher fetcher, Uri uri, int largestWindow = 1024 * 1024, int stallMilliseconds = 15000)
        {
            _fetcher = fetcher;
            _uri = uri;
            _largestWindow = Math.Max(SmallestWindow, largestWindow);
            _stallMilliseconds = stallMilliseconds;
            for (var i = 0; i < Windows; i++)
            {
                _windows[i] = new Window();
            }
        }

        /// <summary>Bytes actually downloaded, for measuring what a track costs.</summary>
        public long Fetched { get; private set; }

        public int Requests { get; private set; }

        /// <summary>A 206 only states the length of its part, so the total isn't known here.</summary>
        public long Length => -1;

        /// <summary>The file's own structure uses the last window; tracks share the rest in turn.</summary>
        public int Lane
        {
            set => _lane = value < 0 ? Windows - 1 : value % (Windows - 1);
        }

        public int Read(long position, byte[] buffer, int offset, int count)
        {
            Window window = null;
            foreach (var w in _windows)
            {
                if (w.Holds(position))
                {
                    window = w;
                    break;
                }
            }
            if (window == null)
            {
                window = _windows[_lane];
                var forward = window.Start >= 0 && position >= window.End && position - window.End <= window.Target;
                window.Target = forward ? Math.Min(_largestWindow, window.Target * 2) : SmallestWindow;
                Fill(window, position, count);
                if (window.Count == 0)
                {
                    return 0;
                }
            }
            var at = (int)(position - window.Start);
            var bytes = Math.Min(count, window.Count - at);
            Buffer.BlockCopy(window.Bytes, at, buffer, offset, bytes);
            return bytes;
        }

        void Fill(Window window, long position, int count)
        {
            var want = Math.Max(window.Target, Math.Min(count, _largestWindow));
            if (window.Bytes == null || window.Bytes.Length < want)
            {
                window.Bytes = new byte[Math.Min(_largestWindow, Math.Max(want, window.Bytes?.Length * 2 ?? 0))];
            }
            window.Start = position;
            window.Count = 0;
            for (var attempt = 0; ; attempt++)
            {
                var problem = Request(window, position, want);
                if (problem == null)
                {
                    Fetched += window.Count;
                    return;
                }
                if (attempt + 1 >= MaxEmptyAttempts)
                {
                    throw new StreamFailure(StreamUrl.Shown(_uri) + problem);
                }
                _fetcher.Wait(StreamFetcher.Backoff(attempt));
            }
        }

        /// <summary>Null when the window holds something or the file has ended. Otherwise what went wrong, with nothing to show for it.</summary>
        string Request(Window window, long position, int want)
        {
            Requests++;
            using (var response = _fetcher.Open(ref _uri, position, want))
            {
                if (response.Status == 200 && position > 0)
                {
                    throw new StreamFailure(StreamUrl.Shown(_uri) + " doesn't support range requests");
                }
                var waited = 0;
                while (window.Count < want)
                {
                    var read = response.Read(window.Bytes, window.Count, want - window.Count, ReadWaitMilliseconds);
                    if (read < 0)
                    {
                        break;
                    }
                    if (read == 0)
                    {
                        // Nothing yet is not the end: a slow link goes quiet for a while. A long silence ends this request.
                        _fetcher.Wait(0);
                        waited += ReadWaitMilliseconds;
                        if (waited >= _stallMilliseconds)
                        {
                            return window.Count > 0 ? null : " stopped sending";
                        }
                        continue;
                    }
                    waited = 0;
                    window.Count += read;
                }
                return response.Interrupted && window.Count == 0 ? " dropped the connection" : null;
            }
        }
    }
}

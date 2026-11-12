using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace BS.AudioStream
{
    /// <summary>A fetch or stream that can't go on. Its message is fit for a log: it names a host at most, never a full URL.</summary>
    internal sealed class StreamFailure : Exception
    {
        public StreamFailure(string reason) : base(reason)
        {
        }
    }

    /// <summary>
    /// HTTP over HttpClient, for the decode thread and anything else that reads from a thread of its own. Redirects are left
    /// to <see cref="StreamFetcher"/>, which checks each hop. A slow reader slows the download through the bounded pipe, so
    /// an endless stream can't pile up in memory.
    /// </summary>
    internal sealed class HttpStreamTransport : IStreamTransport
    {
        const int PipeBytes = 1024 * 1024;

        public static readonly HttpStreamTransport Shared = new HttpStreamTransport();

        readonly HttpClient _client;

        public HttpStreamTransport()
        {
            _client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
            _client.DefaultRequestHeaders.UserAgent.ParseAdd("BSAudioStream/1.0");
        }

        public IStreamResponse Open(Uri uri, long start, long length) => new Response(_client, uri, start, length);

        sealed class Response : IStreamResponse
        {
            readonly BytePipe _pipe = new BytePipe(PipeBytes);
            readonly CancellationTokenSource _cancel = new CancellationTokenSource();
            readonly ManualResetEventSlim _headers = new ManualResetEventSlim(false);

            public Response(HttpClient client, Uri uri, long start, long length)
            {
                Task.Run(() => Fetch(client, uri, start, length));
            }

            public int Status { get; private set; }

            public string ContentType { get; private set; }

            public string Location { get; private set; }

            public long ContentLength { get; private set; } = -1;

            public bool Interrupted => _pipe.Interrupted;

            async Task Fetch(HttpClient client, Uri uri, long start, long length)
            {
                try
                {
                    using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
                    {
                        if (start >= 0)
                        {
                            request.Headers.Range = new RangeHeaderValue(start, length >= 0 ? start + length - 1 : (long?)null);
                        }
                        using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _cancel.Token).ConfigureAwait(false))
                        {
                            Status = (int)response.StatusCode;
                            ContentType = response.Content.Headers.ContentType?.ToString();
                            Location = response.Headers.Location?.OriginalString;
                            ContentLength = response.Content.Headers.ContentLength ?? -1;
                            _headers.Set();
                            using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                            {
                                var buffer = new byte[16 * 1024];
                                while (true)
                                {
                                    var read = await stream.ReadAsync(buffer, 0, buffer.Length, _cancel.Token).ConfigureAwait(false);
                                    if (read <= 0)
                                    {
                                        _pipe.Complete(false);
                                        return;
                                    }
                                    while (!_pipe.TryWrite(buffer, 0, read))
                                    {
                                        await Task.Delay(10, _cancel.Token).ConfigureAwait(false);
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    // Reported through the pipe: a status of 0 if nothing came, or an interrupted end.
                    _headers.Set();
                    _pipe.Complete(true);
                }
            }

            public bool WaitForHeaders(int milliseconds) => _headers.Wait(milliseconds);

            public int Read(byte[] buffer, int offset, int count, int milliseconds) => _pipe.Read(buffer, offset, count, milliseconds);

            public void Dispose()
            {
                _cancel.Cancel();
                _pipe.Complete(true);
            }
        }
    }

    /// <summary>
    /// The one way anything here fetches: range requests, cancellation, retries of what may be temporary, a throughput
    /// reading, and redirects followed by hand so that every hop passes the same address check as the first.
    /// </summary>
    internal sealed class StreamFetcher
    {
        const int MaxRetries = 3;

        readonly IStreamTransport _transport;
        readonly CancellationToken _cancel;
        readonly int _stallMilliseconds;

        // Throughput: bytes over the last full second, kept as a running average.
        readonly Stopwatch _clock = Stopwatch.StartNew();
        long _windowBytes;
        long _windowStart;
        long _bytesPerSecond;

        public StreamFetcher(IStreamTransport transport, CancellationToken cancel, int stallMilliseconds)
        {
            _transport = transport;
            _cancel = cancel;
            _stallMilliseconds = stallMilliseconds;
        }

        /// <summary>The recent download rate. 0 until a second has passed.</summary>
        public double BytesPerSecond => Interlocked.Read(ref _bytesPerSecond);

        public static int Backoff(int attempt) => Math.Min(30000, 500 << Math.Min(attempt, 6));

        /// <summary>Waits, ending early with an exception if cancelled.</summary>
        public void Wait(int milliseconds)
        {
            if (_cancel.WaitHandle.WaitOne(milliseconds))
            {
                _cancel.ThrowIfCancellationRequested();
            }
        }

        /// <summary>
        /// Opens <paramref name="uri"/>, which becomes where the bytes really came from, as a relative URL inside must
        /// resolve against that. Throws <see cref="StreamFailure"/> for anything that won't get better.
        /// </summary>
        public IStreamResponse Open(ref Uri uri, long start = -1, long length = -1)
        {
            for (var attempt = 0; ; attempt++)
            {
                var target = uri;
                for (var hops = 0; hops <= StreamLimits.MaxRedirects; hops++)
                {
                    var response = _transport.Open(target, start, length);
                    var waited = 0;
                    while (!response.WaitForHeaders(100))
                    {
                        waited += 100;
                        if (_cancel.IsCancellationRequested || waited >= _stallMilliseconds)
                        {
                            response.Dispose();
                            _cancel.ThrowIfCancellationRequested();
                            throw new StreamFailure(StreamUrl.Shown(target) + " didn't answer");
                        }
                    }
                    var status = response.Status;
                    if (status == 301 || status == 302 || status == 303 || status == 307 || status == 308)
                    {
                        var next = StreamUrl.Resolve(target, response.Location);
                        response.Dispose();
                        if (next == null)
                        {
                            throw new StreamFailure("a redirect from " + StreamUrl.Shown(target) + " went somewhere that isn't http or https");
                        }
                        target = next;
                        continue;
                    }
                    if (status == 200 || status == 206)
                    {
                        uri = target;
                        return new Metered(response, this);
                    }
                    response.Dispose();
                    var temporary = status == 0 || status == 408 || status == 429 || status >= 500;
                    if (!temporary || attempt >= MaxRetries)
                    {
                        throw new StreamFailure(StreamUrl.Shown(target) + (status == 0 ? " couldn't be reached" : " answered " + status));
                    }
                    target = null;
                    break;
                }
                if (target != null)
                {
                    throw new StreamFailure("too many redirects from " + StreamUrl.Shown(uri));
                }
                Wait(Backoff(attempt));
            }
        }

        void Count(int bytes)
        {
            var now = _clock.ElapsedMilliseconds;
            var total = Interlocked.Add(ref _windowBytes, bytes);
            var elapsed = now - Interlocked.Read(ref _windowStart);
            if (elapsed >= 1000)
            {
                Interlocked.Exchange(ref _windowStart, now);
                Interlocked.Exchange(ref _windowBytes, 0);
                var rate = total * 1000 / elapsed;
                var previous = Interlocked.Read(ref _bytesPerSecond);
                Interlocked.Exchange(ref _bytesPerSecond, previous == 0 ? rate : (previous + rate) / 2);
            }
        }

        sealed class Metered : IStreamResponse
        {
            readonly IStreamResponse _inner;
            readonly StreamFetcher _owner;

            public Metered(IStreamResponse inner, StreamFetcher owner)
            {
                _inner = inner;
                _owner = owner;
            }

            public int Status => _inner.Status;

            public string ContentType => _inner.ContentType;

            public string Location => _inner.Location;

            public long ContentLength => _inner.ContentLength;

            public bool Interrupted => _inner.Interrupted;

            public bool WaitForHeaders(int milliseconds) => _inner.WaitForHeaders(milliseconds);

            public int Read(byte[] buffer, int offset, int count, int milliseconds)
            {
                var read = _inner.Read(buffer, offset, count, milliseconds);
                if (read > 0)
                {
                    _owner.Count(read);
                }
                return read;
            }

            public void Dispose() => _inner.Dispose();
        }
    }
}

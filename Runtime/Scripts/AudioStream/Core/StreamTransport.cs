using System;
using System.Threading;

namespace BS.AudioStream
{
    /// <summary>Fetches bytes. <see cref="Open"/> returns at once and never follows a redirect: the session does that, so it can check each hop.</summary>
    internal interface IStreamTransport
    {
        /// <summary>Starts a request. A <paramref name="start"/> of -1 asks for the whole resource; <paramref name="length"/> of -1 runs to its end.</summary>
        IStreamResponse Open(Uri uri, long start, long length);
    }

    internal interface IStreamResponse : IDisposable
    {
        /// <summary>Waits for the headers. False when they haven't come yet.</summary>
        bool WaitForHeaders(int milliseconds);

        /// <summary>The HTTP status, or 0 when the request failed without one.</summary>
        int Status { get; }

        string ContentType { get; }

        string Location { get; }

        /// <summary>The length of this response's body, or -1 when the server doesn't say.</summary>
        long ContentLength { get; }

        /// <summary>Bytes read: 0 when none came in time, -1 at the end.</summary>
        int Read(byte[] buffer, int offset, int count, int milliseconds);

        /// <summary>True when the end was a dropped connection or an overflowing queue, rather than the end of the resource.</summary>
        bool Interrupted { get; }
    }

    /// <summary>
    /// A bounded byte queue between whatever receives a download and the decode thread. It grows only as far as it has to.
    /// A receiver that can't be paused sees <see cref="TryWrite"/> fail when it is full, and the download is dropped and
    /// resumed from where the reader got to: memory stays bounded however fast the server sends.
    /// </summary>
    internal sealed class BytePipe
    {
        public const int DefaultCapacity = 8 * 1024 * 1024;

        readonly object _gate = new object();
        readonly int _capacity;
        byte[] _buffer = new byte[64 * 1024];
        int _head;
        int _count;
        bool _complete;

        public BytePipe(int capacity = DefaultCapacity)
        {
            _capacity = capacity;
        }

        public bool Interrupted { get; private set; }

        public int Count
        {
            get
            {
                lock (_gate)
                {
                    return _count;
                }
            }
        }

        public bool TryWrite(byte[] data, int offset, int count)
        {
            lock (_gate)
            {
                if (_complete)
                {
                    return false;
                }
                if (_count + count > _capacity)
                {
                    return false;
                }
                if (_count + count > _buffer.Length)
                {
                    Grow(_count + count);
                }
                var tail = (_head + _count) % _buffer.Length;
                var first = Math.Min(count, _buffer.Length - tail);
                Buffer.BlockCopy(data, offset, _buffer, tail, first);
                Buffer.BlockCopy(data, offset + first, _buffer, 0, count - first);
                _count += count;
                Monitor.PulseAll(_gate);
                return true;
            }
        }

        void Grow(int needed)
        {
            var size = _buffer.Length;
            while (size < needed)
            {
                size *= 2;
            }
            var grown = new byte[Math.Min(size, _capacity)];
            var first = Math.Min(_count, _buffer.Length - _head);
            Buffer.BlockCopy(_buffer, _head, grown, 0, first);
            Buffer.BlockCopy(_buffer, 0, grown, first, _count - first);
            _buffer = grown;
            _head = 0;
        }

        public void Complete(bool interrupted)
        {
            lock (_gate)
            {
                if (!_complete)
                {
                    _complete = true;
                    Interrupted = interrupted;
                }
                Monitor.PulseAll(_gate);
            }
        }

        public int Read(byte[] buffer, int offset, int count, int milliseconds)
        {
            lock (_gate)
            {
                if (_count == 0)
                {
                    if (_complete)
                    {
                        return -1;
                    }
                    Monitor.Wait(_gate, milliseconds);
                    if (_count == 0)
                    {
                        return _complete ? -1 : 0;
                    }
                }
                var take = Math.Min(count, _count);
                var first = Math.Min(take, _buffer.Length - _head);
                Buffer.BlockCopy(_buffer, _head, buffer, offset, first);
                Buffer.BlockCopy(_buffer, 0, buffer, offset + first, take - first);
                _head = (_head + take) % _buffer.Length;
                _count -= take;
                return take;
            }
        }
    }
}

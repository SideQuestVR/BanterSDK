using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BS.AudioStream.Tests
{
    /// <summary>What a test wants a path to do.</summary>
    internal sealed class Route
    {
        public byte[] Body;
        public Func<string> Text;
        public string ContentType = "application/octet-stream";
        public int Status = 200;
        public string RedirectTo;
        public bool Ranges = true;

        /// <summary>Cut the connection after this many body bytes, this many times.</summary>
        public long DropAfter = -1;
        public int Drops;

        /// <summary>Send this per write, pausing between: a slow server.</summary>
        public int ChunkBytes = 64 * 1024;
        public int ChunkDelayMilliseconds;

        /// <summary>Send bytes for ever, until the client goes.</summary>
        public Func<long, byte[]> Endless;

        public int Requests;
        public long BytesSent;
    }

    /// <summary>A real HTTP server on the loopback address, for everything a fetch can run into.</summary>
    internal sealed class TestServer : IDisposable
    {
        readonly HttpListener _listener = new HttpListener();
        readonly ConcurrentDictionary<string, Route> _routes = new ConcurrentDictionary<string, Route>();
        readonly CancellationTokenSource _stop = new CancellationTokenSource();

        public TestServer()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            Port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            _listener.Prefixes.Add($"http://localhost:{Port}/");
            _listener.Start();
            Task.Run(Accept);
        }

        public int Port { get; }

        public string Url(string path) => $"http://localhost:{Port}/{path.TrimStart('/')}";

        public Route Add(string path, Route route)
        {
            _routes["/" + path.TrimStart('/')] = route;
            return route;
        }

        public Route Add(string path, byte[] body, string contentType = "application/octet-stream") => Add(path, new Route { Body = body, ContentType = contentType });

        public Route AddText(string path, string text, string contentType = "application/vnd.apple.mpegurl") => Add(path, new Route { Body = Encoding.UTF8.GetBytes(text), ContentType = contentType });

        /// <summary>Serves every file in a folder under a prefix.</summary>
        public void AddFolder(string folder, string prefix)
        {
            foreach (var file in Directory.GetFiles(folder))
            {
                var name = Path.GetFileName(file);
                var type = name.EndsWith(".m3u8") ? "application/vnd.apple.mpegurl" : "application/octet-stream";
                Add(prefix.TrimEnd('/') + "/" + name, File.ReadAllBytes(file), type);
            }
        }

        async Task Accept()
        {
            while (!_stop.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch
                {
                    return;
                }
                _ = Task.Run(() => Serve(context));
            }
        }

        async Task Serve(HttpListenerContext context)
        {
            var response = context.Response;
            try
            {
                if (!_routes.TryGetValue(context.Request.Url.AbsolutePath, out var route))
                {
                    response.StatusCode = 404;
                    response.Close();
                    return;
                }
                Interlocked.Increment(ref route.Requests);
                if (route.RedirectTo != null)
                {
                    response.StatusCode = route.Status;
                    response.RedirectLocation = route.RedirectTo;
                    response.Close();
                    return;
                }
                if (route.Status != 200)
                {
                    response.StatusCode = route.Status;
                    response.Close();
                    return;
                }
                response.ContentType = route.ContentType;
                if (route.Endless != null)
                {
                    response.SendChunked = true;
                    var position = 0L;
                    while (!_stop.IsCancellationRequested)
                    {
                        var piece = route.Endless(position);
                        await response.OutputStream.WriteAsync(piece, 0, piece.Length);
                        await response.OutputStream.FlushAsync();
                        position += piece.Length;
                        Interlocked.Add(ref route.BytesSent, piece.Length);
                        if (route.ChunkDelayMilliseconds > 0)
                        {
                            await Task.Delay(route.ChunkDelayMilliseconds);
                        }
                    }
                    return;
                }

                var body = route.Text != null ? Encoding.UTF8.GetBytes(route.Text()) : route.Body;
                var start = 0L;
                var end = body.LongLength;
                var range = context.Request.Headers["Range"];
                if (range != null && route.Ranges && range.StartsWith("bytes="))
                {
                    var parts = range.Substring(6).Split('-');
                    start = long.Parse(parts[0]);
                    if (parts.Length > 1 && parts[1].Length > 0)
                    {
                        end = Math.Min(end, long.Parse(parts[1]) + 1);
                    }
                    response.StatusCode = 206;
                    response.Headers["Content-Range"] = $"bytes {start}-{end - 1}/{body.LongLength}";
                }
                response.ContentLength64 = end - start;
                var dropping = route.DropAfter >= 0 && Interlocked.Decrement(ref route.Drops) >= 0;
                var sent = 0L;
                var at = start;
                while (at < end)
                {
                    var count = (int)Math.Min(route.ChunkBytes, end - at);
                    if (dropping && sent + count > route.DropAfter)
                    {
                        var left = (int)(route.DropAfter - sent);
                        if (left > 0)
                        {
                            await response.OutputStream.WriteAsync(body, (int)at, left);
                            await response.OutputStream.FlushAsync();
                            Interlocked.Add(ref route.BytesSent, left);
                        }
                        response.Abort();
                        return;
                    }
                    await response.OutputStream.WriteAsync(body, (int)at, count);
                    at += count;
                    sent += count;
                    Interlocked.Add(ref route.BytesSent, count);
                    if (route.ChunkDelayMilliseconds > 0)
                    {
                        await Task.Delay(route.ChunkDelayMilliseconds);
                    }
                }
                response.Close();
            }
            catch
            {
                try
                {
                    response.Abort();
                }
                catch
                {
                }
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            try
            {
                _listener.Stop();
                _listener.Close();
            }
            catch
            {
            }
        }
    }

    /// <summary>The test stand-in for the UnityWebRequest transport: a download on another thread, into a real <see cref="BytePipe"/>.</summary>
    internal sealed class HttpTransport : IStreamTransport, IDisposable
    {
        readonly HttpClient _client;
        readonly int _pipeBytes;

        public HttpTransport(int pipeBytes = BytePipe.DefaultCapacity)
        {
            _pipeBytes = pipeBytes;
            _client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
        }

        /// <summary>Every URL asked for, to prove what was and wasn't fetched.</summary>
        public readonly ConcurrentQueue<string> Requests = new ConcurrentQueue<string>();

        public int PeakPipeBytes;

        public IStreamResponse Open(Uri uri, long start, long length)
        {
            Requests.Enqueue(uri.ToString());
            return new Response(this, _client, uri, start, length, _pipeBytes);
        }

        public void Dispose() => _client.Dispose();

        sealed class Response : IStreamResponse
        {
            readonly HttpTransport _owner;
            readonly BytePipe _pipe;
            readonly CancellationTokenSource _cancel = new CancellationTokenSource();
            readonly ManualResetEventSlim _headers = new ManualResetEventSlim(false);

            public Response(HttpTransport owner, HttpClient client, Uri uri, long start, long length, int pipeBytes)
            {
                _owner = owner;
                _pipe = new BytePipe(pipeBytes);
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
                    var request = new HttpRequestMessage(HttpMethod.Get, uri);
                    if (start >= 0)
                    {
                        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(start, length >= 0 ? start + length - 1 : (long?)null);
                    }
                    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _cancel.Token);
                    Status = (int)response.StatusCode;
                    ContentType = response.Content.Headers.ContentType?.ToString();
                    Location = response.Headers.Location?.ToString();
                    ContentLength = response.Content.Headers.ContentLength ?? -1;
                    _headers.Set();
                    using var stream = await response.Content.ReadAsStreamAsync(_cancel.Token);
                    var buffer = new byte[16 * 1024];
                    while (true)
                    {
                        var read = await stream.ReadAsync(buffer, 0, buffer.Length, _cancel.Token);
                        if (read <= 0)
                        {
                            _pipe.Complete(false);
                            return;
                        }
                        if (!_pipe.TryWrite(buffer, 0, read))
                        {
                            _pipe.Complete(true);
                            return;
                        }
                        var held = _pipe.Count;
                        if (held > _owner.PeakPipeBytes)
                        {
                            _owner.PeakPipeBytes = held;
                        }
                    }
                }
                catch
                {
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

    /// <summary>Collects everything a session outputs, by role, with no pacing.</summary>
    internal sealed class CollectingOutput : IStreamOutput
    {
        public readonly List<StreamFormat> Formats = new List<StreamFormat>();
        public readonly List<List<float>> Channels = new List<List<float>>();
        public string Failure;
        public bool Finished;
        public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
        public long Frames;

        /// <summary>Raised after each write, so a test can stop an endless stream.</summary>
        public Action<long> OnWrite;

        public bool Configure(StreamFormat format)
        {
            Formats.Add(format);
            Channels.Clear();
            for (var i = 0; i < format.Roles.Length; i++)
            {
                Channels.Add(new List<float>());
            }
            return true;
        }

        public bool Write(float[][] planes, int count)
        {
            for (var c = 0; c < Channels.Count; c++)
            {
                var channel = Channels[c];
                var plane = planes[c];
                for (var i = 0; i < count; i++)
                {
                    channel.Add(plane[i]);
                }
            }
            Frames += count;
            OnWrite?.Invoke(Frames);
            return true;
        }

        public readonly List<long> Timestamps = new List<long>();

        public void MarkTimestamp(long ptsUs) => Timestamps.Add(ptsUs);

        public void Finish()
        {
            Finished = true;
            Done.Set();
        }

        public void Fail(string reason)
        {
            Failure = reason;
            Done.Set();
        }

        public float[] Role(AudioChannelRole role)
        {
            var index = Array.IndexOf(Formats.Last().Roles, role);
            return index < 0 ? null : Channels[index].ToArray();
        }
    }

    internal static class Play
    {
        public static AudioChannelRole[] All => ChannelLayouts.Of(8);

        /// <summary>Runs a session to its end, or to the time limit.</summary>
        public static CollectingOutput Url(string url, AudioChannelRole[] layout = null, int seconds = 60, IStreamTransport transport = null, Action<StreamSessionOptions> configure = null)
        {
            var output = new CollectingOutput();
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
            var options = new StreamSessionOptions { Url = url, Layout = layout ?? All, MinReloadSeconds = 0.05, StallMilliseconds = 3000 };
            configure?.Invoke(options);
            var session = new StreamSession(options, transport ?? HttpStreamTransport.Shared, output, cancel.Token);
            session.Run();
            return output;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace BS.VideoStream.Tests
{
    /// <summary>Serves a folder on the loopback address, with byte ranges, for playing fixtures through the real fetcher.</summary>
    internal sealed class LoopbackServer : IDisposable
    {
        readonly HttpListener _listener = new HttpListener();
        readonly Dictionary<string, byte[]> _files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        readonly CancellationTokenSource _stop = new CancellationTokenSource();

        public LoopbackServer(string folder)
        {
            // Subfolders too, for a playlist and its segments.
            var root = Path.GetFullPath(folder);
            foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                _files["/" + Path.GetRelativePath(root, file).Replace('\\', '/')] = File.ReadAllBytes(file);
            }
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            Port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            _listener.Prefixes.Add($"http://localhost:{Port}/");
            _listener.Start();
            Task.Run(Accept);
        }

        public int Port { get; }

        public int Requests;

        public string Url(string name) => $"http://localhost:{Port}/{name}";

        async Task Accept()
        {
            while (!_stop.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception)
                {
                    return;
                }
                _ = Task.Run(() => Serve(context));
            }
        }

        void Serve(HttpListenerContext context)
        {
            Interlocked.Increment(ref Requests);
            var response = context.Response;
            try
            {
                if (!_files.TryGetValue(context.Request.Url.AbsolutePath, out var bytes))
                {
                    response.StatusCode = 404;
                    return;
                }
                var path = context.Request.Url.AbsolutePath;
                response.ContentType = path.EndsWith(".m3u8") ? "application/vnd.apple.mpegurl" : path.EndsWith(".ts") ? "video/mp2t" : "video/mp4";
                long start = 0, end = bytes.Length - 1;
                var range = context.Request.Headers["Range"];
                if (!string.IsNullOrEmpty(range) && range.StartsWith("bytes="))
                {
                    var parts = range.Substring(6).Split('-');
                    start = long.Parse(parts[0]);
                    if (parts.Length > 1 && parts[1].Length > 0)
                    {
                        end = Math.Min(end, long.Parse(parts[1]));
                    }
                    if (start >= bytes.Length)
                    {
                        response.StatusCode = 416;
                        return;
                    }
                    response.StatusCode = 206;
                    response.AddHeader("Content-Range", $"bytes {start}-{end}/{bytes.Length}");
                }
                response.ContentLength64 = end - start + 1;
                response.OutputStream.Write(bytes, (int)start, (int)(end - start + 1));
            }
            catch (Exception)
            {
                // The client went away (a seek, a stop): nothing to do.
            }
            finally
            {
                try
                {
                    response.Close();
                }
                catch (Exception)
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
            catch (Exception)
            {
            }
        }
    }
}

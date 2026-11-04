using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace BS
{
    /// <summary>
    /// Wire trace recorder for the JS↔Unity bridge (Bridge 2 plan §10, "recorded as JSONL"). When enabled, every raw
    /// inbound and outbound bridge message is appended to a JSONL file with time, direction and length, so a session
    /// can be replayed, diffed against a golden trace, or read after a stall.
    ///
    /// Off by default, and costs one volatile read per message while off. When on:
    /// <list type="bullet">
    /// <item>The calling thread (Unity main thread, Ora's Android UI thread, a socket thread) only enqueues a reference:
    /// no formatting, no I/O. A background thread writes.</item>
    /// <item>Memory is bounded: at most <see cref="Options.MaxQueuedRecords"/> records and
    /// <see cref="Options.MaxQueuedChars"/> characters wait for the writer; beyond that records are dropped and counted
    /// (a "dropped" line says how many).</item>
    /// <item>Disk is bounded: the file rotates at <see cref="Options.MaxFileBytes"/> into "&lt;path&gt;.1" (one previous
    /// file kept), so a trace never exceeds about twice that.</item>
    /// <item>Payloads are cut to <see cref="Options.MaxPayloadChars"/> ("cut": true, "len" keeps the full length) and can
    /// be left out or passed through <see cref="Options.Redact"/> (runs on the writer thread, before cutting).</item>
    /// </list>
    /// Traces can hold user ids, space state and page data: local files only, developer-enabled only. Never upload one
    /// without the same review a log upload gets.
    ///
    /// <para><b>File format</b> (one JSON object per line, UTF-8, no BOM). The page-side tap writes the same schema so the
    /// two halves of a session can be merged by "startedUtc" + "t":</para>
    /// <code>
    /// {"type":"header","format":"bs-bridge-trace","version":1,"side":"unity","startedUtc":"2026-11-03T21:00:00.000Z","pid":1234,"maxPayloadChars":4096,"payloads":true}
    /// {"seq":1,"t":0.412,"dir":"out","len":57,"lane":"pipe","data":"..."}
    /// {"seq":2,"t":3.100,"dir":"in","len":70211,"lane":"pipe","data":"...","cut":true}
    /// {"seq":3,"t":3.900,"dir":"in","len":512,"lane":"frame","enc":"b64","data":"AAEC..."}
    /// {"type":"dropped","count":12,"t":9.000}
    /// {"type":"end","t":12.000,"written":3,"dropped":12}
    /// </code>
    /// "t" is milliseconds since <c>startedUtc</c> on a monotonic clock; "seq" numbers the calls (with several calling
    /// threads, neighbouring lines can be out of seq order by a record or two). "len" is UTF-16 chars for text messages
    /// and bytes for binary ones.
    ///
    /// <para><b>Hooks (not installed yet; BSLink/BSPipe belong to another workstream).</b> Two lines in BSPipe.cs, at the
    /// rawest points of today's transport:</para>
    /// <list type="number">
    /// <item>Inbound, first statement of the <c>view.browserMessage.AddListener((reqId, command, data) =&gt; { ... })</c>
    /// lambda in <c>BSPipe.Start</c> (before the start-up-page filter, so ignored messages are traced too):
    /// <c>BS.BridgeTrace.Inbound(data, "pipe");</c></item>
    /// <item>Outbound, first statement of <c>BSPipe.Send(string msg)</c> (after BatchUpdater has packed the frame):
    /// <c>BS.BridgeTrace.Outbound(msg, "pipe");</c></item>
    /// </list>
    /// Bridge 2 transports call the byte[] overloads with their own lane name ("ring", "port", "carrier"). Enable with
    /// <see cref="Enable(Options)"/> or, at start-up on the main thread, <see cref="TryEnableFromCommandLine"/>
    /// (<c>--bs-bridge-trace</c> or <c>--bs-bridge-trace=&lt;path&gt;</c>).
    ///
    /// <para><b>Page-side tap (design, for Injection~/src/bridge, owned by the bridge workstream).</b> A <c>BridgeTap</c>
    /// module with the same schema ("side":"page", "t" from <c>performance.now()</c>): <c>enable({maxRecords,
    /// maxChars, maxPayloadChars, payloads})</c>, <c>disable()</c>, <c>in(frame, lane)</c>, <c>out(frame, lane)</c>,
    /// <c>dump(): string</c> (JSONL) and <c>clear()</c>. It keeps a bounded ring buffer in page memory (oldest records
    /// evicted, an eviction count in the header of the dump) and never sends anything over the bridge itself, so tracing
    /// cannot change what it records. Enabled by <c>window.__BS_BRIDGE_TRACE__ = {...}</c> set before the injection runs,
    /// or <c>?bs-bridge-trace</c> on the page URL, or from the console; read by the conformance harness through CDP
    /// (<c>__bsBridgeTap.dump()</c>). Its two hooks: first line of the transport's send (today
    /// <c>LoggingApiWrapper.emit</c> in src/game/batch-updater.ts, before <c>O.sendMessage</c>):
    /// <c>BridgeTap.out(data, "pipe")</c>; first line of the receive callback (today the <c>O.messages.On("ora-message")</c>
    /// handler in <c>LoggingApiWrapper.on</c>): <c>BridgeTap.in(e.detail.data, "pipe")</c>.</para>
    /// </summary>
    // Internal: not creator API, and a public type in BS.SDK is offered to Visual Scripting and frozen into the surface.
    internal static class BridgeTrace
    {
        // Internal on purpose: a public enum in BS.SDK is offered as a Visual Scripting node (VsNodeGeneration includes
        // every enum of a listed assembly), and the trace is not creator API.
        internal enum Direction { In, Out }

        public sealed class Options
        {
            /// <summary>Trace file; default %TEMP%/bs-bridge-trace/trace-&lt;utc&gt;-&lt;pid&gt;.jsonl.</summary>
            public string Path;
            public long MaxFileBytes = 32L * 1024 * 1024;
            public int MaxPayloadChars = 4096;
            public bool IncludePayloads = true;
            public int MaxQueuedRecords = 20000;
            public long MaxQueuedChars = 8L * 1024 * 1024;
            /// <summary>Optional payload filter (writer thread). Return null to drop the payload but keep the record.</summary>
            public Func<string, string> Redact;
        }

        static Session active;
        static readonly object gate = new object();
        static bool hooksRegistered;

        /// <summary>True while a trace is being recorded.</summary>
        public static bool Enabled => Volatile.Read(ref active) != null;

        /// <summary>The file being written, or null.</summary>
        public static string CurrentPath => Volatile.Read(ref active)?.Path;

        /// <summary>Records dropped by the current trace because the writer could not keep up.</summary>
        public static long Dropped => Volatile.Read(ref active)?.DroppedTotal ?? 0;

        /// <summary>Starts (or restarts) tracing. Returns the trace file path. Throws if the file cannot be created.</summary>
        public static string Enable(Options options = null)
        {
            options = options ?? new Options();
            lock (gate)
            {
                Volatile.Read(ref active)?.Stop();
                var path = options.Path;
                if (string.IsNullOrEmpty(path))
                {
                    var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bs-bridge-trace");
                    path = System.IO.Path.Combine(dir, $"trace-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Pid()}.jsonl");
                }
                var session = new Session(path, options);
                if (!hooksRegistered)
                {
                    hooksRegistered = true;
                    // Editor domain reloads and player exit: flush and let the writer thread go.
                    AppDomain.CurrentDomain.DomainUnload += (_, __) => Disable();
                    AppDomain.CurrentDomain.ProcessExit += (_, __) => Disable();
                }
                Volatile.Write(ref active, session);
                return path;
            }
        }

        /// <summary>Stops tracing; flushes what was queued and closes the file. Safe to call when not tracing.</summary>
        public static void Disable()
        {
            lock (gate)
            {
                var s = Volatile.Read(ref active);
                Volatile.Write(ref active, null);
                s?.Stop();
            }
        }

        // Process ids are not available on every player platform; the trace only uses one for naming and the header.
        static int Pid()
        {
            try { return Process.GetCurrentProcess().Id; }
            catch (Exception) { return 0; }
        }

        /// <summary>Enables tracing if the arguments contain --bs-bridge-trace[=path]. Call once at start-up.</summary>
        public static bool TryEnableFromCommandLine(string[] args = null)
        {
            args = args ?? Environment.GetCommandLineArgs();
            foreach (var a in args)
            {
                if (a == "--bs-bridge-trace")
                {
                    Enable();
                    return true;
                }
                if (a.StartsWith("--bs-bridge-trace=", StringComparison.Ordinal))
                {
                    Enable(new Options { Path = a.Substring("--bs-bridge-trace=".Length) });
                    return true;
                }
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Inbound(string raw, string lane = null) => Volatile.Read(ref active)?.Add(Direction.In, raw, null, lane);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Outbound(string raw, string lane = null) => Volatile.Read(ref active)?.Add(Direction.Out, raw, null, lane);

        /// <summary>A binary frame (Bridge 2 lanes). Copies at most MaxPayloadChars bytes on the calling thread.</summary>
        public static void Inbound(byte[] frame, int offset, int count, string lane = null) => Volatile.Read(ref active)?.AddBytes(Direction.In, frame, offset, count, lane);

        public static void Outbound(byte[] frame, int offset, int count, string lane = null) => Volatile.Read(ref active)?.AddBytes(Direction.Out, frame, offset, count, lane);

        // -------------------------------------------------------------------------------------------------------------

        struct Item
        {
            public long Seq;
            public long Ticks;
            public Direction Dir;
            public string Text;     // string message (whole; cut on the writer thread, after Redact)
            public byte[] Bytes;    // binary message prefix (already cut to MaxPayloadChars bytes)
            public int Length;      // full length: UTF-16 chars for text, bytes for binary
            public string Lane;
        }

        sealed class Session
        {
            public readonly string Path;
            readonly Options options;
            readonly ConcurrentQueue<Item> queue = new ConcurrentQueue<Item>();
            readonly AutoResetEvent signal = new AutoResetEvent(false);
            readonly long startTicks = Stopwatch.GetTimestamp();
            readonly Thread thread;
            StreamWriter writer;
            long fileBytes;
            long seq;
            long queuedRecords;
            long queuedChars;
            long droppedSinceLast;
            long written;
            public long DroppedTotal;
            volatile bool stopping;

            public Session(string path, Options options)
            {
                Path = path;
                this.options = options;
                var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                Open(FileMode.Create);
                thread = new Thread(Run) { IsBackground = true, Name = "BS bridge trace" };
                thread.Start();
            }

            public void Add(Direction dir, string text, byte[] bytes, string lane) => Enqueue(dir, text, bytes, text?.Length ?? 0, lane);

            public void AddBytes(Direction dir, byte[] frame, int offset, int count, string lane)
            {
                if (stopping || frame == null) return;
                var keep = options.IncludePayloads ? Math.Max(0, Math.Min(count, options.MaxPayloadChars)) : 0;
                var copy = new byte[keep];
                Buffer.BlockCopy(frame, offset, copy, 0, keep);
                Enqueue(dir, null, copy, count, lane);
            }

            void Enqueue(Direction dir, string text, byte[] bytes, int length, string lane)
            {
                if (stopping) return;
                var chars = text?.Length ?? bytes?.Length ?? 0;
                if (Interlocked.Increment(ref queuedRecords) > options.MaxQueuedRecords ||
                    Interlocked.Add(ref queuedChars, chars) > options.MaxQueuedChars)
                {
                    Interlocked.Decrement(ref queuedRecords);
                    Interlocked.Add(ref queuedChars, -chars);
                    Interlocked.Increment(ref droppedSinceLast);
                    Interlocked.Increment(ref DroppedTotal);
                    return;
                }
                queue.Enqueue(new Item
                {
                    Seq = Interlocked.Increment(ref seq),
                    Ticks = Stopwatch.GetTimestamp(),
                    Dir = dir,
                    Text = text,
                    Bytes = bytes,
                    Length = length,
                    Lane = lane,
                });
                signal.Set();
            }

            void Open(FileMode mode)
            {
                var stream = new FileStream(Path, mode, FileAccess.Write, FileShare.Read);
                writer = new StreamWriter(stream, new UTF8Encoding(false), 64 * 1024);
                fileBytes = 0;
                WriteLine("{\"type\":\"header\",\"format\":\"bs-bridge-trace\",\"version\":1,\"side\":\"unity\",\"startedUtc\":\"" +
                          DateTime.UtcNow.AddMilliseconds(-Ms(Stopwatch.GetTimestamp())).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture) +
                          "\",\"pid\":" + Pid().ToString(CultureInfo.InvariantCulture) +
                          ",\"maxPayloadChars\":" + options.MaxPayloadChars.ToString(CultureInfo.InvariantCulture) +
                          ",\"payloads\":" + (options.IncludePayloads ? "true" : "false") + "}");
            }

            double Ms(long ticks) => (ticks - startTicks) * 1000.0 / Stopwatch.Frequency;

            void WriteLine(string line)
            {
                writer.Write(line);
                writer.Write('\n');
                fileBytes += Encoding.UTF8.GetByteCount(line) + 1;
            }

            void Rotate()
            {
                writer.Flush();
                writer.Dispose();
                var previous = Path + ".1";
                try
                {
                    if (File.Exists(previous)) File.Delete(previous);
                    File.Move(Path, previous);
                }
                catch (IOException) { }
                Open(FileMode.Create);
            }

            void Run()
            {
                var sb = new StringBuilder(8 * 1024);
                while (true)
                {
                    signal.WaitOne(200);
                    var stop = stopping;
                    try
                    {
                        Drain(sb);
                        writer.Flush();
                    }
                    catch (Exception)
                    {
                        // Disk full, file deleted under us: stop tracing rather than throw on a background thread.
                        stopping = true;
                        stop = true;
                    }
                    if (stop)
                    {
                        try
                        {
                            WriteLine("{\"type\":\"end\",\"t\":" + Num(Ms(Stopwatch.GetTimestamp())) + ",\"written\":" + written + ",\"dropped\":" + Interlocked.Read(ref DroppedTotal) + "}");
                            writer.Flush();
                            writer.Dispose();
                        }
                        catch (Exception) { }
                        return;
                    }
                }
            }

            void Drain(StringBuilder sb)
            {
                var dropped = Interlocked.Exchange(ref droppedSinceLast, 0);
                if (dropped > 0)
                    WriteLine("{\"type\":\"dropped\",\"count\":" + dropped + ",\"t\":" + Num(Ms(Stopwatch.GetTimestamp())) + "}");
                while (queue.TryDequeue(out var item))
                {
                    Interlocked.Decrement(ref queuedRecords);
                    Interlocked.Add(ref queuedChars, -(item.Text?.Length ?? item.Bytes?.Length ?? 0));
                    sb.Clear();
                    sb.Append("{\"seq\":").Append(item.Seq)
                      .Append(",\"t\":").Append(Num(Ms(item.Ticks)))
                      .Append(",\"dir\":\"").Append(item.Dir == Direction.In ? "in" : "out")
                      .Append("\",\"len\":").Append(item.Length);
                    if (item.Lane != null) { sb.Append(",\"lane\":"); AppendString(sb, item.Lane); }
                    if (options.IncludePayloads)
                    {
                        if (item.Text != null)
                        {
                            var text = item.Text;
                            if (options.Redact != null)
                            {
                                try { text = options.Redact(text); }
                                catch (Exception) { text = null; }
                            }
                            if (text != null)
                            {
                                var cut = text.Length > options.MaxPayloadChars;
                                sb.Append(",\"data\":");
                                AppendString(sb, cut ? text.Substring(0, options.MaxPayloadChars) : text);
                                if (cut) sb.Append(",\"cut\":true");
                            }
                        }
                        else if (item.Bytes != null)
                        {
                            sb.Append(",\"enc\":\"b64\",\"data\":\"").Append(Convert.ToBase64String(item.Bytes)).Append('"');
                        }
                    }
                    sb.Append('}');
                    WriteLine(sb.ToString());
                    written++;
                    if (fileBytes >= options.MaxFileBytes)
                        Rotate();
                }
            }

            public void Stop()
            {
                stopping = true;
                signal.Set();
                if (Thread.CurrentThread != thread)
                    thread.Join(2000);
            }

            static string Num(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

            static void AppendString(StringBuilder sb, string s)
            {
                sb.Append('"');
                foreach (var c in s)
                {
                    switch (c)
                    {
                        case '"': sb.Append("\\\""); break;
                        case '\\': sb.Append("\\\\"); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        default:
                            // Control characters (the wire's pilcrow-style delimiters are printable; ASCII control
                            // codes and lone surrogates are escaped so every line stays valid JSON).
                            if (c < 0x20 || char.IsSurrogate(c))
                                sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                            else
                                sb.Append(c);
                            break;
                    }
                }
                sb.Append('"');
            }
        }
    }
}

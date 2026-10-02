// <mirror source="Packages/com.sidequest.packetparty/Runtime/Signaling/PacketPartySignaling.cs" sha256="d6f5b8c336353a6bca23abc1482305522d36a9bf4a712e852332a9e7b5c7ada7" mode="port" />
// <mirror source="Packages/com.sidequest.packetparty/Runtime/Signaling/SignalEnvelope.cs" sha256="9204b842ddc69c26a317ca62fac398ed2c4e2da239b4bf9957e00fc3349a222f" mode="port" />
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BS.LocalMultiplayer
{
    internal enum RelayInboundKind { Opened, Message, Participant, Objects, Closed }

    /// <summary>How a connection ended, which decides the session's retry (RELAY.md 9).</summary>
    internal enum RelayCloseKind
    {
        /// <summary>Nothing answered on the port (or the dial timed out).</summary>
        Refused,
        /// <summary>Something answered, but refused the WebSocket upgrade (HTTP status): no relay there.</summary>
        Rejected,
        /// <summary>The relay closed the socket with a close code.</summary>
        Closed,
        /// <summary>The socket broke (1006), or a send or a message failed.</summary>
        Failed,
    }

    /// <summary>One thing the transport hands the session; the session drains them in order on the main thread.</summary>
    internal sealed class RelayInbound
    {
        public RelayInboundKind Kind;
        /// <summary>The <see cref="RelayConnection.Id"/> it came from: the session drops a stale socket's leftovers.</summary>
        public int Connection;
        public string Type;
        public JObject Body;
        public string From;
        public ParticipantFrame Participant;
        public ObjectFrame[] Objects;
        /// <summary><see cref="MachineClock.NowMs"/> when the whole frame arrived, before parsing (the room clock's receive time).</summary>
        public double ReceivedAtMs;
        public string Uri;
        public RelayCloseKind CloseKind;
        public int CloseCode;
        public string CloseReason;
        public int HttpStatus;
    }

    /// <summary>
    /// One WebSocket to the relay (RELAY.md 1): a ClientWebSocket with a background receive loop and a single
    /// writer send loop, the part of PacketPartySignaling the host needs. It dials 127.0.0.1 and then [::1]
    /// (the hub binds both), sends hello before anything else can be written, and then sends ordered reliable
    /// traffic followed by the two latest-wins hot slots. JSON is parsed off the main thread: the hot lanes
    /// straight into structs, everything else into a JObject. Nothing here touches Unity; results go to the
    /// session's queue. A connection is never reused: each attempt gets a fresh one, so nothing queued for an
    /// old session can reach the relay before a new hello.
    /// </summary>
    internal sealed class RelayConnection
    {
        /// <summary>A full 2048-key, 16 KiB-a-leaf space-state snapshot fits.</summary>
        public const int MaxMessageBytes = 64 * 1024 * 1024;

        static readonly string[] Hosts = { "127.0.0.1", "[::1]" };
        static readonly TimeSpan DialTimeout = TimeSpan.FromSeconds(3);
        static readonly TimeSpan GracefulCloseTimeout = TimeSpan.FromMilliseconds(500);
        static readonly Regex HttpStatusPattern = new Regex(@"status code '(\d{3})'", RegexOptions.CultureInvariant);

        struct Outbound
        {
            public string Text;
            public Action<double> OnSending;
        }

        readonly int _id;
        readonly int _port;
        readonly ConcurrentQueue<RelayInbound> _inbound;
        readonly CancellationTokenSource _cts = new CancellationTokenSource();
        readonly ConcurrentQueue<Outbound> _reliable = new ConcurrentQueue<Outbound>();
        readonly SemaphoreSlim _signal = new SemaphoreSlim(0, int.MaxValue);
        // The receive loop's parsing buffers, reused for every hot frame: only that one loop parses, one message
        // after another, and each Post gets its own copy of the object frames.
        readonly RelayFrameCodec.ReadBuffers _readBuffers = new RelayFrameCodec.ReadBuffers();
        readonly List<ObjectFrame> _objectFrames = new List<ObjectFrame>(4);
        string _participantSlot;
        string _objectsSlot;
        ClientWebSocket _socket;
        // The socket calls in flight, so teardown can mark their failures observed before it aborts them: their
        // await continuations may never run if Unity unloads the domain right after (see TaskFaults).
        Task _pendingReceive;
        Task _pendingSend;
        int _closeRequested;
        int _closedReported;
        long _invalidMessages;
        volatile string _uri;

        public RelayConnection(int id, int port, ConcurrentQueue<RelayInbound> inbound)
        {
            _id = id;
            _port = port;
            _inbound = inbound ?? throw new ArgumentNullException(nameof(inbound));
        }

        public int Id => _id;

        /// <summary>The address that answered, once open.</summary>
        public string Uri => _uri;

        /// <summary>Messages that were not JSON objects with a type (RELAY.md 1: ignored and counted).</summary>
        public long InvalidMessages => Interlocked.Read(ref _invalidMessages);

        /// <summary>True when the last tf.objects message has gone out and the slot takes a new one.</summary>
        public bool ObjectsSlotFree => Volatile.Read(ref _objectsSlot) == null;

        public static string AddressFor(string host, int port) => "ws://" + host + ":" + port + RelayProtocol.WsPath;

        /// <summary>A socket set up for the relay: its subprotocol, and no proxy, as the hub probe (a system proxy
        /// would take the localhost connection and never reach the relay, while the probe still says Ready).</summary>
        internal static ClientWebSocket CreateSocket()
        {
            var socket = new ClientWebSocket();
            socket.Options.AddSubProtocol(RelayProtocol.SubProtocol);
            socket.Options.Proxy = null;
            return socket;
        }

        /// <summary>Dials and sends <paramref name="helloText"/> on a background task.</summary>
        public void Start(string helloText)
        {
            Task.Run(() => RunAsync(helloText));
        }

        /// <summary>Queues a reliable message, sent in order. <paramref name="onSending"/> gets the send time
        /// (taken right before the socket write, as PacketPartySignaling.RequestTimedAsync does).</summary>
        public void EnqueueReliable(string text, Action<double> onSending = null)
        {
            if (Volatile.Read(ref _closeRequested) != 0 || text == null) return;
            _reliable.Enqueue(new Outbound { Text = text, OnSending = onSending });
            Signal();
        }

        /// <summary>The latest pose wins: a pose still waiting to go out is replaced.</summary>
        public void SetParticipant(string text)
        {
            if (Volatile.Read(ref _closeRequested) != 0 || text == null) return;
            Interlocked.Exchange(ref _participantSlot, text);
            Signal();
        }

        /// <summary>Takes a tf.objects message only when the previous one has gone out; the session keeps
        /// collecting the latest frame per object meanwhile, so no object's frame is lost.</summary>
        public bool TrySetObjects(string text)
        {
            if (Volatile.Read(ref _closeRequested) != 0 || text == null) return false;
            if (Interlocked.CompareExchange(ref _objectsSlot, text, null) != null) return false;
            Signal();
            return true;
        }

        /// <summary>
        /// Ends the connection without blocking the main thread. Graceful sends the close frame from a
        /// background task (at most 0.5 s, then aborts); immediate aborts now (script reload, quit). Either way
        /// the relay sees the socket close, which is the only way to leave (RELAY.md 3).
        /// </summary>
        public void Close(bool immediate)
        {
            if (Interlocked.Exchange(ref _closeRequested, 1) != 0) return;
            var socket = Volatile.Read(ref _socket);
            if (immediate || socket == null)
            {
                Cancel();
                Abort(socket);
                return;
            }
            Task.Run(async () =>
            {
                try
                {
                    if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
                    {
                        using (var timeout = new CancellationTokenSource(GracefulCloseTimeout))
                        {
                            await TaskFaults.Observe(socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "client_disconnect", timeout.Token)).ConfigureAwait(false);
                        }
                    }
                }
                catch (Exception)
                {
                    // Best effort: the abort below closes it anyway.
                }
                finally
                {
                    Cancel();
                    Abort(socket);
                }
            });
        }

        async Task RunAsync(string helloText)
        {
            var token = _cts.Token;
            ClientWebSocket socket = null;
            RelayInbound failure = null;
            foreach (var host in Hosts)
            {
                if (token.IsCancellationRequested) break;
                var address = AddressFor(host, _port);
                var candidate = CreateSocket();
                try
                {
                    using (var dial = CancellationTokenSource.CreateLinkedTokenSource(token))
                    {
                        dial.CancelAfter(DialTimeout);
                        await TaskFaults.Observe(candidate.ConnectAsync(new Uri(address), dial.Token)).ConfigureAwait(false);
                    }
                    socket = candidate;
                    _uri = address;
                    break;
                }
                catch (Exception e)
                {
                    Abort(candidate);
                    failure = MoreInformative(failure, DescribeDialFailure(e, address));
                }
            }

            if (socket == null)
            {
                Report(failure ?? Closed(RelayCloseKind.Refused, 0, "couldn't connect", 0));
                return;
            }

            Volatile.Write(ref _socket, socket);
            if (Volatile.Read(ref _closeRequested) != 0)
            {
                // Close() raced the dial.
                Abort(socket);
                return;
            }

            var buffer = new byte[16 * 1024];
            try
            {
                // hello goes out before the send loop starts, so it is always the socket's first message.
                buffer = await SendTextAsync(socket, helloText, buffer, null, token).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Report(Closed(RelayCloseKind.Failed, RelayProtocol.CloseAbnormal, "hello not sent: " + e.Message, 0));
                Cancel();
                Abort(socket);
                return;
            }

            Post(new RelayInbound { Kind = RelayInboundKind.Opened, Uri = _uri });
            _ = Task.Run(() => ReceiveLoopAsync(socket, token));
            await SendLoopAsync(socket, buffer, token).ConfigureAwait(false);
        }

        async Task SendLoopAsync(ClientWebSocket socket, byte[] buffer, CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await _signal.WaitAsync(token).ConfigureAwait(false);
                    // Reliable traffic first, in order; then the hot slots, whose latest value is all that matters.
                    while (Volatile.Read(ref _closeRequested) == 0 && _reliable.TryDequeue(out var item))
                    {
                        buffer = await SendTextAsync(socket, item.Text, buffer, item.OnSending, token).ConfigureAwait(false);
                    }
                    if (Volatile.Read(ref _closeRequested) != 0) return;
                    var participant = Interlocked.Exchange(ref _participantSlot, null);
                    if (participant != null)
                    {
                        buffer = await SendTextAsync(socket, participant, buffer, null, token).ConfigureAwait(false);
                    }
                    var objects = Interlocked.Exchange(ref _objectsSlot, null);
                    if (objects != null)
                    {
                        buffer = await SendTextAsync(socket, objects, buffer, null, token).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                Report(Closed(RelayCloseKind.Failed, RelayProtocol.CloseAbnormal, "send failed: " + e.Message, 0));
                Cancel();
                Abort(socket);
            }
        }

        async Task<byte[]> SendTextAsync(ClientWebSocket socket, string text, byte[] buffer, Action<double> onSending, CancellationToken token)
        {
            var count = Encoding.UTF8.GetByteCount(text);
            if (buffer.Length < count)
            {
                buffer = new byte[Math.Max(count, buffer.Length * 2)];
            }
            Encoding.UTF8.GetBytes(text, 0, text.Length, buffer, 0);
            onSending?.Invoke(MachineClock.NowMs);
            var send = socket.SendAsync(new ArraySegment<byte>(buffer, 0, count), WebSocketMessageType.Text, true, token);
            Volatile.Write(ref _pendingSend, send);
            await send.ConfigureAwait(false);
            // Don't hold on to a one-off giant buffer (a big state batch) for the rest of the session.
            return buffer.Length > 1024 * 1024 ? new byte[16 * 1024] : buffer;
        }

        async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken token)
        {
            var buffer = new byte[64 * 1024];
            var message = new MemoryStream();
            var kind = RelayCloseKind.Failed;
            var code = RelayProtocol.CloseAbnormal;
            string reason = null;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var receive = socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                    Volatile.Write(ref _pendingReceive, receive);
                    var result = await receive.ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        kind = RelayCloseKind.Closed;
                        code = result.CloseStatus.HasValue ? (int)result.CloseStatus.Value : RelayProtocol.CloseNoStatus;
                        reason = result.CloseStatusDescription ?? "";
                        break;
                    }
                    if (result.Count > 0)
                    {
                        message.Write(buffer, 0, result.Count);
                    }
                    if (message.Length > MaxMessageBytes)
                    {
                        kind = RelayCloseKind.Failed;
                        code = 1009;
                        reason = "a message over 64 MiB";
                        break;
                    }
                    if (!result.EndOfMessage) continue;

                    var receivedAt = MachineClock.NowMs;
                    if (result.MessageType == WebSocketMessageType.Text && message.Length > 0)
                    {
                        var text = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                        Parse(text, receivedAt);
                    }
                    // Keep the usual small buffer; drop the one a big snapshot grew.
                    if (message.Capacity > 1024 * 1024)
                    {
                        message = new MemoryStream();
                    }
                    else
                    {
                        message.SetLength(0);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                if (Volatile.Read(ref _closeRequested) != 0) return;
                reason = "cancelled";
            }
            catch (Exception e)
            {
                kind = RelayCloseKind.Failed;
                code = RelayProtocol.CloseAbnormal;
                reason = e.Message;
            }
            Report(Closed(kind, code, reason, 0));
            Cancel();
            Abort(socket);
        }

        // Off the main thread: hot frames into structs, the rest into a JObject (SignalEnvelope.Parse).
        void Parse(string text, double receivedAt)
        {
            if (RelayFrameCodec.LooksLikeParticipant(text))
            {
                if (RelayFrameCodec.TryReadParticipant(text, out var from, out var frame, _readBuffers))
                {
                    Post(new RelayInbound { Kind = RelayInboundKind.Participant, From = from, Participant = frame, ReceivedAtMs = receivedAt });
                }
                else
                {
                    Interlocked.Increment(ref _invalidMessages);
                }
                return;
            }
            if (RelayFrameCodec.LooksLikeObjects(text))
            {
                _objectFrames.Clear();
                if (RelayFrameCodec.TryReadObjects(text, out var from, _objectFrames, _readBuffers))
                {
                    Post(new RelayInbound { Kind = RelayInboundKind.Objects, From = from, Objects = _objectFrames.ToArray(), ReceivedAtMs = receivedAt });
                }
                else
                {
                    Interlocked.Increment(ref _invalidMessages);
                }
                return;
            }

            JObject body;
            try
            {
                // As production: JToken.Parse with Json.NET's defaults.
                body = JToken.Parse(text) as JObject;
            }
            catch (JsonException)
            {
                body = null;
            }
            var type = body != null ? RelayProtocol.Str(body, "type") : "";
            if (string.IsNullOrEmpty(type))
            {
                Interlocked.Increment(ref _invalidMessages);
                return;
            }

            // A hot frame whose type wasn't written first (another writer): convert it here, still off the main thread.
            if (type == RelayProtocol.ParticipantFrame)
            {
                if (RelayFrameCodec.TryReadParticipant(body, out var from, out var frame, _readBuffers))
                {
                    Post(new RelayInbound { Kind = RelayInboundKind.Participant, From = from, Participant = frame, ReceivedAtMs = receivedAt });
                }
                else
                {
                    Interlocked.Increment(ref _invalidMessages);
                }
                return;
            }
            if (type == RelayProtocol.ObjectFrames)
            {
                _objectFrames.Clear();
                if (RelayFrameCodec.TryReadObjects(body, out var from, _objectFrames, _readBuffers))
                {
                    Post(new RelayInbound { Kind = RelayInboundKind.Objects, From = from, Objects = _objectFrames.ToArray(), ReceivedAtMs = receivedAt });
                }
                else
                {
                    Interlocked.Increment(ref _invalidMessages);
                }
                return;
            }

            Post(new RelayInbound { Kind = RelayInboundKind.Message, Type = type, Body = body, ReceivedAtMs = receivedAt });
        }

        void Post(RelayInbound item)
        {
            item.Connection = _id;
            _inbound.Enqueue(item);
        }

        // Exactly one Closed per connection, whichever loop notices first.
        void Report(RelayInbound closed)
        {
            if (Interlocked.Exchange(ref _closedReported, 1) != 0) return;
            Post(closed);
        }

        static RelayInbound Closed(RelayCloseKind kind, int code, string reason, int httpStatus)
        {
            return new RelayInbound
            {
                Kind = RelayInboundKind.Closed,
                CloseKind = kind,
                CloseCode = code,
                CloseReason = reason ?? "",
                HttpStatus = httpStatus,
            };
        }

        static RelayInbound DescribeDialFailure(Exception e, string address)
        {
            // A server that isn't the relay answers the upgrade with an HTTP status ("The server returned status
            // code '404' when status code '101' was expected"); a closed port refuses or times out.
            for (var inner = e; inner != null; inner = inner.InnerException)
            {
                var match = HttpStatusPattern.Match(inner.Message ?? "");
                if (match.Success && int.TryParse(match.Groups[1].Value, out var status))
                {
                    return Closed(RelayCloseKind.Rejected, 0, $"{address} answered HTTP {status}", status);
                }
            }
            for (var inner = e; inner != null; inner = inner.InnerException)
            {
                if (inner is SocketException || inner is OperationCanceledException)
                {
                    return Closed(RelayCloseKind.Refused, 0, $"nothing answered at {address}", 0);
                }
            }
            return Closed(RelayCloseKind.Refused, 0, $"{address}: {e.Message}", 0);
        }

        // Keep the failure that says the most: an HTTP answer beats a refused port.
        static RelayInbound MoreInformative(RelayInbound current, RelayInbound candidate)
        {
            if (current == null) return candidate;
            if (candidate.CloseKind == RelayCloseKind.Rejected && current.CloseKind != RelayCloseKind.Rejected) return candidate;
            return current;
        }

        void Signal()
        {
            try
            {
                _signal.Release();
            }
            catch (SemaphoreFullException)
            {
                // The loop is already awake many times over.
            }
            catch (ObjectDisposedException)
            {
            }
        }

        // Every teardown path cancels before it aborts the socket, so this is where the calls in flight get their
        // failures marked observed.
        void Cancel()
        {
            TaskFaults.Observe(Volatile.Read(ref _pendingReceive));
            TaskFaults.Observe(Volatile.Read(ref _pendingSend));
            try
            {
                _cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        static void Abort(ClientWebSocket socket)
        {
            if (socket == null) return;
            try
            {
                socket.Abort();
            }
            catch (Exception)
            {
            }
            try
            {
                socket.Dispose();
            }
            catch (Exception)
            {
            }
        }
    }
}

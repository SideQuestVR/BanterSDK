using System;
using System.Collections.Generic;

namespace BS.Bridge
{
    internal sealed class BridgeViewOptions
    {
        /// <summary>Kill switch: false answers every HELLO with WELCOME(mode=legacy) and opens no session.</summary>
        public bool SessionsEnabled = true;
        /// <summary>
        /// Android's compat channel can deliver a page's first messages ahead of its LoadStarted. Then a HELLO only
        /// claims an unclaimed LoadStarted whose URL matches its href (same host); otherwise it waits for the next one.
        /// M4 (Ora's document-start signal) replaces this heuristic; Android stays on the legacy lane until then.
        /// </summary>
        public bool LoadStartedMayTrail;
        /// <summary>A hold that never sees its LoadStarted + OnLoad is released after this long (and counted).</summary>
        public int HoldTimeoutMs = 5000;
        /// <summary>
        /// Held bytes past which acks stop. 0 (the default): a held document's frames are never acknowledged, so the
        /// page's credit window bounds the hold, and an ack never covers work a CLOSE reports unconsumed.
        /// </summary>
        public int HoldAckLimit = 0;
        /// <summary>
        /// The hold's cap, in bytes, each item counting its payload plus a fixed per-item overhead. Unmarked strings (no
        /// credits) past it, counting everything held, are dropped and counted. Framed messages are also checked on their own account (the credit window normally keeps them far
        /// below it): one that would pass it RESETs the session, which re-sends it as strings.
        /// </summary>
        public long MaxHeldBytes = 4L * 1024 * 1024;
        /// <summary>The page's send credit (WELCOME.recvWindow).</summary>
        public int RecvWindow = BridgeWire.DefaultWindow;
        public int FragmentSize = BridgeWire.FragmentSize;
        /// <summary>Out-of-order frames kept per session. 0: Ora's carrier is FIFO, so a gap is a protocol error.</summary>
        public int ReorderFrames = 0;
        /// <summary>
        /// Per Flush call (session lane): payload bytes (text 2 per unit; headers not counted, so whole fragments fit)
        /// and frame count. The page's credit window is the flow control: a page that acknowledges promptly can take up
        /// to its whole window each frame (seven full 64K-char text frames, as each frame's header counts against the
        /// window), so this defaults to the window and only caps it.
        /// </summary>
        public int FlushMaxBytes = BridgeWire.MaxWindow;
        public int FlushMaxFrames = 64;
        /// <summary>
        /// Main-thread time per Flush call for encoding and posting frames (the sink's synchronous send included):
        /// what is left goes next frame, within credits. At least one frame always goes. 0 or less: no limit.
        /// </summary>
        public double FlushBudgetMs = 2.0;
        /// <summary>The budget's clock, in Stopwatch ticks (tests swap in a fake).</summary>
        public Func<long> FlushClock = System.Diagnostics.Stopwatch.GetTimestamp;
        /// <summary>Per Flush call on the legacy lane: chars in the one "‽" batch.</summary>
        public int LegacyBatchMaxChars = 256 * 1024;
        /// <summary>
        /// Queued for a document beyond this many bytes while it has acknowledged nothing new for OutboundStallMs
        /// (frozen, hidden, hostile): the session is reset to the legacy lane, which has no flow control, like today.
        /// A page that keeps acknowledging may have more queued (one large reply drains over several frames).
        /// </summary>
        public long MaxOutboundBytes = 8L * 1024 * 1024;
        public int OutboundStallMs = 5000;
        /// <summary>
        /// Queued for a document beyond this, acknowledging or not: reset. Twice the largest message a page reassembles
        /// (BridgeWire.MaxMessage), so no single reply the session lane can deliver reaches it.
        /// </summary>
        public long MaxOutboundHardBytes = 2L * BridgeWire.MaxMessage;
        public uint Families;
        public uint Caps;
        public BridgeReassembler.Budget ReassemblyBudget;
        public Action<string> Log;
    }

    internal sealed class BridgeStats
    {
        public long StringsIn, CarriersIn, LegacyBatchesIn, FramesOut, LegacyBatchesOut, TextsDispatched, OpsDispatched;
        public long BadCarriers, StaleFrames, HostOnlyFromPage, ProtocolResets, Duplicates;
        public long LateReplies, DuplicateReplies, ReusedRequestIds, DroppedForClosedDocument, DroppedOps;
        public long HeldItems, HoldTimeouts, HoldDiscarded, HoldOverflowDropped, HandlerErrors, Hellos, Refusals, Sessions;
        public long LegacyMalformedBatches, OutboundOverflows, Revivals, FlushBudgetStops;
    }

    /// <summary>
    /// The Unity end of the bridge for one Ora view. Owns document attribution (token + epoch, LoadStarted/OnLoad
    /// claims), the per-document hold, the inbound FIFO (carrier frames, legacy batches and raw strings in arrival
    /// order) and the single outbound queue that replaces BatchUpdater: any thread may Send; the main thread Flushes
    /// once per frame (PostLateUpdate), within per-call bounds.
    /// Threading: Receive and Send are thread-safe; everything else is main-thread only.
    /// </summary>
    internal sealed class BridgeView
    {
        readonly IBridgeSink sink;
        readonly IBridgeDispatcher dispatcher;
        readonly Func<long> clockMs;
        public readonly BridgeViewOptions Options;
        public readonly BridgeStats Stats = new BridgeStats();

        readonly object inLock = new object();
        Queue<string> inbound = new Queue<string>();
        readonly object outLock = new object();
        List<OutMsg> outbox = new List<OutMsg>();
        List<OutMsg> outSwap = new List<OutMsg>();
        /// <summary>Strings for the legacy lane, each with the document it is for (0 = whichever page is there).</summary>
        readonly Queue<(string text, uint epoch)> legacyBacklog = new Queue<(string, uint)>();

        readonly Queue<(BridgeSession session, BridgeItem item)> ready = new Queue<(BridgeSession, BridgeItem)>();
        readonly List<BridgeMessage> scratchMessages = new List<BridgeMessage>();
        readonly List<RawOp> scratchOps = new List<RawOp>();
        readonly List<string> scratchTexts = new List<string>();
        /// <summary>Scratch list slots kept between uses (more are released after a huge batch).</summary>
        internal const int ScratchKeep = 4096;
        readonly List<OutFrame> scratchFrames = new List<OutFrame>();
        readonly Queue<string> closedTokens = new Queue<string>();
        readonly HashSet<string> closedTokenSet = new HashSet<string>();

        BridgeSession current;
        uint epochCounter;
        int loadGen;
        int lastClaimedLoad;
        string lastLoadUrl;
        bool lastLoadOnLoadDone;
        bool paused;

        struct OutMsg
        {
            public string Text;
            public byte[] Op;
            public uint Epoch;
            public int LegacyReplyId;   // > 0: "!i!:<id>" reply text
            public uint TypedReplyId;   // > 0: REPLY op
        }

        public BridgeView(IBridgeSink sink, IBridgeDispatcher dispatcher, BridgeViewOptions options, Func<long> clockMs)
        {
            this.sink = sink;
            this.dispatcher = dispatcher;
            Options = options ?? new BridgeViewOptions();
            this.clockMs = clockMs;
        }

        public BridgeSession Current => current;
        public int InboundCount { get { lock (inLock) return inbound.Count; } }
        public int ReadyCount => ready.Count;
        public int LoadGeneration => loadGen;
        public int LegacyBacklogCount => legacyBacklog.Count;

        // =========================================================================================== inbound

        /// <summary>Every page message (Ora custom-message data), in arrival order. Any thread.</summary>
        public void Receive(string message)
        {
            if (message == null) return;
            lock (inLock) inbound.Enqueue(message);
        }

        /// <summary>
        /// Main thread: decodes and dispatches in arrival order until maxItems have run or outOfBudget says stop. The
        /// budget is checked after every unit of work (an ingest or a dispatch); at least one runs per call. Undecoded
        /// strings stay queued and unacknowledged, so credits slow the page down.
        /// </summary>
        public int Drain(int maxItems = int.MaxValue, Func<bool> outOfBudget = null)
        {
            int done = 0;
            while (done < maxItems)
            {
                if (ready.Count > 0)
                {
                    if (DispatchReady(ready.Dequeue())) done++;
                }
                else
                {
                    string next;
                    lock (inLock)
                    {
                        if (inbound.Count == 0) break;
                        next = inbound.Dequeue();
                    }
                    Ingest(next);
                }
                if (outOfBudget != null && outOfBudget()) break;
            }
            return done;
        }

        void Ingest(string msg)
        {
            Stats.StringsIn++;
            if (BridgeCarrier.IsCarrier(msg)) { IngestCarrier(msg); return; }
            if (msg.StartsWith(BridgeLegacyBatch.PageMarker, StringComparison.Ordinal))
            {
                Stats.LegacyBatchesIn++;
                scratchTexts.Clear();
                if (!BridgeLegacyBatch.Unpack(msg, BridgeLegacyBatch.PageMarker.Length, scratchTexts))
                {
                    Stats.LegacyMalformedBatches++;
                    Log("Dropped the rest of a malformed message batch: " + (msg.Length > 200 ? msg.Substring(0, 200) : msg));
                }
                foreach (var t in scratchTexts) RouteUnmarked(t);
                scratchTexts.Clear();
                // A huge batch must not leave its slots pinned for the life of the view.
                if (scratchTexts.Capacity > ScratchKeep) scratchTexts.Capacity = ScratchKeep;
                return;
            }
            RouteUnmarked(msg);
        }

        /// <summary>
        /// An unmarked string has no epoch. It belongs to the newest document: held with it while that document is
        /// held (keeping its FIFO), otherwise dispatched in that document's context, or without one.
        /// </summary>
        void RouteUnmarked(string text)
        {
            var s = current;
            if (s != null && s.IsLive && s.Held) { HoldItem(s, new BridgeItem(text), 0); return; }
            ready.Enqueue((s != null && s.IsLive ? s : null, new BridgeItem(text)));
        }

        void IngestCarrier(string msg)
        {
            Stats.CarriersIn++;
            // The header first: a frame for another document, or a kind a page may not send, is refused before its
            // body is decoded.
            if (!BridgeCarrier.TryPeekHeader(msg, out var peek, out _)) { Stats.BadCarriers++; return; }
            if (FrameKind.IsHostOnly(peek.Kind)) { Stats.HostOnlyFromPage++; return; }
            var s = current;
            if (peek.Epoch != 0 && (s == null || s.Epoch != peek.Epoch || s.State == BridgeSessionState.Closed || s.State == BridgeSessionState.Legacy))
            {
                Stats.StaleFrames++;
                return;
            }
            var e = BridgeCarrier.TryDecode(msg, out var f);
            if (e != BridgeError.None) { Stats.BadCarriers++; return; }
            var h = f.Header;
            if (h.Epoch == 0) { IngestSessionless(f); return; }
            long now = clockMs();
            s.LastHeardMs = now;
            if (h.Ack > s.Link.LastAckReceived) s.AckProgressMs = now;
            if (!s.Link.OnAck(h.Ack)) { ResetSession(s, BridgeReason.Protocol, "ack beyond the last frame sent"); return; }
            if (!s.HandshakeComplete && h.Ack >= 1)
            {
                s.HandshakeComplete = true;
                if (s.State == BridgeSessionState.AwaitingAck) s.State = BridgeSessionState.Open;
            }
            switch (h.Kind)
            {
                case FrameKind.Data:
                    IngestData(s, f);
                    return;
                case FrameKind.Credit:
                    s.Link.SendWindow = (int)Math.Min(BridgeOps.DecodeU32(f.Payload), BridgeWire.MaxWindow);
                    return;
                default:
                    return; // ACK, HEARTBEAT: the ack above is all they carry
            }
        }

        void IngestData(BridgeSession s, CarrierFrame f)
        {
            var h = f.Header;
            var link = s.Link;
            int cost = BridgeLink.FrameCost(h);
            // A farewell: the page is giving up (perhaps mid-message, perhaps credit-blocked).
            bool farewell = IsFarewell(f);
            // Host-enforced window: the page may never have more unacknowledged than we granted (one frame may always
            // go, and so may a farewell).
            if (!farewell && link.UnackedBytes > 0 && (long)link.UnackedBytes + cost > link.RecvWindow)
            {
                ResetSession(s, BridgeReason.Protocol, "credit window exceeded");
                return;
            }
            // A farewell abandons a partial message.
            if (farewell && link.Reassembler.Busy) link.Reassembler.Reset();
            link.AckFrozen = s.Held && s.HeldBytes >= Options.HoldAckLimit;
            scratchMessages.Clear();
            int dupBefore = link.Duplicates;
            var seg = f.IsText
                ? new Segment { Text = f.Source, Offset = f.BodyStart, Length = h.Length }
                : new Segment { Bytes = f.Frame, Offset = BridgeWire.HeaderBytes, Length = h.Length };
            var e = link.ReceiveData(h, seg, scratchMessages);
            Stats.Duplicates += link.Duplicates - dupBefore;
            uint overflowFrom = 0;
            foreach (var m in scratchMessages)
            {
                if (s.Held && s.State != BridgeSessionState.Closed && HeldFramedBytesAfter(s, m) > Options.MaxHeldBytes)
                {
                    overflowFrom = m.FirstSeq;
                    break;
                }
                foreach (var item in m.Items)
                {
                    if (s.State == BridgeSessionState.Closed) break;
                    if (!item.IsText && item.Opcode < BridgeOp.LegacyText && item.Opcode != BridgeOp.Bye)
                    {
                        SessionOp(s, item, h);
                        continue;
                    }
                    if (!s.Held) { ready.Enqueue((s, item)); continue; }
                    if (!item.IsText && item.Opcode == BridgeOp.Bye)
                    {
                        // The document leaves before anything of it ran: close now, as an epoch-0 BYE does, instead of
                        // holding a farewell behind work that will never run (and a page can't pile BYEs into the hold).
                        CloseSession(s, BridgeReason.PageHide, false);
                        break;
                    }
                    HoldItem(s, item, m.FirstSeq);
                }
            }
            scratchMessages.Clear();
            if (overflowFrom != 0)
            {
                // Not held, not run: the RESET reports it unconsumed, so the page re-sends it (and what follows) as
                // strings, which queue behind what is held, under the cap for unframed strings.
                ResetSession(s, BridgeReason.Overflow, "held frames over " + Options.MaxHeldBytes + " bytes", overflowFrom - 1);
                return;
            }
            if (e != BridgeError.None && s.State != BridgeSessionState.Closed) ResetSession(s, BridgeReason.Protocol, BridgeErrors.Name(e));
        }

        /// <summary>
        /// LEGACY/BYE alone in a small complete frame: a page's farewell (protocolError, declining a late WELCOME). A
        /// frame that carries anything else, or more than a control payload, is not one and gets no credit exemption.
        /// </summary>
        bool IsFarewell(CarrierFrame f)
        {
            var h = f.Header;
            if (f.IsText || (h.Flags & FrameFlags.Complete) != FrameFlags.Complete || h.Length > BridgeWire.MaxControlPayload) return false;
            scratchOps.Clear();
            if (BridgeOps.Decode(f.Payload, scratchOps) != BridgeError.None || scratchOps.Count == 0) return false;
            foreach (var op in scratchOps)
                if (op.Opcode != BridgeOp.Legacy && op.Opcode != BridgeOp.Bye) return false;
            return true;
        }

        /// <summary>The held framed bytes if message m were held too.</summary>
        static long HeldFramedBytesAfter(BridgeSession s, BridgeMessage m)
        {
            long total = s.HeldFramedBytes;
            foreach (var item in m.Items) total += HeldCost(item);
            return total;
        }

        /// <summary>
        /// What a held item pins beyond its payload: the string object and its queue slot (about 70 bytes per held
        /// 1-char string on 64-bit .NET 8). Charged per item so that many tiny items can't hide under the byte cap.
        /// </summary>
        const int HeldItemOverhead = 64;

        static long HeldCost(in BridgeItem item) => item.Cost + HeldItemOverhead;

        /// <summary>Session-plane ops run at ingest; BYE stays in order (it would otherwise drop the ops before it).</summary>
        void SessionOp(BridgeSession s, BridgeItem item, FrameHeader h)
        {
            switch ((int)item.Opcode)
            {
                case BridgeOp.Ping:
                {
                    long n = BridgeOps.DecodeU32(item.Payload);
                    if (n >= 0) s.Link.EnqueueOp(BridgeOps.Encode(BridgeOp.Pong, BridgeOps.EncodeU32((uint)n)));
                    return;
                }
                case BridgeOp.Legacy:
                    ToLegacy(s, h.Ack);
                    return;
                default:
                    return; // PONG, and HELLO/WELCOME/HOLDING out of place
            }
        }

        void IngestSessionless(CarrierFrame f)
        {
            if (f.Header.Kind != FrameKind.Data || f.IsText || f.Header.Flags != FrameFlags.Complete) { Stats.BadCarriers++; return; }
            scratchOps.Clear();
            var payload = f.Payload;
            if (BridgeOps.Decode(payload, scratchOps) != BridgeError.None) { Stats.BadCarriers++; return; }
            var ops = scratchOps.ToArray();
            foreach (var op in ops)
            {
                var p = payload.Slice(op.Offset, op.Length);
                switch ((int)op.Opcode)
                {
                    case BridgeOp.Hello:
                        // A page's HELLO is always its first DATA frame.
                        if (f.Header.Seq != 1) { Refuse(BridgeReason.Protocol); break; }
                        OnHello(HelloPayload.TryDecode(p));
                        break;
                    case BridgeOp.Legacy:
                    case BridgeOp.Bye:
                        if (FarewellPayload.TryDecode(p, out var fw) && current != null && current.DocToken == fw.DocToken && current.IsLive)
                        {
                            if (op.Opcode == BridgeOp.Legacy) ToLegacy(current, 0);
                            else if (current.Held) CloseSession(current, BridgeReason.PageHide, false);
                            // In order: what the page sent before its BYE still runs.
                            else ready.Enqueue((current, new BridgeItem(BridgeOp.Bye, Array.Empty<byte>(), 0, 0)));
                        }
                        break;
                }
            }
        }

        // ======================================================================================= handshake

        void OnHello(HelloPayload hello)
        {
            Stats.Hellos++;
            if (hello == null) { Refuse(BridgeReason.Protocol); return; }
            if (hello.ProtoMajor != BridgeWire.ProtoMajor) { Refuse(BridgeReason.Version); return; }
            if (!Options.SessionsEnabled)
            {
                Stats.Refusals++;
                PostSessionless(BridgeOp.Welcome, new WelcomePayload { Epoch = 0, Mode = WelcomePayload.ModeLegacy }.Encode());
                return;
            }
            if (hello.Href.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) { Refuse(BridgeReason.AboutDocument); return; }
            if (!hello.IsTop) { Refuse(BridgeReason.NotTop); return; }
            if (current != null && current.IsLive && current.DocToken == hello.DocToken)
            {
                // The same document again (a duplicate HELLO): same epoch, same answer.
                if (!current.OptOut && current.State == BridgeSessionState.AwaitingAck) PostWelcomeDirect(current);
                return;
            }
            if (closedTokenSet.Contains(hello.DocToken)) { Refuse(BridgeReason.Closed); return; }
            // A revival: a page whose session a navigation closed is still alive (that navigation never committed) and
            // asks again under "<old token>~n". It may never displace a newer document, nor take the load that closed it.
            bool revival = IsRevivalOf(hello.DocToken);
            if (revival && current != null && current.IsLive) { Refuse(BridgeReason.Superseded); return; }

            if (current != null && current.IsLive) CloseSession(current, BridgeReason.Superseded, true);
            var link = new BridgeLink(Options.FragmentSize, Options.RecvWindow,
                (int)Math.Max(4096, Math.Min(hello.RecvWindow, (uint)BridgeWire.MaxWindow)), Options.ReassemblyBudget, Options.ReorderFrames);
            var s = new BridgeSession(++epochCounter, hello.DocToken, hello.Href, link);
            link.LastSeqReceived = 1;
            link.ConsumedSeq = 1;
            link.AckSeq = 1;
            link.LastCompleteSeq = 1;
            s.LastHeardMs = clockMs();
            s.AckProgressMs = s.LastHeardMs;
            Stats.Sessions++;
            s.Revival = revival;
            if (revival) { Stats.Revivals++; Wait(s); }
            else Claim(s);
            current = s;
            if ((hello.Features & BridgeFeature.OptOut) != 0)
            {
                s.OptOut = true;
                s.State = BridgeSessionState.Legacy;
                return;
            }
            s.State = BridgeSessionState.AwaitingAck;
            link.EnqueueOp(BridgeOps.Encode(BridgeOp.Welcome, WelcomeFor(s).Encode()));
        }

        bool IsRevivalOf(string token)
        {
            int i = token.IndexOf('~');
            return i > 0 && closedTokenSet.Contains(token.Substring(0, i));
        }

        WelcomePayload WelcomeFor(BridgeSession s) => new WelcomePayload
        {
            Epoch = s.Epoch, Mode = WelcomePayload.ModeV2, Lane = WelcomePayload.LaneCarrier, Families = Options.Families,
            Caps = Options.Caps, Features = BridgeFeature.TextForm, RecvWindow = (uint)Options.RecvWindow,
            MaxFramePayload = BridgeWire.MaxFramePayload, FragmentSize = (uint)Options.FragmentSize,
        };

        void PostWelcomeDirect(BridgeSession s)
        {
            var body = BridgeOps.Encode(BridgeOp.Welcome, WelcomeFor(s).Encode());
            Post(BridgeCarrier.EncodeBinary(new FrameHeader(FrameKind.Data, FrameFlags.Complete, s.Epoch, 1, s.Link.AckSeq, 0), body));
        }

        void Refuse(byte reason)
        {
            Stats.Refusals++;
            PostSessionless(BridgeOp.Bye, new FarewellPayload { Reason = reason, DocToken = string.Empty }.Encode());
        }

        void PostSessionless(int opcode, byte[] payload)
        {
            var body = BridgeOps.Encode((uint)opcode, payload);
            Post(BridgeCarrier.EncodeBinary(new FrameHeader(FrameKind.Data, FrameFlags.Complete, 0, 1, 1, 0), body));
        }

        // ======================================================================================= documents

        /// <summary>
        /// Which LoadStarted belongs to a new document. An unclaimed LoadStarted (one that came after the previous
        /// document's) is this document's, except on lanes where messages can overtake LoadStarted: there it must
        /// also match the href's host, or the document waits for the next LoadStarted.
        /// </summary>
        void Claim(BridgeSession s)
        {
            bool unclaimed = loadGen > lastClaimedLoad;
            if (unclaimed && (!Options.LoadStartedMayTrail || SameHost(lastLoadUrl, s.Href)))
            {
                s.ClaimedLoad = loadGen;
                lastClaimedLoad = loadGen;
                s.Held = !lastLoadOnLoadDone;
            }
            else Wait(s);
            if (s.Held) s.HeldSinceMs = clockMs();
        }

        /// <summary>Held until the next LoadStarted + OnLoad (or the hold timeout).</summary>
        void Wait(BridgeSession s)
        {
            s.ClaimedLoad = 0;
            s.Held = true;
            s.HeldSinceMs = clockMs();
        }

        /// <summary>
        /// The view started loading a page (Ora LoadStarted). Call before BSScene.OnLoad. Start-up (about:) and
        /// same-document navigations don't count.
        /// </summary>
        public void NotifyLoadStarted(string url, bool sameDocument = false)
        {
            if (sameDocument || string.IsNullOrEmpty(url) || url.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) return;
            loadGen++;
            lastLoadUrl = url;
            lastLoadOnLoadDone = false;
            var s = current;
            if (s == null || !s.IsLive) return;
            if (s.ClaimedLoad == 0 && !s.Revival)
            {
                // The document said HELLO first (Android): this is its LoadStarted.
                s.ClaimedLoad = loadGen;
                lastClaimedLoad = loadGen;
                return;
            }
            // The document that claimed an earlier load is being replaced (a revival had its load already: this one
            // is a newer document's). Its held ops and pending requests die with it; the next document's HELLO claims
            // this load.
            CloseSession(s, BridgeReason.Navigated, true);
        }

        /// <summary>BSScene.OnLoad has run for the latest LoadStarted (its reset is queued ahead of anything after it).</summary>
        public void NotifyOnLoad()
        {
            if (loadGen == 0) return;
            lastLoadOnLoadDone = true;
            var s = current;
            if (s != null && s.IsLive && s.Held && s.ClaimedLoad == loadGen) Release(s, false);
        }

        void Release(BridgeSession s, bool timedOut)
        {
            if (timedOut)
            {
                Stats.HoldTimeouts++;
                bool newerLoad = s.ClaimedLoad == 0 && loadGen > lastClaimedLoad;
                if (newerLoad && s.Revival)
                {
                    // A revival never takes a load: the one pending belongs to the navigation that replaced this
                    // document, which may just be slow. Releasing would run this document's work in that world, so it
                    // stays held until the newer document supersedes it, a LoadStarted closes it, or it says BYE.
                    s.HoldExpired = true;
                    Log($"[Bridge] Revived document epoch {s.Epoch} stays held after {Options.HoldTimeoutMs} ms: a newer load is pending ({s.Hold.Count} held).");
                    return;
                }
                if (newerLoad) { s.ClaimedLoad = loadGen; lastClaimedLoad = loadGen; }
                Log($"[Bridge] Released document epoch {s.Epoch} after {Options.HoldTimeoutMs} ms without its LoadStarted/OnLoad ({s.Hold.Count} held).");
            }
            s.Held = false;
            while (s.Hold.Count > 0) ready.Enqueue((s, s.Hold.Dequeue().Item));
            s.HeldBytes = 0;
            s.HeldFramedBytes = 0;
            s.FirstHeldFramedSeq = 0;
            s.Link.UnfreezeAck();
        }

        void HoldItem(BridgeSession s, BridgeItem item, uint firstSeq)
        {
            bool framed = firstSeq != 0;
            long cost = HeldCost(item);
            if (!framed && s.HeldBytes + cost > Options.MaxHeldBytes)
            {
                // Unmarked strings have no credits to slow them: past the cap they are dropped, as BSLink's legacy
                // hold rejects past 4096.
                Stats.HoldOverflowDropped++;
                if (!s.HoldOverflowLogged)
                {
                    s.HoldOverflowLogged = true;
                    Log($"[Bridge] Document epoch {s.Epoch} is held and over {Options.MaxHeldBytes} bytes: dropping unframed strings.");
                }
                return;
            }
            if (framed && s.FirstHeldFramedSeq == 0) s.FirstHeldFramedSeq = firstSeq;
            s.Hold.Enqueue(new HeldItem(item, framed));
            s.HeldBytes += cost;
            if (framed) s.HeldFramedBytes += cost;
            Stats.HeldItems++;
        }

        /// <summary>Timers (main thread, once per frame): the hold timeout.</summary>
        public void Tick()
        {
            var s = current;
            if (s == null || !s.IsLive || !s.Held || s.HoldExpired) return;
            if (clockMs() - s.HeldSinceMs >= Options.HoldTimeoutMs) Release(s, true);
        }

        /// <summary>The last of the page's DATA seqs whose content will run: held work is not consumed.</summary>
        static uint ReportedConsumed(BridgeSession s)
        {
            uint c = s.Link.LastCompleteSeq;
            if (s.FirstHeldFramedSeq != 0) c = Math.Min(c, s.FirstHeldFramedSeq - 1);
            return c;
        }

        /// <summary>
        /// The document has gone: drop its held ops, forget its requests, never answer them. Work already released to
        /// the ready queue counts as consumed (it is dropped at dispatch, not handed back): only held work is reported
        /// unconsumed, for the page to park.
        /// </summary>
        void CloseSession(BridgeSession s, byte reason, bool notify)
        {
            if (s.State == BridgeSessionState.Closed) return;
            bool wasFramed = s.State == BridgeSessionState.Open;
            uint consumed = ReportedConsumed(s);
            s.State = BridgeSessionState.Closed;
            s.CloseReason = reason;
            Stats.HoldDiscarded += s.Hold.Count;
            s.Hold.Clear();
            s.Held = false;
            s.HeldBytes = 0;
            s.HeldFramedBytes = 0;
            s.FirstHeldFramedSeq = 0;
            s.LegacyLedger.Clear();
            s.TypedLedger.Clear();
            s.Link.Reassembler.Reset();
            s.Link.TakeUnconsumed(uint.MaxValue);
            RememberClosed(s.DocToken);
            if (notify && wasFramed)
                PostClose(s, FrameKind.Close, consumed, BridgeFrame.EncodeClose(reason, consumed, string.Empty));
            if (current == s) current = null;
        }

        void ResetSession(BridgeSession s, byte reason, string why) => ResetSession(s, reason, why, s.Link.LastCompleteSeq);

        /// <summary>
        /// The page broke the protocol (or outran us) but the document lives on: RESET tells it the last of its frames
        /// we consumed so it re-sends the rest as strings; our unsent backlog moves to the legacy lane.
        /// A hold keeps everything in it, framed and unframed, in arrival order (the page does not re-send raw strings,
        /// so dropping only the framed part would let them overtake it): held framed work counts as consumed, and the
        /// page's re-sent strings queue behind it until the hold is released.
        /// </summary>
        void ResetSession(BridgeSession s, byte reason, string why, uint consumed)
        {
            Stats.ProtocolResets++;
            Log($"[Bridge] Reset document epoch {s.Epoch}: {why}");
            s.FirstHeldFramedSeq = 0;
            PostClose(s, FrameKind.Reset, consumed, BridgeFrame.EncodeClose(reason, consumed, why));
            // Everything we posted before the RESET reaches the page first and is consumed there; only the unsent
            // backlog moves to strings.
            ToLegacy(s, s.Link.NextSeq - 1);
        }

        /// <summary>
        /// Switch a live document to strings. consumedByPage: the last of our DATA seqs the page took; our frames after
        /// it never reached its parser, so their strings go again on the legacy lane, ahead of anything newer.
        /// </summary>
        void ToLegacy(BridgeSession s, uint consumedByPage)
        {
            if (!s.IsLive || s.State == BridgeSessionState.Legacy) return;
            s.State = BridgeSessionState.Legacy;
            s.Link.Reassembler.Reset();
            var rest = s.Link.TakeUnconsumed(consumedByPage);
            if (rest.Count == 0) return;
            var older = legacyBacklog.ToArray();
            legacyBacklog.Clear();
            foreach (var e in rest) if (e.IsText) legacyBacklog.Enqueue((e.Text, s.Epoch)); else Stats.DroppedOps++;
            foreach (var t in older) legacyBacklog.Enqueue(t);
        }

        void RememberClosed(string token)
        {
            if (!closedTokenSet.Add(token)) return;
            closedTokens.Enqueue(token);
            while (closedTokens.Count > 32) closedTokenSet.Remove(closedTokens.Dequeue());
        }

        // ======================================================================================= dispatch

        bool DispatchReady((BridgeSession session, BridgeItem item) r)
        {
            var s = r.session;
            if (s != null && s.State == BridgeSessionState.Closed) { Stats.DroppedForClosedDocument++; return false; }
            var item = r.item;
            using (BridgeContext.Enter(s?.Epoch ?? 0))
            {
                try
                {
                    if (item.IsText)
                    {
                        if (s != null) NoteLegacyRequest(s, item.Text);
                        Stats.TextsDispatched++;
                        dispatcher.DispatchText(s, item.Text);
                    }
                    else if (item.Opcode == BridgeOp.Bye)
                    {
                        if (s != null) CloseSession(s, BridgeReason.PageHide, false);
                    }
                    else if (item.Opcode == BridgeOp.LegacyText)
                    {
                        Stats.DroppedOps++; // invalid UTF-8
                    }
                    else
                    {
                        Stats.OpsDispatched++;
                        dispatcher.DispatchOp(s, item.Opcode, item.Payload);
                    }
                }
                catch (Exception ex)
                {
                    Stats.HandlerErrors++;
                    Log("[Bridge] Handler threw: " + ex);
                }
            }
            return true;
        }

        void NoteLegacyRequest(BridgeSession s, string text)
        {
            int id = LegacyRequestId(text, true);
            if (id <= 0 || id == NoReplyRequestId) return;
            if (!s.LegacyLedger.Add(id)) Stats.ReusedRequestIds++;
        }

        /// <summary>The page's request id that nothing waits on (scene.ts SendWithoutReply).</summary>
        public const int NoReplyRequestId = 10000;

        /// <summary>
        /// The id of "!i!:&lt;id&gt;¶…" (a request when requireDelimiter, which the page always sends) or "!i!:&lt;id&gt;"
        /// (a reply, possibly bare). 0 when the text is not one.
        /// </summary>
        public static int LegacyRequestId(string text, bool requireDelimiter)
        {
            const string prefix = "!i!:";
            if (text == null || text.Length <= prefix.Length || !text.StartsWith(prefix, StringComparison.Ordinal)) return 0;
            int id = 0, i = prefix.Length;
            for (; i < text.Length && text[i] >= '0' && text[i] <= '9'; i++)
            {
                if (id > 100_000_000) return 0;
                id = id * 10 + (text[i] - '0');
            }
            if (i == prefix.Length) return 0;
            if (i < text.Length ? text[i] != '¶' : requireDelimiter) return 0;
            return id;
        }

        // ======================================================================================= outbound

        /// <summary>
        /// Queue one legacy string for the page. Any thread. A reply ("!i!:&lt;id&gt;") is addressed to the document
        /// whose op is being handled (BridgeContext), so it is dropped if that document has gone, and sent once.
        /// </summary>
        public void Send(string text)
        {
            if (text == null) return;
            var m = new OutMsg { Text = text };
            int id = LegacyRequestId(text, false);
            if (id > 0) { m.LegacyReplyId = id; m.Epoch = BridgeContext.CurrentEpoch; }
            lock (outLock) outbox.Add(m);
        }

        /// <summary>Queue a typed op for the current document (M3). Any thread.</summary>
        public void SendOp(byte[] op)
        {
            lock (outLock) outbox.Add(new OutMsg { Op = op, Epoch = BridgeContext.CurrentEpoch });
        }

        /// <summary>The single reply to typed request reqId of document epoch. Any thread.</summary>
        public void Reply(uint epoch, uint reqId, byte status, ReadOnlySpan<byte> body)
        {
            var op = BridgeOps.Encode(BridgeOp.Reply, ReplyPayload.Encode(reqId, status, body));
            lock (outLock) outbox.Add(new OutMsg { Op = op, Epoch = epoch, TypedReplyId = reqId });
        }

        public bool HasOutbound
        {
            get
            {
                lock (outLock) if (outbox.Count > 0) return true;
                return legacyBacklog.Count > 0 || (current != null && current.IsLive && current.Link.HasOutbound);
            }
        }

        /// <summary>
        /// Main thread, PostLateUpdate: routes queued messages in order, then sends what the page's credit allows, at most
        /// FlushMaxBytes of payload / FlushMaxFrames frames, or one legacy batch of at most LegacyBatchMaxChars. What
        /// doesn't fit waits for the next call, so a burst spreads over frames instead of building giant ones.
        /// </summary>
        public int Flush()
        {
            List<OutMsg> batch;
            lock (outLock)
            {
                batch = outbox;
                outbox = outSwap;
                outSwap = batch;
            }
            var s = current;
            bool live = s != null && s.IsLive;
            bool open = live && s.State == BridgeSessionState.Open;
            foreach (var m in batch)
            {
                if (m.Epoch != 0 && (!live || s.Epoch != m.Epoch))
                {
                    Stats.LateReplies++;
                    continue;
                }
                // A legacy reply is sent once, to a request this document asked: a reply produced outside any context
                // (epoch 0) is checked against the current document's ledger too.
                if (m.LegacyReplyId > 0 && live && !s.LegacyLedger.Remove(m.LegacyReplyId))
                {
                    Stats.DuplicateReplies++;
                    continue;
                }
                if (m.TypedReplyId > 0 && (!live || !s.TypedLedger.Remove(m.TypedReplyId)))
                {
                    Stats.DuplicateReplies++;
                    continue;
                }
                if (m.Text != null)
                {
                    if (open) s.Link.EnqueueText(m.Text);
                    else legacyBacklog.Enqueue((m.Text, live ? s.Epoch : 0));
                }
                else if (open) s.Link.EnqueueOp(m.Op);
                else Stats.DroppedOps++; // typed ops have no legacy form
            }
            batch.Clear();
            if (open) CheckOutbound(s);
            if (paused) return 0;

            int posted = 0;
            if (legacyBacklog.Count > 0) posted += FlushLegacy();
            // Older strings still waiting on the legacy lane go first: frames wait until the backlog is out.
            s = current;
            if (legacyBacklog.Count == 0 && s != null && (s.State == BridgeSessionState.Open || s.State == BridgeSessionState.AwaitingAck))
            {
                // One frame at a time, so the caps apply to what is actually encoded and posted: payload, frame count
                // and main-thread time. A frame is only taken (given its seq) when it is about to go.
                long start = Options.FlushBudgetMs > 0 ? Options.FlushClock() : 0;
                long budget = Options.FlushBudgetMs > 0 ? (long)(Options.FlushBudgetMs * System.Diagnostics.Stopwatch.Frequency / 1000) : 0;
                long payload = 0;
                for (int n = 0; n < Options.FlushMaxFrames; n++)
                {
                    long next = s.Link.NextPayload();
                    if (next < 0) break;
                    if (n > 0 && payload + next > Options.FlushMaxBytes) break;
                    if (n > 0 && budget > 0 && Options.FlushClock() - start >= budget) { Stats.FlushBudgetStops++; break; }
                    scratchFrames.Clear();
                    if (s.Link.TakeFrames(scratchFrames, int.MaxValue, 1) == 0) break; // the credit window
                    Post(scratchFrames[0].ToCarrier());
                    scratchFrames.Clear();
                    Stats.FramesOut++;
                    posted++;
                    payload += next;
                }
                if (posted == 0 && s.HandshakeComplete && s.Link.NeedsAck())
                {
                    PostControl(s, FrameKind.Ack, Array.Empty<byte>());
                    posted++;
                }
            }
            return posted;
        }

        /// <summary>
        /// Outbound backpressure for an open document: what is queued for it may exceed MaxOutboundBytes while it keeps
        /// acknowledging (a large reply drains over several frames), but not for OutboundStallMs without progress, and
        /// never past MaxOutboundHardBytes. Either way the session goes to the legacy lane (no flow control, like today).
        /// </summary>
        void CheckOutbound(BridgeSession s)
        {
            var link = s.Link;
            long now = clockMs();
            if (link.InflightBytes == 0) s.AckProgressMs = now; // nothing of ours to acknowledge: not stalled
            long queued = link.QueuedCost;
            if (queued <= Options.MaxOutboundBytes) return;
            long stalledMs = now - s.AckProgressMs;
            bool stalled = !paused && stalledMs >= Options.OutboundStallMs;
            if (!stalled && queued <= Options.MaxOutboundHardBytes) return;
            Stats.OutboundOverflows++;
            ResetSession(s, BridgeReason.Overflow, stalled
                ? $"outbound queue over {Options.MaxOutboundBytes} bytes and nothing acknowledged for {stalledMs} ms"
                : $"outbound queue over {Options.MaxOutboundHardBytes} bytes");
        }

        int FlushLegacy()
        {
            var texts = scratchTexts;
            texts.Clear();
            int chars = 0;
            var s = current;
            while (legacyBacklog.Count > 0)
            {
                var next = legacyBacklog.Peek();
                // For a document that has gone since (a reply, or its unsent backlog): nobody to give it to.
                if (next.epoch != 0 && (s == null || !s.IsLive || s.Epoch != next.epoch))
                {
                    legacyBacklog.Dequeue();
                    Stats.LateReplies++;
                    continue;
                }
                int size = BridgeCarrier.EntrySize(next.text);
                if (texts.Count > 0 && chars + size > Options.LegacyBatchMaxChars) break;
                texts.Add(legacyBacklog.Dequeue().text);
                chars += size;
            }
            if (texts.Count == 0) return 0;
            Post(BridgeLegacyBatch.Pack(BridgeLegacyBatch.UnityMarker, texts));
            Stats.LegacyBatchesOut++;
            texts.Clear();
            return 1;
        }

        /// <summary>The application paused (OnApplicationPause / Editor pause): tell an open page, hold frames.</summary>
        public void Pause()
        {
            if (paused) return;
            paused = true;
            var s = current;
            if (s != null && s.State == BridgeSessionState.Open) PostControl(s, FrameKind.Pause, Array.Empty<byte>());
        }

        public void Resume()
        {
            if (!paused) return;
            paused = false;
            var s = current;
            if (s != null) s.AckProgressMs = clockMs(); // a pause is not a stall
            if (s != null && s.State == BridgeSessionState.Open) PostControl(s, FrameKind.Resume, Array.Empty<byte>());
        }

        /// <summary>
        /// The link is going away (its BSLink destroyed, play mode ended). Closes the current document without a word
        /// to it, which releases what it holds, including its share of a shared reassembly budget. Idempotent.
        /// </summary>
        public void Shutdown()
        {
            var s = current;
            if (s != null && s.IsLive) CloseSession(s, BridgeReason.Closed, false);
            current = null;
        }

        void PostControl(BridgeSession s, byte kind, byte[] payload)
        {
            var h = s.Link.ControlHeader(kind, payload.Length);
            Post(BridgeCarrier.EncodeBinary(h, payload));
            Stats.FramesOut++;
        }

        /// <summary>CLOSE/RESET: the header's ack is the consumed seq too, so it never acknowledges what it reports unconsumed.</summary>
        void PostClose(BridgeSession s, byte kind, uint consumed, byte[] payload)
        {
            var h = new FrameHeader(kind, 0, s.Epoch, 0, Math.Min(consumed, s.Link.AckSeq), payload.Length);
            Post(BridgeCarrier.EncodeBinary(h, payload));
            Stats.FramesOut++;
        }

        void Post(string data)
        {
            try { sink.Post(data); }
            catch (Exception ex) { Log("[Bridge] Post failed: " + ex.Message); }
        }

        void Log(string m) => Options.Log?.Invoke(m);

        static bool SameHost(string a, string b)
        {
            if (a == null || b == null) return false;
            string ha = Host(a), hb = Host(b);
            if (ha == null || hb == null) return StripFragment(a) == StripFragment(b);
            return string.Equals(ha, hb, StringComparison.OrdinalIgnoreCase);
        }

        static string StripFragment(string u) { int i = u.IndexOf('#'); return i < 0 ? u : u.Substring(0, i); }

        static string Host(string url)
        {
            int s = url.IndexOf("://", StringComparison.Ordinal);
            if (s < 0) return null;
            s += 3;
            int e = s;
            while (e < url.Length && url[e] != '/' && url[e] != '?' && url[e] != '#') e++;
            var authority = url.Substring(s, e - s);
            int at = authority.LastIndexOf('@');
            if (at >= 0) authority = authority.Substring(at + 1);
            int colon = authority.LastIndexOf(':');
            if (colon >= 0 && authority.IndexOf(']') < colon) authority = authority.Substring(0, colon);
            return authority;
        }
    }
}

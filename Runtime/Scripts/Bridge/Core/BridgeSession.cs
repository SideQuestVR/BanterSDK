using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;

namespace BS.Bridge
{
    internal enum BridgeSessionState
    {
        /// <summary>WELCOME queued; frames flow behind it until a page frame acknowledges the epoch.</summary>
        AwaitingAck,
        /// <summary>The page has acknowledged the epoch.</summary>
        Open,
        /// <summary>RESET sent: nothing goes to the page, and none of its frames count, until it says HELLO again.</summary>
        Resetting,
        /// <summary>The document is gone (navigated, superseded, BYE). Nothing for it is dispatched or sent.</summary>
        Closed,
    }

    /// <summary>
    /// One page document on one view: its token, epoch, transport link, the hold that keeps its ops until its
    /// LoadStarted and OnLoad have run, and its request ledgers (exactly one reply per request, never across documents).
    /// Plain C#: created and mutated only on the main thread by BridgeView.
    /// </summary>
    internal sealed class BridgeSession
    {
        public readonly uint Epoch;
        public readonly string DocToken;
        public readonly string Href;
        /// <summary>Replaced when the document starts its session over: the epoch and everything else here stay.</summary>
        public BridgeLink Link;
        public BridgeSessionState State;
        public bool HandshakeComplete;
        public byte CloseReason;

        /// <summary>The LoadStarted generation this document claimed; 0 while it waits for its LoadStarted.</summary>
        public int ClaimedLoad;
        /// <summary>A replaced document still alive ("&lt;token&gt;~n"). It never claims a load: see BridgeView.Release.</summary>
        public bool Revival;
        public bool Held;
        public long HeldSinceMs;
        /// <summary>The hold timed out but its release would run this document's work in a newer load's world: closed at RevivalHoldLimitMs.</summary>
        public bool HoldExpired;
        public long LastHeardMs;
        /// <summary>Held payload in bytes (text 2 per UTF-16 unit).</summary>
        public long HeldBytes;
        /// <summary>The framed part of HeldBytes (capped on its own account, so raw strings can't starve it).</summary>
        public long HeldFramedBytes;
        internal readonly Queue<HeldItem> Hold = new Queue<HeldItem>();
        /// <summary>First fragment seq of the first framed message held (0 = none): held work is not consumed work.</summary>
        public uint FirstHeldFramedSeq;
        public bool HoldOverflowLogged;
        public bool OutboundDropLogged;
        public bool ReplyFailLogged;
        /// <summary>Replies past MaxOutboundHardBytes since AckAtCap changed: what tells a reading page from one that isn't.</summary>
        public int RepliesPastCap;
        public uint AckAtCap;

        /// <summary>Legacy request ids ("!i!:&lt;id&gt;¶") dispatched for this document and not answered yet.</summary>
        internal readonly HashSet<int> LegacyLedger = new HashSet<int>();
        /// <summary>Typed request ids (M3) begun and not answered yet.</summary>
        internal readonly HashSet<uint> TypedLedger = new HashSet<uint>();
        /// <summary>CALL ops admitted and not run yet, in the hold or the ready queue.</summary>
        public int CallsQueued;
        /// <summary>CALL ops refused for the queue limit since the last one ran.</summary>
        public int CallsOverflowed;

        /// <summary>The families the last WELCOME offered this document.</summary>
        public uint Offered;
        /// <summary>The pushed families this document said ACCEPT to: Offered's pushed bits that the page reads.</summary>
        public uint Accepted;
        /// <summary>Scheduler work this document's ops queued and not started; null unless FENCE or DELTA is offered.</summary>
        public BridgeFence Fence;
        /// <summary>
        /// The page's items taken in, numbered from 1 in the order it sent them: strings and typed ops in its DATA frames,
        /// not session-plane ops. The page numbers them the same way, so a number names one of its sends.
        /// </summary>
        public uint ItemsIn;
        /// <summary>The number of the last of those dispatched (or refused).</summary>
        public uint ItemsDone;
        uint applied;
        /// <summary>
        /// DELTA's applied count: every item up to it has been dispatched and none of the scheduler work it queued is
        /// still to start (or the fence has expired). Written on the main thread, read anywhere.
        /// </summary>
        public uint Applied => Volatile.Read(ref applied);

        /// <summary>Moves Applied up to ItemsDone if no work they queued is still to start. Main thread.</summary>
        public void UpdateApplied()
        {
            var f = Fence;
            if (f == null || f.Pending == 0 || f.Expired) Volatile.Write(ref applied, ItemsDone);
        }

        public bool Accepts(uint family) => (Accepted & family) != 0;

        public BridgeSession(uint epoch, string docToken, string href, BridgeLink link)
        {
            Epoch = epoch;
            DocToken = docToken;
            Href = href;
            Link = link;
            link.Epoch = epoch;
        }

        public bool IsLive => State != BridgeSessionState.Closed;
        public int HeldCount => Hold.Count;
        public int OutstandingLegacyRequests => LegacyLedger.Count;

        /// <summary>A typed request is being handled; its single reply goes through BridgeView.Reply.</summary>
        public bool BeginRequest(uint reqId) => reqId != 0 && TypedLedger.Add(reqId);

        public override string ToString() => $"session epoch={Epoch} state={State} held={Held} claimed={ClaimedLoad}";
    }

    /// <summary>
    /// An item held for a document, whether it came in a frame (the page re-sends those after a RESET), and its number
    /// among the page's items (BridgeSession.ItemsIn; 0 for an unframed string, which the page does not number).
    /// </summary>
    internal readonly struct HeldItem
    {
        public readonly BridgeItem Item;
        public readonly bool Framed;
        public readonly uint Number;
        public HeldItem(BridgeItem item, bool framed, uint number = 0) { Item = item; Framed = framed; Number = number; }
    }

    /// <summary>
    /// The epoch of the document whose op is being dispatched, flowing with the ExecutionContext into awaits and tasks
    /// started by the handler, so a reply produced later is addressed to the document that asked (and dropped if that
    /// document has gone) instead of whichever page is current by then.
    /// </summary>
    internal static class BridgeContext
    {
        static readonly AsyncLocal<uint> epoch = new AsyncLocal<uint>();
        // The document's fence (FENCE/DELTA families), alongside its epoch; null otherwise, and then never written, so a
        // document without those families flows exactly what it did before.
        static readonly AsyncLocal<BridgeFence> fence = new AsyncLocal<BridgeFence>();

        public static uint CurrentEpoch => epoch.Value;
        public static BridgeFence CurrentFence => fence.Value;

        public static Scope Enter(uint e) => Enter(e, null);

        /// <param name="f">Counts the scheduler work queued from here on (Bind), until the scope ends; null: nothing counts.</param>
        public static Scope Enter(uint e, BridgeFence f)
        {
            var prev = epoch.Value;
            var prevFence = fence.Value;
            epoch.Value = e;
            if (!ReferenceEquals(prevFence, f)) fence.Value = f;
            return new Scope(prev, prevFence);
        }

        /// <summary>
        /// Until disposed, nothing queued here counts in the current document's fence: for work started now that serves
        /// every document rather than doing this one's op (BSScene's property worker). No fence set: nothing changes.
        /// </summary>
        public static FenceScope WithoutFence()
        {
            var f = fence.Value;
            if (f != null) fence.Value = null;
            return new FenceScope(f);
        }

        public readonly struct FenceScope : IDisposable
        {
            readonly BridgeFence prev;
            internal FenceScope(BridgeFence prev) { this.prev = prev; }
            public void Dispose() { if (prev != null) fence.Value = prev; }
        }

        public readonly struct Scope : IDisposable
        {
            readonly uint prev;
            readonly BridgeFence prevFence;
            internal Scope(uint prev, BridgeFence prevFence) { this.prev = prev; this.prevFence = prevFence; }
            public void Dispose()
            {
                epoch.Value = prev;
                if (!ReferenceEquals(fence.Value, prevFence)) fence.Value = prevFence;
            }
        }

        /// <summary>
        /// For queues that keep bare delegates (they don't carry the ExecutionContext): the action runs in the epoch
        /// current when it was queued. No epoch set (no op being handled) = the action itself. With a fence set, the
        /// action is counted in it from now until it starts.
        /// </summary>
        public static Action Bind(Action action)
        {
            uint e = epoch.Value;
            if (e == 0 || action == null) return action;
            var f = fence.Value;
            if (f != null) return Fenced(action, e, f);
            return () =>
            {
                var prev = epoch.Value;
                epoch.Value = e;
                try { action(); }
                finally { epoch.Value = prev; }
            };
        }

        static Action Fenced(Action action, uint e, BridgeFence f)
        {
            f.Add();
            int started = 0;
            return () =>
            {
                // Released as it starts: what it queues while it runs is counted in its turn.
                if (Interlocked.Exchange(ref started, 1) == 0) f.Release();
                var prev = epoch.Value;
                var prevFence = fence.Value;
                epoch.Value = e;
                if (!ReferenceEquals(prevFence, f)) fence.Value = f;
                try { action(); }
                finally
                {
                    epoch.Value = prev;
                    if (!ReferenceEquals(fence.Value, prevFence)) fence.Value = prevFence;
                }
            };
        }

        /// <summary>
        /// The same for a reply callback that the app may call later, from anywhere (BSStateRequest/BSHostRequest): it
        /// answers the document that asked. No epoch set = the callback itself.
        /// </summary>
        public static Action<T> Bind<T>(Action<T> callback)
        {
            uint e = epoch.Value;
            if (e == 0 || callback == null) return callback;
            return value =>
            {
                var prev = epoch.Value;
                epoch.Value = e;
                try { callback(value); }
                finally { epoch.Value = prev; }
            };
        }

        /// <summary>
        /// The same for a coroutine: every MoveNext (and nested coroutines it yields) runs in the epoch. With a fence
        /// set, the coroutine is counted in it until its first step starts.
        /// </summary>
        public static IEnumerator Bind(IEnumerator coroutine)
        {
            uint e = epoch.Value;
            if (e == 0 || coroutine == null) return coroutine;
            var f = fence.Value;
            f?.Add();
            return new EpochCoroutine(coroutine, e, f, f != null);
        }

        sealed class EpochCoroutine : IEnumerator
        {
            readonly IEnumerator inner;
            readonly uint e;
            readonly BridgeFence f;
            bool counted;
            object current;
            public EpochCoroutine(IEnumerator inner, uint e, BridgeFence f = null, bool counted = false)
            {
                this.inner = inner; this.e = e; this.f = f; this.counted = counted;
            }
            public object Current => current;
            public void Reset() => inner.Reset();
            public bool MoveNext()
            {
                if (counted)
                {
                    counted = false;
                    f.Release();
                }
                var prev = epoch.Value;
                var prevFence = fence.Value;
                epoch.Value = e;
                if (!ReferenceEquals(prevFence, f)) fence.Value = f;
                try
                {
                    bool more = inner.MoveNext();
                    var c = more ? inner.Current : null;
                    current = c is IEnumerator nested && !(c is EpochCoroutine) ? new EpochCoroutine(nested, e, f) : c;
                    return more;
                }
                finally
                {
                    epoch.Value = prev;
                    if (!ReferenceEquals(fence.Value, prevFence)) fence.Value = prevFence;
                }
            }
        }
    }

    /// <summary>Unity's view of one Ora view: where its strings go.</summary>
    internal interface IBridgeSink
    {
        /// <summary>One message to the page (OraView.Send: default-message). Called on the main thread.</summary>
        void Post(string data);
    }

    /// <summary>What runs the page's ops. Called on the main thread from BridgeView.Drain, inside a BridgeContext.</summary>
    internal interface IBridgeDispatcher
    {
        /// <summary>
        /// A command string: a LEGACY_TEXT op, or an unmarked string from a page script (O.sendMessage).
        /// session is null for a page that has no session (it never said HELLO).
        /// </summary>
        void DispatchText(BridgeSession session, string text);

        /// <summary>A typed op (M3 families). M1 defines none.</summary>
        void DispatchOp(BridgeSession session, uint opcode, ReadOnlySpan<byte> payload);
    }
}

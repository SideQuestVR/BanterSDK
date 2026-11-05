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

    /// <summary>An item held for a document, and whether it came in a frame (the page re-sends those after a RESET).</summary>
    internal readonly struct HeldItem
    {
        public readonly BridgeItem Item;
        public readonly bool Framed;
        public HeldItem(BridgeItem item, bool framed) { Item = item; Framed = framed; }
    }

    /// <summary>
    /// The epoch of the document whose op is being dispatched, flowing with the ExecutionContext into awaits and tasks
    /// started by the handler, so a reply produced later is addressed to the document that asked (and dropped if that
    /// document has gone) instead of whichever page is current by then.
    /// </summary>
    internal static class BridgeContext
    {
        static readonly AsyncLocal<uint> epoch = new AsyncLocal<uint>();

        public static uint CurrentEpoch => epoch.Value;

        public static Scope Enter(uint e)
        {
            var prev = epoch.Value;
            epoch.Value = e;
            return new Scope(prev);
        }

        public readonly struct Scope : IDisposable
        {
            readonly uint prev;
            internal Scope(uint prev) { this.prev = prev; }
            public void Dispose() => epoch.Value = prev;
        }

        /// <summary>
        /// For queues that keep bare delegates (they don't carry the ExecutionContext): the action runs in the epoch
        /// current when it was queued. No epoch set (no op being handled) = the action itself.
        /// </summary>
        public static Action Bind(Action action)
        {
            uint e = epoch.Value;
            if (e == 0 || action == null) return action;
            return () =>
            {
                var prev = epoch.Value;
                epoch.Value = e;
                try { action(); }
                finally { epoch.Value = prev; }
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

        /// <summary>The same for a coroutine: every MoveNext (and nested coroutines it yields) runs in the epoch.</summary>
        public static IEnumerator Bind(IEnumerator coroutine)
        {
            uint e = epoch.Value;
            if (e == 0 || coroutine == null) return coroutine;
            return new EpochCoroutine(coroutine, e);
        }

        sealed class EpochCoroutine : IEnumerator
        {
            readonly IEnumerator inner;
            readonly uint e;
            object current;
            public EpochCoroutine(IEnumerator inner, uint e) { this.inner = inner; this.e = e; }
            public object Current => current;
            public void Reset() => inner.Reset();
            public bool MoveNext()
            {
                var prev = epoch.Value;
                epoch.Value = e;
                try
                {
                    bool more = inner.MoveNext();
                    var c = more ? inner.Current : null;
                    current = c is IEnumerator nested && !(c is EpochCoroutine) ? new EpochCoroutine(nested, e) : c;
                    return more;
                }
                finally { epoch.Value = prev; }
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

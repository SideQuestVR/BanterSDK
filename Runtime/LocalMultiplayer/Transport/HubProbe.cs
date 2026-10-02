using System;
using System.Collections;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SideQuest.Ora;
using UnityEngine;

namespace BS.LocalMultiplayer
{
    /// <summary>
    /// Holds the page until the hub answers (BSNetworkHost.PageLoadGate). Every player loads its page from the
    /// main editor's Ora web server, which also runs the relay, so a clone that loads too early shows the
    /// error page and never reaches SCENE_READY. The probe GETs /__sq-relay/hub (RELAY.md 1) with HttpClient on
    /// a background task (Unity's own web requests refuse plain http in these projects) and the gate polls
    /// it every frame: Ready loads as soon as the view can take a URL; another project's hub, or a web server
    /// with no relay, is reported and loaded anyway after a short grace (the relay will refuse the hello and
    /// say why); nothing answering for 30 s loads anyway too, as before this existed.
    /// </summary>
    internal static class HubProbe
    {
        public const int PollMilliseconds = 250;
        public const int RequestTimeoutMilliseconds = 1500;
        public const float UnreachableSeconds = 30f;
        public const float MismatchGraceSeconds = 5f;
        public const float NoRelayGraceSeconds = 2f;
        public const float ViewInitialiseTimeoutSeconds = 30f;

        static readonly string[] Hosts = { "127.0.0.1", "[::1]" };

        /// <summary>The hub as last seen (the overlay's relay line). One instance, updated in place. Main thread.</summary>
        public static HubInfo Last { get; private set; } = new HubInfo();

        /// <summary>The web server port the page was loaded from; 0 until the gate ran.</summary>
        public static int PagePort { get; private set; }

        // The probe that may report, and what it said last. The loop runs on a background task, so both are
        // written under s_lock: a loop only reports while it is still the current probe, and a stopped or
        // replaced one can never overwrite what a newer probe said.
        static readonly object s_lock = new object();
        static CancellationTokenSource s_probe;
        static HubInfo s_latest;
#if UNITY_EDITOR
        static bool s_exitHooked;
#endif

        // Play mode without a domain reload keeps statics; start every session clean.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            StopProbe();
            Last = new HubInfo();
            PagePort = 0;
            lock (s_lock)
            {
                s_latest = null;
            }
        }

        /// <summary>BSNetworkHost.PageLoadGate. Yields until the page may load; never throws.</summary>
        public static IEnumerator Gate(OraView view, int port)
        {
            GateRun gate;
            try
            {
                gate = new GateRun(view, port);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                yield break;
            }
            while (gate.Step())
            {
                yield return null;
            }
        }

        /// <summary>Starts polling on a background task; the latest answer is in <see cref="Latest"/>.</summary>
        static void StartProbe(int port, string expectedRoot)
        {
            var cts = BeginProbe();
            Task.Run(() => ProbeLoopAsync(port, expectedRoot, cts));
        }

        /// <summary>Makes a new probe the current one, stopping the previous one, and clears the last answer.
        /// Main thread.</summary>
        internal static CancellationTokenSource BeginProbe()
        {
            StopProbe();
            var cts = new CancellationTokenSource();
            lock (s_lock)
            {
                s_probe = cts;
                s_latest = null;
            }
            HookPlayModeExit();
            return cts;
        }

        /// <summary>Cancels the current probe; its loop reports nothing more. Main thread.</summary>
        internal static void StopProbe()
        {
            CancellationTokenSource cts;
            lock (s_lock)
            {
                cts = s_probe;
                s_probe = null;
            }
            UnhookPlayModeExit();
            if (cts == null) return;
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        /// <summary>True while a probe runs.</summary>
        internal static bool Probing
        {
            get
            {
                lock (s_lock)
                {
                    return s_probe != null;
                }
            }
        }

        internal static HubInfo Latest
        {
            get
            {
                lock (s_lock)
                {
                    return s_latest;
                }
            }
        }

        /// <summary>Records <paramref name="owner"/>'s answer, if it is still the current probe. Any thread.</summary>
        internal static bool Publish(CancellationTokenSource owner, HubInfo info)
        {
            lock (s_lock)
            {
                if (owner == null || !ReferenceEquals(s_probe, owner)) return false;
                s_latest = info;
                return true;
            }
        }

        static async Task ProbeLoopAsync(int port, string expectedRoot, CancellationTokenSource owner)
        {
            var token = owner.Token;
            try
            {
                // No proxy: a system proxy would answer for localhost and hide the hub.
                using (var handler = new HttpClientHandler { UseProxy = false })
                using (var http = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(RequestTimeoutMilliseconds) })
                {
                    while (!token.IsCancellationRequested)
                    {
                        var info = await ProbeOnceAsync(http, port, expectedRoot, token).ConfigureAwait(false);
                        // Replaced or stopped while the request ran: a newer probe (or none) owns the answer now.
                        if (!Publish(owner, info)) return;
                        if (info.State == HubState.Ready) return;
                        await Task.Delay(PollMilliseconds, token).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                Publish(owner, new HubInfo { State = HubState.Waiting, Message = e.Message });
            }
        }

        // The gate's coroutine dies with Play, but the probe runs on a background task, and only the gate
        // stops it once it has an answer: a gate still waiting when Play stops (a clone whose main editor never
        // answered, say) would leave it polling localhost for the rest of edit mode. Stop it with Play.
        static void HookPlayModeExit()
        {
#if UNITY_EDITOR
            if (s_exitHooked) return;
            s_exitHooked = true;
            UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
        }

        static void UnhookPlayModeExit()
        {
#if UNITY_EDITOR
            if (!s_exitHooked) return;
            s_exitHooked = false;
            UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
#endif
        }

#if UNITY_EDITOR
        /// <summary>True while the probe listens for Play stopping (exactly while it runs).</summary>
        internal static bool StopsWithPlay => s_exitHooked;

        internal static void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange change)
        {
            if (change == UnityEditor.PlayModeStateChange.ExitingPlayMode)
            {
                StopProbe();
            }
        }
#endif

        // 127.0.0.1 first; [::1] when nothing answers there (a server without the relay binds one family only).
        static async Task<HubInfo> ProbeOnceAsync(HttpClient http, int port, string expectedRoot, CancellationToken token)
        {
            HubInfo waiting = null;
            foreach (var host in Hosts)
            {
                try
                {
                    using (var response = await TaskFaults.Observe(http.GetAsync("http://" + host + ":" + port + RelayProtocol.HubInfoPath, token)).ConfigureAwait(false))
                    {
                        var body = await TaskFaults.Observe(response.Content.ReadAsStringAsync()).ConfigureAwait(false);
                        return Classify((int)response.StatusCode, body, expectedRoot, port);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception e)
                {
                    waiting = new HubInfo { State = HubState.Waiting, Message = $"nothing answers at localhost:{port} yet ({Innermost(e).Message})" };
                }
            }
            return waiting ?? new HubInfo { State = HubState.Waiting };
        }

        /// <summary>What a /__sq-relay/hub answer means for this project.</summary>
        internal static HubInfo Classify(int status, string body, string expectedRoot, int port)
        {
            if (status != 200)
            {
                return new HubInfo
                {
                    State = HubState.NoRelay,
                    Message = $"localhost:{port} has no local multiplayer relay (HTTP {status}): Ora is older than 0.1.0, or local multiplayer is off in the main editor",
                };
            }
            JObject json = null;
            try
            {
                json = JToken.Parse(body ?? "") as JObject;
            }
            catch (JsonException)
            {
            }
            if (json == null || !RelayProtocol.Bool(json, "relay"))
            {
                return new HubInfo
                {
                    State = HubState.NoRelay,
                    Message = $"localhost:{port} answered, but not as a local multiplayer hub",
                };
            }
            var info = new HubInfo
            {
                ProjectRoot = RelayProtocol.Str(json, "projectRoot"),
                Pid = (int)RelayProtocol.Long(json, "pid"),
                ParentPid = (int)RelayProtocol.Long(json, "parentPid"),
            };
            if (json["persistence"] is JObject persistence)
            {
                info.PersistenceEnabled = RelayProtocol.Bool(persistence, "enabled");
                info.PersistenceDir = RelayProtocol.Str(persistence, "dir");
            }
            if (string.Equals(MppmEnvironment.NormalizeRoot(info.ProjectRoot), MppmEnvironment.NormalizeRoot(expectedRoot), StringComparison.Ordinal))
            {
                info.State = HubState.Ready;
                info.Message = $"hub ready at localhost:{port} (pid {info.Pid})";
            }
            else
            {
                info.State = HubState.ProjectMismatch;
                info.Message = $"localhost:{port} is the hub of another project, {info.ProjectRoot} (pid {info.Pid}): stop Play there";
            }
            return info;
        }

        static Exception Innermost(Exception e)
        {
            while (e.InnerException != null) e = e.InnerException;
            return e;
        }

        static void CopyInto(HubInfo from, HubInfo to)
        {
            to.State = from.State;
            to.Message = from.Message ?? "";
            to.ProjectRoot = from.ProjectRoot ?? "";
            to.Pid = from.Pid;
            to.ParentPid = from.ParentPid;
            to.PersistenceEnabled = from.PersistenceEnabled;
            to.PersistenceDir = from.PersistenceDir ?? "";
        }

        /// <summary>One run of the gate. Every step catches its own exceptions: a broken probe must still load the page.</summary>
        sealed class GateRun
        {
            readonly OraView _view;
            readonly int _port;
            readonly int _mainProcessId;
            readonly float _start;
            float _mismatchSince = -1f;
            float _noRelaySince = -1f;
            float _viewWaitSince = -1f;
            bool _probing = true;
            bool _warned;

            public GateRun(OraView view, int port)
            {
                _view = view;
                _port = port > 0 ? port : RelayProtocol.DefaultPort;
                PagePort = _port;
                var env = MppmEnvironment.Current;
                _mainProcessId = env.MainProcessId;
                _start = Time.realtimeSinceStartup;
                Last.State = HubState.Waiting;
                Last.Message = $"waiting for the hub at localhost:{_port}";
                StartProbe(_port, env.MainProjectRoot);
            }

            /// <summary>True while the page must wait.</summary>
            public bool Step()
            {
                try
                {
                    return _probing ? Probe() : WaitForView();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    StopProbe();
                    return false;
                }
            }

            bool Probe()
            {
                var now = Time.realtimeSinceStartup;
                var info = Latest;
                if (info != null)
                {
                    CopyInto(info, Last);
                }
                switch (info != null ? info.State : HubState.Waiting)
                {
                    case HubState.Ready:
                        if (info.ParentPid > 0 && _mainProcessId > 0 && info.ParentPid != _mainProcessId)
                        {
                            Debug.LogWarning($"[LocalMP] The hub at localhost:{_port} (pid {info.Pid}) was started by process {info.ParentPid}, not by the main editor ({_mainProcessId}): it may be left over from an earlier session.");
                        }
                        return FinishProbe();
                    case HubState.ProjectMismatch:
                        if (_mismatchSince < 0f) _mismatchSince = now;
                        WarnOnce(info.Message);
                        return now - _mismatchSince < MismatchGraceSeconds || FinishProbe();
                    case HubState.NoRelay:
                        if (_noRelaySince < 0f) _noRelaySince = now;
                        WarnOnce(info.Message);
                        return now - _noRelaySince < NoRelayGraceSeconds || FinishProbe();
                    default:
                        _mismatchSince = -1f;
                        _noRelaySince = -1f;
                        if (now - _start < UnreachableSeconds) return true;
                        Last.State = HubState.Unreachable;
                        Last.Message = $"nothing answered at localhost:{_port} for {UnreachableSeconds:0} s; is the main editor in Play?";
                        Debug.LogWarning("[LocalMP] " + Last.Message + " Loading the page anyway.");
                        return FinishProbe();
                }
            }

            bool FinishProbe()
            {
                StopProbe();
                _probing = false;
                _viewWaitSince = Time.realtimeSinceStartup;
                return WaitForView();
            }

            // LoadUrl before the view's window exists only defers (and warns); wait for it instead.
            bool WaitForView()
            {
                if (_view == null || _view.initialised) return false;
                if (Time.realtimeSinceStartup - _viewWaitSince < ViewInitialiseTimeoutSeconds) return true;
                Debug.LogWarning("[LocalMP] The space view still isn't initialised; loading the page anyway.");
                return false;
            }

            void WarnOnce(string message)
            {
                if (_warned) return;
                _warned = true;
                Debug.LogWarning("[LocalMP] " + message);
            }
        }
    }
}

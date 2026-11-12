using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

// The contracts every part of the in-editor local multiplayer host is written against. The host stands in
// for the Greenfield client (NetworkService and its bridges), the relay in the Ora Electron app for the
// PacketParty server. The wire protocol is C:\workspace\SideQuest.Ora\Protocol\RELAY.md.
namespace BS.LocalMultiplayer
{
    /// <summary>
    /// What a player may do in the room, from the shared settings (worldOwnerSlot, moderatorSlots). As in
    /// production, protected room-state writes need the world owner or a moderator, and a one-shot's
    /// fromAdmin is the world owner only.
    /// </summary>
    public enum PlayerRole { Member = 0, Moderator = 1, Owner = 2 }

    public enum SessionState { Idle, Connecting, Joining, Joined, Reconnecting, Failed }

    /// <summary>What the hub probe found at the page's web server port.</summary>
    public enum HubState { Unknown, Waiting, Ready, NoRelay, ProjectMismatch, Unreachable }

    public sealed class HubInfo
    {
        public HubState State = HubState.Unknown;
        public string Message = "";
        public string ProjectRoot = "";
        public int Pid;
        public int ParentPid;
        public bool PersistenceEnabled;
        public string PersistenceDir = "";
    }

    /// <summary>This editor player's identity: its Multiplayer Play Mode slot and the shared settings.</summary>
    public sealed class LocalIdentity
    {
        /// <summary>"Main Editor", or MPPM's "Player N" for a clone.</summary>
        public string Slot = "Main Editor";
        /// <summary>1 for the main editor, 2..4 for MPPM players, 0 when unknown.</summary>
        public int SlotIndex = 1;
        public bool IsClone;
        public string VpId = "";
        /// <summary>The main editor's process id (a clone's -mainProcessId).</summary>
        public int MainProcessId;
        /// <summary>The main project's root folder, also from inside a clone (Library/VP/...).</summary>
        public string MainProjectRoot = "";
        /// <summary>"local:" + Slot: the stable user id production gets from sign-in.</summary>
        public string ClientId = "";
        /// <summary>HashUid(ClientId): the page's user key (UserData.uid), as NetworkService.HashUid.</summary>
        public string Uid = "";
        public string DisplayName = "";
        public PlayerRole Role;
        /// <summary>Per-slot colour for the orb and the overlay only, never UserData.color.</summary>
        public Color Tint = Color.white;
        /// <summary>"local:" + settings.worldOwnerSlot; one-shots from it arrive with fromAdmin.</summary>
        public string WorldOwnerClientId = "";
    }

    /// <summary>The cross-player object keys a player has (RELAY.md 6.5), for Id-mismatch diagnostics.</summary>
    public sealed class ObjectManifest
    {
        public List<string> Ids = new List<string>();
        public List<string> RuntimeIds = new List<string>();
    }

    /// <summary>A remote player in the room.</summary>
    public sealed class LocalPeer
    {
        /// <summary>The room session id: UserData.id, and a one-shot's fromId.</summary>
        public string RoomSessionId = "";
        public string PeerId = "";
        /// <summary>The relay's userId: "local:" + slot.</summary>
        public string ClientId = "";
        public string Uid = "";
        public string DisplayName = "";
        public string Slot = "";
        public PlayerRole Role;
        public int EditorPid;
        public Color Tint = Color.white;
        public ObjectManifest Manifest = new ObjectManifest();
        // Set by presence when it builds the remote player (NetworkService.CreateRemotePlayer).
        public GameObject Root;
        public Transform Head;
        public UserData User;
    }

    /// <summary>
    /// The pose extension of tf.participant (RELAY.md 6.2), standing in for production's BonePoseCodec stream:
    /// the hip in world space and the head and hands relative to the hip.
    /// </summary>
    public struct PoseExt
    {
        public Vector3 HipPosition;
        public Quaternion HipRotation;
        public Vector3 HeadPosition;
        public Quaternion HeadRotation;
        public Vector3 LeftPosition;
        public Quaternion LeftRotation;
        public Vector3 RightPosition;
        public Quaternion RightRotation;
        /// <summary>The seated leg pose (production: TruePoseData.IsSeated from the seat's isSeat).</summary>
        public bool Seated;
    }

    /// <summary>tf.participant (RELAY.md 6.2): the player root, as production's participant transform "root".</summary>
    public struct ParticipantFrame
    {
        public uint Seq;
        /// <summary><see cref="MachineClock.NowMs"/> when sampled.</summary>
        public double T;
        public int ValidForMs;
        public bool Discontinuity;
        public Vector3 Position;
        public Quaternion Rotation;
        public bool HasVelocity;
        public Vector3 Velocity;
        public bool HasExt;
        public PoseExt Ext;
    }

    /// <summary>One frame of tf.objects (RELAY.md 6.2).</summary>
    public struct ObjectFrame
    {
        public string ObjectId;
        public uint Generation;
        public int Epoch;
        public ushort Seq;
        /// <summary><see cref="MachineClock.NowMs"/> when sampled.</summary>
        public double T;
        public int ValidForMs;
        public Vector3 Position;
        public bool HasRotation;
        public Quaternion Rotation;
        public bool HasVelocity;
        public Vector3 Velocity;
    }

    /// <summary>
    /// A millisecond clock that every process on the machine shares: Unix-epoch milliseconds with Stopwatch
    /// resolution. Every Multiplayer Play Mode player runs on the same machine, so frame timestamps compare across
    /// players without production's clock-offset estimation. The raw Stopwatch is not machine-wide: Unity's Mono
    /// counts its ticks from each process's first read (mono_100ns_ticks), so editors started at different times
    /// disagree by hours, and a remote stream's frames all look like they are in the future. Each process pairs the
    /// Stopwatch with the system clock once, right at a system-clock tick, which puts every player on one timeline
    /// to well under a millisecond; after that only the monotonic Stopwatch moves it.
    /// </summary>
    public static class MachineClock
    {
        const long UnixEpochTicks = 621355968000000000L; // new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks
        static readonly double s_msPerTick = 1000.0 / Stopwatch.Frequency;
        static readonly double s_offsetMs = AlignToSystemClock();

        public static double NowMs => Stopwatch.GetTimestamp() * s_msPerTick + s_offsetMs;

        // Pairs the clocks just after DateTime.UtcNow changes value: with a coarse (15.6 ms) system timer the pair is
        // then as close as two consecutive reads, not up to one timer period apart. Spins at most 50 ms, once.
        static double AlignToSystemClock()
        {
            long first = DateTime.UtcNow.Ticks;
            long utcTicks = first;
            long stopwatch = Stopwatch.GetTimestamp();
            long deadline = stopwatch + Stopwatch.Frequency / 20;
            while (utcTicks == first && stopwatch < deadline)
            {
                utcTicks = DateTime.UtcNow.Ticks;
                stopwatch = Stopwatch.GetTimestamp();
            }
            double utcMs = (utcTicks - UnixEpochTicks) / (double)TimeSpan.TicksPerMillisecond;
            return utcMs - stopwatch * s_msPerTick;
        }
    }

    public enum DiagnosticLevel { Info, Warning, Error }

    public sealed class DiagnosticEntry
    {
        public string Key;
        public DiagnosticLevel Level;
        public string Message;
        /// <summary>Time.realtimeSinceStartupAsDouble when set.</summary>
        public double Time;
    }

    /// <summary>What the overlay and the window list: one entry per key, Set replaces. Main thread only.</summary>
    public interface ILocalDiagnostics
    {
        void Set(string key, DiagnosticLevel level, string message);
        void Clear(string key);
        void ClearPrefix(string prefix);
        IReadOnlyList<DiagnosticEntry> Entries { get; }
        event Action Changed;
    }

    /// <summary>A relay request failed. <see cref="Code"/> is the relay or server error code, or "not_connected",
    /// "timeout", "closed".</summary>
    public sealed class LocalRelayException : Exception
    {
        public readonly string Code;

        public LocalRelayException(string code, string message = null) : base(message ?? code)
        {
            Code = code;
        }
    }

    /// <summary>
    /// The relay connection and the room session: the PacketPartyClient stand-in. Every event and handler runs
    /// on the main thread, in the order the messages arrived.
    /// </summary>
    public interface ILocalSession
    {
        SessionState State { get; }
        event Action<SessionState> StateChanged;
        string RoomId { get; }
        /// <summary>Null until the first welcome.</summary>
        string OwnRoomSessionId { get; }
        string OwnPeerId { get; }
        string RelayUri { get; }
        double RttMs { get; }
        string LastError { get; }
        HubInfo Hub { get; }

        /// <summary>After room.ready, with the snapshots applied (PacketPartyClient.OnRoomJoined).</summary>
        event Action RoomJoined;
        /// <summary>The session ended: the socket closed or the player left. Remote peers are gone.</summary>
        event Action RoomLeft;

        IReadOnlyCollection<LocalPeer> Peers { get; }
        bool TryGetPeer(string roomSessionId, out LocalPeer peer);
        event Action<LocalPeer> PeerJoined;
        event Action<LocalPeer> PeerLeft;
        event Action<LocalPeer> PeerManifestChanged;

        /// <summary>
        /// A reliable request. Resolves with the whole reply message (matched by requestId). Throws
        /// <see cref="LocalRelayException"/>: "not_connected" unless joined, "timeout" after 10 s, "closed" when the
        /// socket drops first.
        /// </summary>
        Task<JObject> RequestAsync(string type, JObject body, CancellationToken cancellationToken = default);
        /// <summary>A reliable fire-and-forget message; false (dropped) unless joined.</summary>
        bool Send(string type, JObject body);
        /// <summary>Incoming messages of a type, in arrival order.</summary>
        void On(string type, Action<JObject> handler);
        void Off(string type, Action<JObject> handler);

        /// <summary>tf.participant: the latest frame wins and goes out with the next flush.</summary>
        void SendParticipantFrame(in ParticipantFrame frame);
        /// <summary>(fromRoomSessionId, frame).</summary>
        event Action<string, ParticipantFrame> ParticipantFrameReceived;
        /// <summary>tf.objects: queued per object (the latest wins), flushed once per frame.</summary>
        void QueueObjectFrame(in ObjectFrame frame);
        /// <summary>(fromRoomSessionId, frame).</summary>
        event Action<string, ObjectFrame> ObjectFrameReceived;

        /// <summary>
        /// EditableOwnUserState: this player's engine root keys (attachment_&lt;Id&gt;, pilot), diff-synced every
        /// 50 ms and pushed again on every join. "" is a value (cleared), not a delete.
        /// </summary>
        void SetEngineUserState(string key, string value);
        /// <summary>Engine (non-"props.") user-state paths of every player, own echo included:
        /// (roomSessionId, path, value as a string, deleted).</summary>
        event Action<string, string, string, bool> EngineUserStateChanged;
        /// <summary>On join: roomSessionId → (path → value) for the engine paths.</summary>
        event Action<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> EngineUserStateSnapshot;
        /// <summary>The player left (app.userState.removed): drop their engine state.</summary>
        event Action<string> EngineUserStateRemoved;

        /// <summary>Sends this player's object manifest (RELAY.md 6.5) when it changed.</summary>
        void UpdateManifest(ObjectManifest manifest);

        /// <summary>Close the socket and stay out of the room.</summary>
        void Leave();
        /// <summary>Join the current room again after <see cref="Leave"/>.</summary>
        void Join();
        /// <summary>Reload the page, so this player goes through OnLoad and OnSceneReady like a late joiner.</summary>
        void Rejoin();
        /// <summary>admin.clearRoomState (the world owner only).</summary>
        Task<bool> ClearRoomStateAsync();
    }

    /// <summary>
    /// One feature of the host. Every non-abstract MonoBehaviour in this assembly that implements it and carries
    /// <see cref="LocalModuleAttribute"/> is added to the host GameObject and installed in ascending order, before
    /// BSNetworkHost.Active is set, and uninstalled in reverse order. Install must attach every SDK listener and
    /// delegate it needs right away: scene objects have not started yet.
    /// </summary>
    public interface ILocalModule
    {
        void Install(ILocalHost host);
        void Uninstall();
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class LocalModuleAttribute : Attribute
    {
        public int Order { get; }

        public LocalModuleAttribute(int order)
        {
            Order = order;
        }
    }

    /// <summary>Install order of the modules.</summary>
    public static class ModuleOrder
    {
        /// <summary>Join/leave lifecycle (NetworkService), publishing gate.</summary>
        public const int Lifecycle = 100;
        /// <summary>Remote players and their UserData.</summary>
        public const int Presence = 150;
        public const int Clock = 180;
        /// <summary>Provides ILocalRig, IRemoteAvatarFactory and IRemoteBoneSource.</summary>
        public const int Avatars = 200;
        public const int OneShot = 300;
        public const int SpaceState = 310;
        public const int UserState = 320;
        public const int SyncedObjects = 400;
        public const int ObjectIdDiagnostics = 450;
        public const int Attachments = 500;
        public const int Overlay = 900;
    }

    /// <summary>The host, as the modules see it.</summary>
    public interface ILocalHost
    {
        /// <summary>The live scene.</summary>
        BSScene Scene { get; }
        ILocalSession Session { get; }
        LocalIdentity Identity { get; }
        LocalMultiplayerSettings Settings { get; }
        ILocalDiagnostics Diagnostics { get; }
        /// <summary>The DontDestroyOnLoad "[LocalMultiplayer]" GameObject the modules live on.</summary>
        GameObject HostObject { get; }
        /// <summary>Parent of the remote players' roots.</summary>
        Transform RemotesRoot { get; }
        /// <summary>The local player's pose is being published (production: StartPublishing one frame after the cage
        /// opens). Synced objects publish whenever they are owned and bound, as in production.</summary>
        bool PublishingLive { get; }
        event Action<bool> PublishingChanged;
        /// <summary>Changes with every relay session; stamp posted work with it and drop stale work.</summary>
        int SessionEpoch { get; }
        void Provide<T>(T service) where T : class;
        /// <summary>A service another module provided, or null.</summary>
        T Get<T>() where T : class;
    }

    /// <summary>The installed host, for the window and the overlay. Null when local multiplayer is not running.</summary>
    public static class LocalMultiplayerRuntime
    {
        public static ILocalHost Current { get; private set; }
        public static event Action<ILocalHost> Installed;
        public static event Action Uninstalled;

        internal static void SetCurrent(ILocalHost host)
        {
            if (Current == host)
            {
                return;
            }
            Current = host;
            if (host != null)
            {
                Installed?.Invoke(host);
            }
            else
            {
                Uninstalled?.Invoke();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            Current = null;
        }
    }

    /// <summary>Builds a remote player's avatar (provided by the avatar module, used by presence).</summary>
    public interface IRemoteAvatarFactory
    {
        /// <summary>Builds the avatar under <paramref name="root"/>, which presence owns along with its UserData.
        /// Returns the head transform (UserData.Head).</summary>
        Transform BuildAvatar(LocalPeer peer, Transform root);
    }

    /// <summary>Remote avatar bones (production: RemoteAvatarService).</summary>
    public interface IRemoteBoneSource
    {
        /// <summary>The remote player's bone on the orb rig, named as HumanBodyBones.</summary>
        bool TryGetRemoteBone(string roomSessionId, HumanBodyBones bone, out Transform result);
        /// <summary>Where a remote player holds an object: "left", "right" or "head" (RemoteAvatarService.cs:115-126).</summary>
        Transform ResolveHeldAnchor(string roomSessionId, string anchorKey);
        /// <summary>Glues the remote hip to a seat (RemoteAvatarService.SetPilotSeat); a null seat clears it. False
        /// while that player's avatar doesn't exist yet, so the caller retries.</summary>
        bool SetPilotSeat(string roomSessionId, Transform seat, Vector3 hipLocalPosition, Quaternion hipLocalRotation);
    }

    /// <summary>The local desktop player's body parts, standing in for FlexaBody's physics hands, torso ball and camera.</summary>
    public interface ILocalRig
    {
        Transform Head { get; }
        Transform Torso { get; }
        Transform LeftHand { get; }
        Transform RightHand { get; }
        Rigidbody TorsoBody { get; }
        Rigidbody LeftHandBody { get; }
        Rigidbody RightHandBody { get; }
        IReadOnlyList<Collider> LocalPlayerColliders { get; }
        Vector3 HipPosition { get; }
        Quaternion HipRotation { get; }
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace BS.VideoStream
{
    /// <summary>
    /// Once a frame, after every Update: CPU-path frames go into their textures, then one render event draws every
    /// GPU-path decoder's due frame into its texture, before the cameras render (on Vulkan, after them). It keeps issuing the event while the
    /// native library still has decoders, destroyed ones included, because their graphics resources are released there.
    /// </summary>
    [AddComponentMenu("")]
    internal sealed class VideoFrameDriver : MonoBehaviour
    {
        static VideoFrameDriver _instance;
        static readonly List<VideoPlayback> Players = new List<VideoPlayback>();
        static readonly List<VideoPlayback> Snapshot = new List<VideoPlayback>();

        internal static void Register(VideoPlayback player)
        {
            if (!Players.Contains(player))
            {
                Players.Add(player);
            }
            Ensure();
        }

        internal static void Unregister(VideoPlayback player)
        {
            Players.Remove(player);
        }

        static void Ensure()
        {
            if (_instance != null || !Application.isPlaying)
            {
                return;
            }
            var host = new GameObject("BS Video Frames") { hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave };
            DontDestroyOnLoad(host);
            _instance = host.AddComponent<VideoFrameDriver>();
        }

        // On Vulkan the event goes at the end of the frame, after every camera, through a command buffer, as Unity's WebRTC
        // plugin does: issued from LateUpdate it ran inside an open render pass on Quest (builds 9157, 9158), where its
        // copy can't be recorded, though its id was reserved and configured to run outside one. The picture then shows a
        // frame later. Batch mode never reaches the end of a frame, so it keeps LateUpdate (tests run on D3D11 anyway).
        static bool AtEndOfFrame => SystemInfo.graphicsDeviceType == GraphicsDeviceType.Vulkan && !Application.isBatchMode;
        static CommandBuffer _command;
        readonly WaitForEndOfFrame _endOfFrame = new WaitForEndOfFrame();

        void Start()
        {
            if (AtEndOfFrame)
            {
                StartCoroutine(PresentAtEndOfFrame());
            }
        }

        IEnumerator PresentAtEndOfFrame()
        {
            while (true)
            {
                yield return _endOfFrame;
                Present();
            }
        }

        void LateUpdate()
        {
            Snapshot.Clear();
            Snapshot.AddRange(Players);
            foreach (var player in Snapshot)
            {
                player.BeforePresent();
            }
            if (!AtEndOfFrame)
            {
                Present();
            }
            foreach (var player in Snapshot)
            {
                player.AfterPresent();
            }
        }

        static void Present()
        {
            if (!VideoDecoderNative.Available || VideoDecoderNative.LiveCount <= 0)
            {
                return;
            }
            if (!AtEndOfFrame)
            {
                GL.IssuePluginEvent(VideoDecoderNative.RenderEvent, VideoDecoderNative.RenderEventId);
                return;
            }
            if (_command == null)
            {
                _command = new CommandBuffer { name = "BSVideoPlayer present" };
            }
            _command.IssuePluginEvent(VideoDecoderNative.RenderEvent, VideoDecoderNative.RenderEventId);
            Graphics.ExecuteCommandBuffer(_command);
            _command.Clear();
        }

        void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        void OnApplicationQuit()
        {
            if (VideoDecoderNative.Loaded)
            {
                VideoDecoderNative.DestroyAll();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Players.Clear();
            _instance = null;
        }

#if UNITY_EDITOR
        [InitializeOnLoadMethod]
        static void HookEditor()
        {
            // Native decoders outlive Play mode and domain reloads (the library stays loaded): stop them with the scripts.
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingPlayMode)
                {
                    StopAll();
                }
            };
            AssemblyReloadEvents.beforeAssemblyReload += StopAll;
        }

        static void StopAll()
        {
            // Only once a video has loaded the library: asking would load it into the editor for good.
            if (!VideoDecoderNative.Loaded)
            {
                return;
            }
            VideoDecoderNative.DestroyAll();
            // One more event lets the render thread release what it can now; the rest goes at the next video's events.
            Present();
        }
#endif
    }
}

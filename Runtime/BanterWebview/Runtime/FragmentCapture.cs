#define DEBUG
#undef DEBUG

#define TEST
//#undef TEST

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Banter.SDK;
using Banter.Utilities.Async;
using UnityEngine.Experimental.Rendering;

namespace TLab.WebView
{
	public abstract class FragmentCapture : MonoBehaviour, IOffscreen
	{
		public enum State
		{
			None,
			Initialising,
			Initialized,
			Destroyed,
		}

		[Header("Capture Settings")]
		[SerializeField] protected RawImage m_rawImage;
		[SerializeField] protected UnityEvent<Texture2D> m_onCapture = new UnityEvent<Texture2D>();
		[SerializeField] protected UnityEvent m_onLoading = new UnityEvent();
		[SerializeField] protected UnityEvent m_onNativeReady = new UnityEvent();
		[SerializeField] protected string m_preloadScript;
		[SerializeField] protected Vector2Int m_viewSize = new Vector2Int(1024, 1024);
		[SerializeField] protected Vector2Int m_texSize = new Vector2Int(512, 512);
		[SerializeField, Min(1)] protected int m_fps = 30;
		[SerializeField] protected CaptureMode m_captureMode = CaptureMode.HardwareBuffer;

		protected bool m_isVulkan;

		private string THIS_NAME => "[" + this.GetType() + "] ";

		public Vector2Int viewSize { get => m_viewSize; set => m_viewSize = value; }

		public Vector2Int texSize { get => m_texSize; set => m_texSize = value; }

		public RawImage rawImage { get => m_rawImage; set => m_rawImage = value; }

		public UnityEvent<Texture2D> onCapture { get => m_onCapture; set => m_onCapture = value; }

		public UnityEvent onLoading { get => m_onLoading; set => m_onLoading = value; }

		public UnityEvent onNativeReady { get => m_onNativeReady; set => m_onNativeReady = value; }

		public string preloadScript { get => m_preloadScript; set => m_preloadScript = value; }



		public int fps { get => m_fps; set => m_fps = value; }

		public int winId = 0;

		public CaptureMode captureMode { get => m_captureMode; set => m_captureMode = value; }

		protected State m_state = State.None;

		public State state => m_state;

#if UNITY_ANDROID && !UNITY_EDITOR || DEBUG
		protected AndroidJavaObject m_NativePlugin;

		private class NativePluginRef
		{
			public AndroidJavaObject plugin;

			public NativePluginRef(AndroidJavaObject plugin)
			{
				this.plugin = plugin;
			}
		}

		private static List<NativePluginRef> m_garbageCollectTargets = new List<NativePluginRef>();
#else
		MemoryMappedFile mmf;
		MemoryMappedViewAccessor accessor;
		IntPtr ptr;
#endif

		public static void GarbageCollect()
		{
#if UNITY_ANDROID && !UNITY_EDITOR || DEBUG
			var queue = new Queue<NativePluginRef>();

			foreach (var disposable in m_garbageCollectTargets)
			{
				if (disposable.plugin != null)
				{
					// Wait until Dispose() is completed on the plug-in side.
					// Then remove it from Unity management.
					if (NativePlugin.GetIsFragmentDisposed((int)disposable.plugin.GetRawObject()))
					{
						disposable.plugin.Dispose();
						disposable.plugin = null;
						queue.Enqueue(disposable);
					}
				}
			}

			foreach (var disposed in queue)
				m_garbageCollectTargets.Remove(disposed);
#endif
		}
		protected delegate void UpdateFrameFunc();

		protected UpdateFrameFunc m_updateFrameFunc;

		protected Texture2D m_loadingView;
		protected Texture2D m_contentView;

		protected static Vector2Int m_screenFullRes;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
		protected static void PreInit()
		{
			// https://github.com/TLabAltoh/TLabWebView/issues/6
			m_screenFullRes = new Vector2Int(Screen.width, Screen.height);
		}

		/// <summary>
		/// Launch initialize task if Fragment is not initialized yet.
		/// </summary>
		public virtual void Init()
		{
			if (m_state == State.None)
				StartCoroutine(InitTask());
		}

		/// <summary>
		/// Set resolution for both View and Texture (called on initialization).
		/// </summary>
		/// <param name="viewSize">View Size</param>
		/// <param name="texSize">Tex Size</param>
		public virtual void InitResolution(Vector2Int viewSize, Vector2Int texSize)
		{
			m_viewSize = viewSize;
			m_texSize = texSize;
		}

		public virtual void Init(Vector2Int viewSize, Vector2Int texSize)
		{
			InitResolution(viewSize, texSize);

			Init();
		}
		public virtual string package => "";

		public virtual IEnumerator InitTask()
		{
			// var browserManager = BrowserManager.Instance;
			m_state = State.Initialising;
#if UNITY_ANDROID && !UNITY_EDITOR || DEBUG
			// I cannot find the way to preload (load on startup)
			// jni shared library. so call library function and
			// load dinamically here. (call unity plugin on load)
			switch (SystemInfo.renderingThreadingMode)
			{
				case UnityEngine.Rendering.RenderingThreadingMode.MultiThreaded:
					GL.IssuePluginEvent(NativePlugin.DummyRenderEventFunc(), 0);
					break;
				default:
					NativePlugin.DummyRenderEvent(0);
					break;
			}
#else

			Action InitFunc = () =>
			{
				m_state = State.Initialized;
				// m_onLoading?.Invoke();
				try
				{
					BanterScene.Instance().link.Send($"{APICommands.CREATE_WINDOW}{MessageDelimiters.PRIMARY}{m_texSize.x}{MessageDelimiters.PRIMARY}{m_texSize.y}{MessageDelimiters.PRIMARY}about:blank{MessageDelimiters.PRIMARY}{m_preloadScript}", msg => {
							
						var parts = msg.Split(MessageDelimiters.PRIMARY, 5);
						if (parts.Length < 4)
						{
							Debug.LogError("Failed to create window: " + msg);
							return;
						}
						winId = int.Parse(parts[1]);
						
						m_texSize.x = int.Parse(parts[2]);
						m_texSize.y = int.Parse(parts[3]);
						Debug.Log("[Banter] Created window with ID: " + winId + $" | Size: {m_texSize.x}x{m_texSize.y}");
						mmf = MemoryMappedFile.OpenExisting("BanterPixelBuffer" + parts[1], MemoryMappedFileRights.Read);
						accessor = mmf.CreateViewAccessor();
						ptr = accessor.SafeMemoryMappedViewHandle.DangerousGetHandle();
						UnityMainThreadTaskScheduler.Default.Enqueue(() =>
						{
							if (m_contentView == null)
							{
								TextureCreationFlags flags = TextureCreationFlags.None;
								m_contentView = new Texture2D(m_texSize.x, m_texSize.y, GraphicsFormat.B8G8R8A8_SRGB, flags);
							}

							if (m_rawImage != null)
							{
								m_rawImage.texture = m_contentView;
							}
							m_onCapture?.Invoke(m_contentView);
						});
					});

				}
				catch (Exception ex)
				{
					Debug.LogError($"Failed to open shared memory: {ex}");
					return;
				}
			};
			if (BanterScene.Instance().link.pipe.GetIsConnected())
			{
				InitFunc?.Invoke();
			}
			else
			{
				EventHandler _Connected = null;
				_Connected = (arg0, arg1) =>
				{
					InitFunc?.Invoke();
					BanterScene.Instance().link.Connected -= _Connected;
				};
				BanterScene.Instance().link.Connected += _Connected;
			}
#endif


			yield return new WaitForEndOfFrame();

#if UNITY_ANDROID && !UNITY_EDITOR || DEBUG

			if ((m_captureMode != CaptureMode.Surface))
			{
				m_loadingView = Texture2D.linearGrayTexture;
				m_contentView = null;
				m_onLoading?.Invoke();
				if(m_rawImage != null)
					m_rawImage.texture = m_loadingView;
			}

			m_isVulkan = (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Vulkan);

			m_NativePlugin = new AndroidJavaObject(package);

			switch (m_captureMode)
			{
				case CaptureMode.HardwareBuffer:
					m_updateFrameFunc = m_isVulkan ? UpdateVulkanFrame : UpdateGLESFrame;
					break;
				case CaptureMode.ByteBuffer:
					m_updateFrameFunc = UpdateFrameWithByteBuffer;
					break;
				case CaptureMode.Surface:
					m_updateFrameFunc = UpdateFrameDummy;
					break;
			}

			if (m_NativePlugin != null) {		
				InitNativePlugin();
				m_onNativeReady?.Invoke();
			}

			while (!IsInitialized())
				yield return new WaitForEndOfFrame();

			m_state = State.Initialized;
#endif
		}

		protected virtual void InitNativePlugin() { }

		/// <summary>
		/// If it returns true, this Fragment is already initialized.
		/// </summary>
		/// <returns>Whether or not this Fragment is initialized</returns>
		public bool IsInitialized()
		{
#if UNITY_ANDROID && !UNITY_EDITOR || DEBUG
			if (m_NativePlugin != null)
			{
				var instance_ptr = m_NativePlugin.GetRawObject();
				return NativePlugin.GetIsFragmentInitialized((int)instance_ptr);
			}
			return false;
#else
			return false;
#endif
		}

		/// <summary>
		/// Return the texture pointer of the View frame (NOTE: In Vulkan, the VkImage pointer returned by this function could not be used for UpdateExternalTexture. This issue has not been fixed).
		/// </summary>
		/// <returns>texture pointer of the view frame (Vulkan: VkImage, OpenGLES: TexID)</returns>
		public IntPtr GetPlatformTextureID()
		{
			if (m_state != State.Initialized)
				return IntPtr.Zero;

#if UNITY_ANDROID && !UNITY_EDITOR || DEBUG
			return NativePlugin.GetPlatformTextureID((int)m_NativePlugin.GetRawObject());
#else
			return IntPtr.Zero;
#endif
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="fps">Fps fo rendering</param>
		public void SetFps(int fps)
		{
			m_fps = fps;
#if UNITY_ANDROID && !UNITY_EDITOR || DEBUG
			m_NativePlugin.Call(nameof(SetFps), fps);
#endif
		}

		/// <summary>
		/// Update Texture resolution
		/// </summary>
		/// <param name="texSize">Tex Size</param>
		public void ResizeTex(Vector2Int texSize)
		{
			if (m_state != State.Initialized)
				return;

			if (m_rawImage != null)
				m_rawImage.texture = m_loadingView;
			m_onLoading?.Invoke();

			m_texSize = texSize;

#if UNITY_ANDROID && !UNITY_EDITOR || DEBUG
			m_NativePlugin.Call(nameof(ResizeTex), texSize.x, texSize.y);
#endif
		}

		/// <summary>
		/// Update View resolution
		/// </summary>
		/// <param name="viewSize"></param>
		public void ResizeView(Vector2Int viewSize)
		{
			if (m_state != State.Initialized)
				return;

			if (m_rawImage != null)
				m_rawImage.texture = m_loadingView;
			m_onLoading?.Invoke();

			m_viewSize = viewSize;

#if UNITY_ANDROID && !UNITY_EDITOR || DEBUG
			m_NativePlugin.Call(nameof(ResizeView), viewSize.x, viewSize.y);
#endif
		}

		/// <summary>
		/// Update resolution for both View and Texture
		/// </summary>
		/// <param name="texSize">Tex Size</param>
		/// <param name="viewSize">Web Size</param>
		public void Resize(Vector2Int texSize, Vector2Int viewSize)
		{
			if (m_state != State.Initialized)
				return;

			if (m_rawImage != null)
				m_rawImage.texture = m_loadingView;
			m_onLoading?.Invoke();

			m_texSize = texSize;
			m_viewSize = viewSize;

#if UNITY_ANDROID && !UNITY_EDITOR || DEBUG
			m_NativePlugin.Call(nameof(Resize), texSize.x, texSize.y, viewSize.x, viewSize.y);
#else
			Destroy(m_contentView);
			m_contentView = null;
			BanterScene.Instance().link.Send($"{APICommands.RESIZE_WINDOW}{MessageDelimiters.PRIMARY}{m_texSize.x}{MessageDelimiters.PRIMARY}{m_texSize.y}", msg => {
				var parts = msg.Split(MessageDelimiters.PRIMARY, 4);
				if (parts.Length < 3)
				{
					Debug.LogError("Failed to resize window: " + msg);
					return;
				}
				m_texSize.x = int.Parse(parts[1]);
				m_texSize.y = int.Parse(parts[2]);
				UnityMainThreadTaskScheduler.Default.Enqueue(() =>
				{
					if (m_contentView == null)
						m_contentView = new Texture2D(m_texSize.x, m_texSize.y, TextureFormat.RGBA32, false, true);
					
					if (m_rawImage != null)
						m_rawImage.texture = m_contentView;
					onCapture?.Invoke(m_contentView);
				});
			});
#endif
		}

		public void SetSurface(IntPtr surfce, int width, int height)
		{
			if (m_state != State.Initialized)
				return;

#if UNITY_ANDROID && !UNITY_EDITOR || DEBUG
			var instance = m_NativePlugin.GetRawObject();
			NativePlugin.SetSurface((int)instance, (int)surfce, width, height);
#endif
		}

		public void RemoveSurface()
		{
			if (m_state != State.Initialized)
				return;

#if UNITY_ANDROID && !UNITY_EDITOR || DEBUG
			var instance = m_NativePlugin.GetRawObject();
			NativePlugin.RemoveSurface((int)instance);
#endif
		}

		private void UpdateSurface()
		{
			// External texture update behaviour
			// OpenGLES: Use the same texture
			// Vulkan: Create new VkImage and copy buffer to new one,
			// Texture Buffer is not shared so in order to update
			// frame, need to call update frame every frame. (Maybe
			// this processing is too heavy)

#if UNITY_ANDROID && !UNITY_EDITOR || DEBUG
			if (SystemInfo.renderingThreadingMode == UnityEngine.Rendering.RenderingThreadingMode.MultiThreaded){
				GL.IssuePluginEvent(NativePlugin.UpdateSharedTextureFunc(), (int)m_NativePlugin.GetRawObject());
			}else{
				NativePlugin.UpdateSharedTexture((int)m_NativePlugin.GetRawObject());
			}
#endif
		}

		protected void UpdateGLESFrame()
		{
#if UNITY_ANDROID && !UNITY_EDITOR || DEBUG
			UpdateSurface();

			int instance = (int)m_NativePlugin.GetRawObject();
			var flag = NativePlugin.ContentExists(instance);
			if (!flag)
				return;

			flag = NativePlugin.GetSharedBufferUpdateFlag(instance);
			if (!flag)
			{
				var texID = GetPlatformTextureID();

				// In OpenGLES API, the texture created by
				// CreateExternalTexture has same texture pointer
				// as function's arguments. And texture size is
				// not same with passed as arguments to display
				// (it seems same to native texture size). In
				// OpenGLES API, is buffer allocated for texture
				// size passed as arguments? If so, do I need to
				// pass zero (or one) for argments of texture size
				// to reduce overhead of memory allocation?

				var tmp = Texture2D.CreateExternalTexture(1, 1, TextureFormat.ARGB32, false, false, texID);

				//Debug.Log(THIS_NAME + $"[CreateExternalTexture] size: {tmp.width}, {tmp.height}, id: {texID}, {tmp.GetNativeTexturePtr()}");

				NativePlugin.SetSharedBufferUpdateFlag(instance, true);
				if(m_rawImage != null)
					m_rawImage.texture = tmp;
				m_onCapture?.Invoke(tmp);
				var release = m_contentView;
				if (release != null)
					Destroy(release);
				m_contentView = tmp;
			}
#endif
		}

		protected void UpdateVulkanFrame()
		{
#if UNITY_ANDROID && !UNITY_EDITOR || DEBUG
			UpdateSurface();

			int instance = (int)m_NativePlugin.GetRawObject();
			
			var flag = NativePlugin.ContentExists(instance);
			if (!flag)
				return;

			flag = NativePlugin.GetSharedBufferUpdateFlag(instance);
			if (!flag)
			{
				// Destroy the shared texture and verify that
				// the native plugin no longer references the
				// Unity texture. Because in Vulkan API, native
				// plugin directly copied buffer to Unity texture.

				var tmp = new Texture2D(m_texSize.x, m_texSize.y, TextureFormat.RGBA32, false, true);

				NativePlugin.SetUnityTextureID(instance, (long)tmp.GetNativeTexturePtr());
				NativePlugin.SetSharedBufferUpdateFlag(instance, true);

				if(m_rawImage != null)
					m_rawImage.texture = tmp;
				m_onCapture?.Invoke(tmp);
				var release = m_contentView;
				if (release != null)
					Destroy(release);
				m_contentView = tmp;
			}
#endif
		}

		/// <summary>
		/// Update frame from CPU side. This function is for non-hardware buffer use case.
		/// </summary>
		protected void UpdateFrameWithByteBuffer()
		{
#if UNITY_ANDROID && !UNITY_EDITOR || DEBUG
			var flag = NativePlugin.ContentExists((int)m_NativePlugin.GetRawObject());
			if (!flag)
				return;

			var buf = (byte[])(Array)m_NativePlugin.Call<sbyte[]>("GetFrameBuffer");
			// Because the content is already validated, there is
			// no need to buffer's null validation here.

			if (m_contentView == null)
				m_contentView = new Texture2D(m_texSize.x, m_texSize.y, TextureFormat.RGBA32, false, true);
			else
			{
				if (m_contentView.width * m_contentView.height * 4 != buf.Length)
				{
					Destroy(m_contentView);

					m_contentView = new Texture2D(m_texSize.x, m_texSize.y, TextureFormat.RGBA32, false, true);
				}
			}

			m_contentView.LoadRawTextureData(buf);
			m_contentView.Apply();

			if(m_rawImage != null)
				m_rawImage.texture = m_contentView;
			m_onCapture?.Invoke(m_contentView);
#endif
		}

		protected void UpdateFrameDummy() { }

		/// <summary>
		/// Request Webview to update frame.
		/// </summary>
		public void UpdateFrame()
		{
			// Debug.Log(gameObject.name + "UpdateFrame called. " + winId + ", " + m_state);
			if (m_state != State.Initialized)
				return;

			// Debug.Log(gameObject.name + "UpdateFrame called2. " + winId + ", " + m_state);
#if UNITY_ANDROID && !UNITY_EDITOR || DEBUG
			m_updateFrameFunc.Invoke();
#else
			if (m_contentView != null && ptr != IntPtr.Zero)
			{
				m_contentView.LoadRawTextureData(ptr, m_texSize.x * m_texSize.y * 4);
				m_contentView.Apply();
				// Debug.Log(THIS_NAME + "update frame. " + winId);
			}
			else
			{
				// Debug.Log(THIS_NAME + "ContentView is null or ptr is zero, cannot update frame. " + winId);
			}
#endif
		}

		protected virtual void Destroy()
		{
			if (m_state == State.Destroyed || m_state == State.None)
				return;

#if UNITY_ANDROID && !UNITY_EDITOR || DEBUG

			m_garbageCollectTargets.Add(new NativePluginRef(m_NativePlugin));
#endif

			// I need to call destroy in main thread
			// This may delete the external texture sooner than
			// the native plugin's destroy process, but this is
			// currently not a problem.
			Destroy(m_contentView);
			m_contentView = null;

			// I need to call this function on unity's render thread
			// because ReleaseSharedTexture() call GLES or Vulkan
			// function and it needs to be called on render thread.
#if UNITY_ANDROID && !UNITY_EDITOR || DEBUG
			if (SystemInfo.renderingThreadingMode == UnityEngine.Rendering.RenderingThreadingMode.MultiThreaded)
				GL.IssuePluginEvent(NativePlugin.DisposeFunc(), (int)m_NativePlugin.GetRawObject());
			else
				NativePlugin.Dispose((int)m_NativePlugin.GetRawObject());

			m_state = State.Destroyed;
#else
 		accessor?.Dispose();
        mmf?.Dispose();
#endif
			// BrowserManager.Instance.UnregisterBrowser(GetInstanceId());
		}
		/// <summary>
		/// Test function of <see cref="SendMsg">SetSurface</see>
		/// </summary>
		public void SendMsg(string msg)
		{
			if (string.IsNullOrEmpty(m_preloadScript))
			{
				return;
			}

#if UNITY_ANDROID && !UNITY_EDITOR || DEBUG
			m_NativePlugin.Call(nameof(SendMsg), msg);
#endif
		}
		/// <summary>
		/// Test function of <see cref="StartSocketServer">SetSurface</see>
		/// </summary>
		public int StartSocketServer()
		{
			if (string.IsNullOrEmpty(m_preloadScript))
			{
				return -2;
			}

#if UNITY_ANDROID && !UNITY_EDITOR || DEBUG
			return m_NativePlugin.Call<int>(nameof(StartSocketServer));
#else
			return -2;
#endif
		}
#if TEST
		/// <summary>
		/// Test function of <see cref="SetSurface">SetSurface</see>
		/// </summary>
		public void RenderContent2TmpSurface()
		{
			if (m_state != State.Initialized)
				return;

#if UNITY_ANDROID && !UNITY_EDITOR || DEBUG
			m_NativePlugin.Call(nameof(RenderContent2TmpSurface));
#endif
		}
#endif

		protected void OnDestroy() => Destroy();

		protected void OnApplicationQuit() => Destroy();
	}
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;

namespace BS
{
    public partial class BSTeleporter
    {
        static readonly int PortalImage = Shader.PropertyToID("_MainTex");
        static readonly int HasPortalImage = Shader.PropertyToID("_HasImage");
        static readonly int StereoPortalImage = Shader.PropertyToID("_StereoImage");
        static readonly int PerspectivePortalImage = Shader.PropertyToID("_PerspectiveImage");
        static readonly int PortalViewLeft = Shader.PropertyToID("_PortalViewProjectionL");
        static readonly int PortalViewRight = Shader.PropertyToID("_PortalViewProjectionR");
        static bool capturingPortal;
        static readonly HashSet<BSTeleporter> enabledPortals = new HashSet<BSTeleporter>();
        RenderTexture portalImage;
        MeshRenderer portalRenderer;
        MaterialPropertyBlock originalPortalProperties;
        Pose capturedDestination;
        float capturedEyeSeparation;
        float capturedConvergence;
        const float PortalFieldOfView = 60f;
        bool portalImageDirty = true;
        double nextPortalCapture;
        int earliestPortalFrame;
        bool capturedPerspective, wasLiveActive;
        int capturedResolution;
        MeshRenderer hiddenRuntimeRenderer;
        bool originalRuntimeForceOff;
        readonly UnityEngine.Plane[] portalFrustum = new UnityEngine.Plane[6];
        Camera liveCaptureCamera;
        readonly List<(MeshRenderer renderer, bool hidden)> hiddenPortals = new List<(MeshRenderer renderer, bool hidden)>();
        MaterialPropertyBlock portalProperties;
        int liveCaptureFrame = -1, liveCaptureEyes;

        // Inspector callbacks only invalidate; captures run later or before the live viewer renders.
        public void RefreshPortalPreview() => portalImageDirty = true;

        void LateUpdate() => UpdatePortalPreview();
        void OnEnable()
        {
            enabledPortals.Add(this);
            RenderPipelineManager.beginCameraRendering += BeforePortalViewer;
            earliestPortalFrame = Time.frameCount + 2;
            RefreshPortalPreview();
        }
        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeforePortalViewer;
            enabledPortals.Remove(this);
            ReleasePortalPreview();
            RestoreRuntimePortalVisibility();
        }
        void OnValidate() => RefreshPortalPreview();

#if UNITY_EDITOR
        public void UpdateEditorPortalPreview() => UpdatePortalPreview();
        public void ReleaseEditorPortalPreview() => ReleasePortalPreview();
#endif

        void BeforePortalViewer(ScriptableRenderContext context, Camera camera)
        {
            if (!Application.isPlaying || !realtimeWhenNearby || capturingPortal || camera != Camera.main) return;
            // URP refreshes the current XR pass's eye matrices immediately before this callback.
            UpdatePortalPreview(camera, context);
        }

        void UpdatePortalPreview(Camera renderingViewer = null, ScriptableRenderContext? renderContext = null)
        {
            if (!ApplyRuntimePortalVisibility() || !portalPreviewEnabled || !isActiveAndEnabled || !HasDestination)
            {
                ReleasePortalPreview();
                return;
            }
            if (capturingPortal || !gameObject.scene.IsValid() || !gameObject.scene.isLoaded) return;
            if (portalRenderer == null) portalRenderer = GetComponent<MeshRenderer>();
            if (portalRenderer == null || portalRenderer.sharedMaterial == null
                || !portalRenderer.sharedMaterial.HasProperty(HasPortalImage)) return;
            var pose = DestinationPose;
            var viewer = renderingViewer != null ? renderingViewer : Camera.main;
            var live = Application.isPlaying && realtimeWhenNearby && LivePortalVisible(viewer);
            if (!live) ReleaseLiveCaptureCamera();
            if (live != wasLiveActive) nextPortalCapture = 0;
            wasLiveActive = live;
            if (live && !renderContext.HasValue) return;
            var eyeSeparation = PortalEyeSeparation(viewer);
            var convergence = PortalConvergence();
            if (convergence <= 0f) { ReleasePortalPreview(); return; }
            if (!live && portalImage != null && !portalImageDirty && !capturedPerspective
                && capturedResolution == PortalResolution
                && Mathf.Abs(eyeSeparation - capturedEyeSeparation) < .001f
                && Mathf.Abs(convergence - capturedConvergence) < .001f
                && (pose.position - capturedDestination.position).sqrMagnitude < .000001f
                && Quaternion.Angle(pose.rotation, capturedDestination.rotation) < .01f) return;
            var now = Time.realtimeSinceStartupAsDouble;
            var multipass = live && viewer.stereoEnabled && XRSettings.stereoRenderingMode == XRSettings.StereoRenderingMode.MultiPass;
            var activeEye = multipass ? viewer.stereoActiveEye : Camera.MonoOrStereoscopicEye.Mono;
            var captureEye = activeEye == Camera.MonoOrStereoscopicEye.Left ? 0 : activeEye == Camera.MonoOrStereoscopicEye.Right ? 1 : -1;
            var eyeMask = captureEye < 0 ? 3 : 1 << captureEye;
            var sameFrame = live && liveCaptureFrame == Time.frameCount;
            // Multi-pass updates only its current eye. If Unity cannot identify it, refresh both
            // on each pass rather than display an eye left over from the previous frame.
            if (sameFrame && (!multipass || captureEye >= 0 && (liveCaptureEyes & eyeMask) == eyeMask)) return;
            if (now < nextPortalCapture && !sameFrame || Application.isPlaying && Time.frameCount < earliestPortalFrame) return;
            nextPortalCapture = now + (live ? LiveCaptureInterval : .5);
            CapturePortalImage(pose, eyeSeparation, live ? viewer : null, renderContext, captureEye);
            if (live)
            {
                liveCaptureEyes = sameFrame ? liveCaptureEyes | eyeMask : eyeMask;
                liveCaptureFrame = Time.frameCount;
            }
            capturedDestination = pose;
            capturedEyeSeparation = eyeSeparation;
            capturedConvergence = convergence;
            capturedPerspective = live;
            capturedResolution = PortalResolution;
            portalImageDirty = false;
        }

        internal int PortalResolution => Mathf.Clamp(Mathf.ClosestPowerOfTwo(Mathf.Max(1, portalResolution)), 64, 1024);
        internal double LiveCaptureInterval => realtimeRefreshRate > 0f ? 1.0 / realtimeRefreshRate : 0.0;

        internal bool LivePortalVisible(Camera viewer)
        {
            if (viewer == null) return false;
            if (portalRenderer == null) portalRenderer = GetComponent<MeshRenderer>();
            if (portalRenderer == null || !portalRenderer.enabled || portalRenderer.forceRenderingOff) return false;
            if ((viewer.cullingMask & 1 << portalRenderer.gameObject.layer) == 0
                || Mathf.Abs(Vector3.Dot(transform.forward, viewer.transform.position - transform.position)) < .025f) return false;
            var bounds = portalRenderer.bounds;
            var distance = Mathf.Max(0f, realtimeDistance);
            if ((bounds.ClosestPoint(viewer.transform.position) - viewer.transform.position).sqrMagnitude > distance * distance) return false;
            GeometryUtility.CalculateFrustumPlanes(viewer.cullingMatrix, portalFrustum);
            return GeometryUtility.TestPlanesAABB(portalFrustum, bounds);
        }

        bool ApplyRuntimePortalVisibility()
        {
            if (!Application.isPlaying || showRuntimeVisual)
            {
                RestoreRuntimePortalVisibility();
                return true;
            }
            var renderer = GetComponent<MeshRenderer>();
            if (renderer != hiddenRuntimeRenderer)
            {
                RestoreRuntimePortalVisibility();
                hiddenRuntimeRenderer = renderer;
                if (renderer != null) originalRuntimeForceOff = renderer.forceRenderingOff;
            }
            if (hiddenRuntimeRenderer != null) hiddenRuntimeRenderer.forceRenderingOff = true;
            ReleasePortalPreview();
            return false;
        }

        void RestoreRuntimePortalVisibility()
        {
            if (hiddenRuntimeRenderer != null && hiddenRuntimeRenderer.forceRenderingOff)
                hiddenRuntimeRenderer.forceRenderingOff = originalRuntimeForceOff;
            hiddenRuntimeRenderer = null;
        }

        internal static float PortalEyeSeparation(Camera camera)
        {
            if (camera == null || !camera.stereoEnabled) return 0f;
            var left = camera.GetStereoViewMatrix(Camera.StereoscopicEye.Left).inverse.GetColumn(3);
            var right = camera.GetStereoViewMatrix(Camera.StereoscopicEye.Right).inverse.GetColumn(3);
            var separation = Vector3.Distance(left, right);
            return separation > 0f && !float.IsInfinity(separation) ? separation : 0f;
        }

        internal static Matrix4x4 PortalMapping(Pose entrance, Pose destination) =>
            Matrix4x4.TRS(destination.position, destination.rotation, Vector3.one)
            * Matrix4x4.TRS(entrance.position, entrance.rotation, Vector3.one).inverse;

        internal void CapturePortalImage(Pose pose, float eyeSeparation, Camera perspectiveCamera = null,
            ScriptableRenderContext? renderContext = null, int captureEye = -1)
        {
            var convergence = PortalConvergence();
            if (convergence <= 0f) return;
            if (portalRenderer == null) portalRenderer = GetComponent<MeshRenderer>();
            if (originalPortalProperties == null)
            {
                originalPortalProperties = new MaterialPropertyBlock();
                portalRenderer.GetPropertyBlock(originalPortalProperties);
            }
            var stereo = eyeSeparation > 0f;
            var resolution = PortalResolution;
            var width = stereo ? resolution * 2 : resolution;
            if (portalImage == null || portalImage.width != width || portalImage.height != resolution)
            {
                DestroyPortalImage();
                portalImage = new RenderTexture(width, resolution, 0, RenderTextureFormat.ARGB32)
                {
                    name = "Teleporter destination", hideFlags = HideFlags.HideAndDontSave,
                    antiAliasing = 1, useMipMap = false
                };
                portalImage.Create();
            }

            GameObject cameraObject = null;
            RenderTexture captureTarget = null;
            var retainCamera = Application.isPlaying && perspectiveCamera != null;
            hiddenPortals.Clear();
#if UNITY_EDITOR
            Scene previewScene = default;
#endif
            var previousTarget = RenderTexture.active;
            capturingPortal = true;
            try
            {
                Camera camera;
                if (retainCamera && liveCaptureCamera != null)
                {
                    camera = liveCaptureCamera;
                    cameraObject = camera.gameObject;
                }
                else
                {
                    cameraObject = new GameObject("Teleporter snapshot camera") { hideFlags = HideFlags.HideAndDontSave };
                    camera = cameraObject.AddComponent<Camera>();
                    camera.enabled = false;
                    cameraObject.AddComponent<UniversalAdditionalCameraData>().allowXRRendering = false;
                    if (retainCamera) liveCaptureCamera = camera;
                }
#if UNITY_EDITOR
                // Keep temporary capture objects out of authored scenes, including their dirty state.
                if (!Application.isPlaying)
                {
                    previewScene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
                    SceneManager.MoveGameObjectToScene(cameraObject, previewScene);
                }
#endif
#if UNITY_EDITOR
                if (!Application.isPlaying) camera.scene = gameObject.scene;
#endif
                camera.transform.SetPositionAndRotation(pose.position + Vector3.up * 1.6f, pose.rotation);
                var main = perspectiveCamera != null ? perspectiveCamera : Camera.main;
                camera.cullingMask = (main != null ? main.cullingMask : ~0) & ~(1 << 5) & ~(1 << 22);
                camera.clearFlags = main != null ? main.clearFlags : CameraClearFlags.Skybox;
                camera.backgroundColor = main != null ? main.backgroundColor : new Color(.04f, .07f, .1f);
                camera.farClipPlane = main != null ? main.farClipPlane : 500f;
                camera.nearClipPlane = .05f;
                camera.fieldOfView = PortalFieldOfView;
                camera.aspect = 1f;
                camera.allowHDR = false;
                camera.allowMSAA = false;
                camera.ResetWorldToCameraMatrix();
                camera.ResetProjectionMatrix();
                var projection = camera.projectionMatrix;
                // Align the stereo convergence plane with this portal's physical width, without toe-in.
                var descriptor = portalImage.descriptor;
                descriptor.width = resolution;
                descriptor.depthBufferBits = 24;
                captureTarget = RenderTexture.GetTemporary(descriptor);
                camera.targetTexture = captureTarget;
                // Avoid recursive portal pictures without changing collider or GameObject state.
                hiddenPortals.Add((portalRenderer, portalRenderer.forceRenderingOff));
                portalRenderer.forceRenderingOff = true;
#if UNITY_EDITOR
                // Edit-mode callbacks are not guaranteed for ordinary scene components after reload.
                if (!Application.isPlaying)
                {
                    foreach (var portal in FindObjectsByType<BSTeleporter>(FindObjectsSortMode.None)) HidePortalDuringCapture(portal);
                }
                else
#endif
                {
                    foreach (var portal in enabledPortals) HidePortalDuringCapture(portal);
                }
                var mapping = PortalMapping(new Pose(transform.position, transform.rotation), pose);
                var properties = portalProperties ??= new MaterialPropertyBlock();
                portalRenderer.GetPropertyBlock(properties);
                var leftProjection = properties.GetMatrix(PortalViewLeft);
                var rightProjection = properties.GetMatrix(PortalViewRight);
                for (var eye = 0; eye < (stereo ? 2 : 1); eye++)
                {
                    if (stereo && captureEye >= 0 && eye != captureEye) continue;
                    var offset = stereo ? (eye == 0 ? -.5f : .5f) * eyeSeparation : 0f;
                    if (perspectiveCamera != null)
                    {
                        var eyeId = eye == 0 ? Camera.StereoscopicEye.Left : Camera.StereoscopicEye.Right;
                        var sourceView = stereo ? perspectiveCamera.GetStereoViewMatrix(eyeId) : perspectiveCamera.worldToCameraMatrix;
                        var sourceProjection = stereo ? perspectiveCamera.GetStereoProjectionMatrix(eyeId) : perspectiveCamera.projectionMatrix;
                        var view = sourceView * mapping.inverse;
                        var inverseView = view.inverse;
                        camera.transform.SetPositionAndRotation(inverseView.GetColumn(3),
                            Quaternion.LookRotation(-inverseView.GetColumn(2), inverseView.GetColumn(1)));
                        camera.worldToCameraMatrix = view;
                        camera.projectionMatrix = sourceProjection;
                        var normal = pose.rotation * Vector3.forward;
                        var side = Vector3.Dot(normal, camera.transform.position - pose.position) < 0f ? 1f : -1f;
                        var clipNormal = view.MultiplyVector(normal * side).normalized;
                        var clipPoint = view.MultiplyPoint(pose.position + normal * side * .01f);
                        camera.projectionMatrix = camera.CalculateObliqueMatrix(new Vector4(clipNormal.x, clipNormal.y, clipNormal.z,
                            -Vector3.Dot(clipNormal, clipPoint)));
                        var projectedView = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true) * sourceView;
                        if (eye == 0) leftProjection = projectedView;
                        else rightProjection = projectedView;
                    }
                    else
                    {
                        camera.transform.position = pose.position + Vector3.up * 1.6f + pose.rotation * Vector3.right * offset;
                        projection.m02 = -projection.m00 * offset / convergence;
                        camera.projectionMatrix = projection;
                    }
                    if (renderContext.HasValue)
                    {
                        // This installed URP API uses the current context without nesting camera
                        // callbacks or rebuilding the XR layout, unlike Camera.Render/render requests.
#pragma warning disable CS0618
                        UniversalRenderPipeline.RenderSingleCamera(renderContext.Value, camera);
#pragma warning restore CS0618
                    }
                    else camera.Render();
                    Graphics.CopyTexture(captureTarget, 0, 0, 0, 0, resolution, resolution, portalImage, 0, 0, eye * resolution, 0);
                }
                camera.targetTexture = null;
                properties.SetTexture(PortalImage, portalImage);
                properties.SetFloat(HasPortalImage, 1f);
                properties.SetFloat(StereoPortalImage, stereo ? 1f : 0f);
                properties.SetFloat(PerspectivePortalImage, perspectiveCamera != null ? 1f : 0f);
                properties.SetMatrix(PortalViewLeft, leftProjection);
                properties.SetMatrix(PortalViewRight, stereo ? rightProjection : leftProjection);
                portalRenderer.SetPropertyBlock(properties);
            }
            finally
            {
                foreach (var hidden in hiddenPortals)
                    if (hidden.renderer != null) hidden.renderer.forceRenderingOff = hidden.hidden;
                RenderTexture.active = previousTarget;
                if (cameraObject != null) cameraObject.GetComponent<Camera>().targetTexture = null;
                if (captureTarget != null) RenderTexture.ReleaseTemporary(captureTarget);
                hiddenPortals.Clear();
                if (cameraObject != null && !retainCamera)
                {
                    if (Application.isPlaying) Destroy(cameraObject);
                    else DestroyImmediate(cameraObject);
                }
#if UNITY_EDITOR
                if (previewScene.IsValid()) UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(previewScene);
#endif
                capturingPortal = false;
            }
        }

        float PortalConvergence()
        {
            var mesh = GetComponent<MeshFilter>()?.sharedMesh;
            return mesh != null ? Mathf.Max(.05f, mesh.bounds.extents.x * transform.TransformVector(Vector3.right).magnitude
                / Mathf.Tan(PortalFieldOfView * .5f * Mathf.Deg2Rad)) : 0f;
        }

        void HidePortalDuringCapture(BSTeleporter portal)
        {
            if (portal == null) return;
            var renderer = portal.GetComponent<MeshRenderer>();
            if (renderer == null || renderer == portalRenderer) return;
            hiddenPortals.Add((renderer, renderer.forceRenderingOff));
            renderer.forceRenderingOff = true;
        }

        void ReleasePortalPreview()
        {
            ReleaseLiveCaptureCamera();
            if (portalRenderer != null && originalPortalProperties != null)
            {
                var properties = portalProperties ??= new MaterialPropertyBlock();
                portalRenderer.GetPropertyBlock(properties);
                var material = portalRenderer.sharedMaterial;
                properties.SetTexture(PortalImage, originalPortalProperties.GetTexture(PortalImage)
                    ?? (material != null ? material.GetTexture(PortalImage) : null) ?? Texture2D.blackTexture);
                properties.SetFloat(HasPortalImage, originalPortalProperties.HasFloat(HasPortalImage)
                    ? originalPortalProperties.GetFloat(HasPortalImage) : material != null ? material.GetFloat(HasPortalImage) : 0f);
                properties.SetFloat(StereoPortalImage, originalPortalProperties.HasFloat(StereoPortalImage)
                    ? originalPortalProperties.GetFloat(StereoPortalImage) : material != null ? material.GetFloat(StereoPortalImage) : 0f);
                properties.SetFloat(PerspectivePortalImage, originalPortalProperties.HasFloat(PerspectivePortalImage)
                    ? originalPortalProperties.GetFloat(PerspectivePortalImage) : material != null ? material.GetFloat(PerspectivePortalImage) : 0f);
                properties.SetMatrix(PortalViewLeft, originalPortalProperties.HasMatrix(PortalViewLeft)
                    ? originalPortalProperties.GetMatrix(PortalViewLeft) : material != null && material.HasMatrix(PortalViewLeft) ? material.GetMatrix(PortalViewLeft) : Matrix4x4.identity);
                properties.SetMatrix(PortalViewRight, originalPortalProperties.HasMatrix(PortalViewRight)
                    ? originalPortalProperties.GetMatrix(PortalViewRight) : material != null && material.HasMatrix(PortalViewRight) ? material.GetMatrix(PortalViewRight) : Matrix4x4.identity);
                portalRenderer.SetPropertyBlock(properties);
            }
            originalPortalProperties = null;
            portalRenderer = null;
            DestroyPortalImage();
            portalImageDirty = true;
            nextPortalCapture = 0;
            wasLiveActive = false;
            liveCaptureFrame = -1;
            liveCaptureEyes = 0;
        }

        void DestroyPortalImage()
        {
            if (portalImage != null)
            {
                portalImage.Release();
                if (Application.isPlaying) Destroy(portalImage);
                else DestroyImmediate(portalImage);
                portalImage = null;
            }
        }

        void ReleaseLiveCaptureCamera()
        {
            if (liveCaptureCamera == null) return;
            var cameraObject = liveCaptureCamera.gameObject;
            liveCaptureCamera = null;
            if (Application.isPlaying) Destroy(cameraObject);
            else DestroyImmediate(cameraObject);
        }
    }
}

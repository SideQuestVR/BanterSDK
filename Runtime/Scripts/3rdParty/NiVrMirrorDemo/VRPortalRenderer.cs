using UnityEngine;
using UnityEngine.XR;
using System.Collections.Generic;

[DefaultExecutionOrder(700)]
public class VRPortalRenderer : MonoBehaviour {
    static readonly List<VRPortalRenderer> Active = new List<VRPortalRenderer>();
    public static IReadOnlyList<VRPortalRenderer> ActiveMirrors => Active;
    Renderer _surface;

    public bool TryGetJiggleView(bool rightEye, out Matrix4x4 worldToClip, out Vector3 position, out int mask) {
        worldToClip = Matrix4x4.identity;
        position = default;
        mask = cameraViewMask;
        Camera camera = _srcCamera;
        if (!camera) return false;
        // updateEyePos uses the main-camera pose in non-XR, even with a source override.
        var poseCamera = XRSettings.isDeviceActive ? camera : Camera.main;
        if (!poseCamera) return false;
        var eyePosition = poseCamera.transform.position;
        var eyeRotation = poseCamera.transform.rotation;
        if (XRSettings.isDeviceActive) {
            TryGetEye(out eyePosition, out eyeRotation, rightEye ? XRNode.RightEye : XRNode.LeftEye);
            var parent = camera.transform.parent;
            if (parent) { eyePosition = parent.TransformPoint(eyePosition); eyeRotation = parent.rotation * eyeRotation; }
        }
        return TryGetJiggleView(eyePosition, eyeRotation, camera.nearClipPlane, camera.farClipPlane,
            out worldToClip, out position, out mask);
    }

    /// <summary>Conservative surface test used by animation budgets; this does not render the mirror.</summary>
    public bool SurfaceInView(Plane[] planes, int cullingMask) {
        return isActiveAndEnabled && _surface && _surface.enabled && _surface.gameObject.activeInHierarchy
            && (cullingMask & (1 << _surface.gameObject.layer)) != 0
            && Finite(_surface.bounds.center) && Finite(_surface.bounds.extents)
            && GeometryUtility.TestPlanesAABB(planes, _surface.bounds);
    }

    /// <summary>A conservative reflected/portal frustum, without the oblique near-plane cut.</summary>
    public bool TryGetJiggleView(Vector3 eyePosition, Quaternion eyeRotation, float near, float far,
        out Matrix4x4 worldToClip, out Vector3 position, out int mask) {
        worldToClip = Matrix4x4.identity;
        position = default;
        mask = cameraViewMask;
        if (!isActiveAndEnabled || !float.IsFinite(near) || !float.IsFinite(far) || near <= 0f || far <= near
            || !float.IsFinite(cameraFov) || cameraFov <= 0f || cameraFov >= 180f || !Finite(eyePosition)
            || !float.IsFinite(eyeRotation.x) || !float.IsFinite(eyeRotation.y)
            || !float.IsFinite(eyeRotation.z) || !float.IsFinite(eyeRotation.w)) return false;
        var source = new Coord(transform);
        var target = !mirrorMode && portalTransform ? new Coord(portalTransform) : source;
        Vector3 p = source.worldToLocalPos(eyePosition);
        Vector3 f = source.worldToLocalDir(eyeRotation * Vector3.forward);
        Vector3 u = source.worldToLocalDir(eyeRotation * Vector3.up);
        if (mirrorMode) {
            p = Vector3.Reflect(p, Vector3.up);
            f = Vector3.Reflect(f, Vector3.up);
            u = Vector3.Reflect(u, Vector3.up);
        }
        position = target.localToWorldPos(p);
        f = target.localToWorldDir(f);
        u = target.localToWorldDir(u);
        if (!Finite(position) || !Finite(f) || !Finite(u) || f.sqrMagnitude < 0.0001f
            || Vector3.Cross(f, u).sqrMagnitude < 0.0001f) return false;
        Quaternion rotation = Quaternion.LookRotation(f, u);
        var view = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * Matrix4x4.TRS(position, rotation, Vector3.one).inverse;
        // Mirror renders use the viewer's aspect (at least 1), portals 1; before the first render the game view's.
        float aspect = renderCam ? Mathf.Max(1f, renderCam.aspect) : 1f;
        worldToClip = Matrix4x4.Perspective(cameraFov, aspect, near, far) * view;
        return true;
    }

    static bool Finite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    [Header("Camera")]
    [SerializeField] private Camera sourceCamOverride = null;
    [SerializeField] private LayerMask cameraViewMask;
    [SerializeField] public int renderTargetSize = 1024;
    [SerializeField] private float cameraFov = 120.0f;
    [SerializeField] private CameraClearFlags cameraClear = CameraClearFlags.Skybox;

    [Header("Portals")]
    [Tooltip("When true the mirror mode will be used (reflection). When false, the portal mode is used.")]
    public bool mirrorMode = true;
    [Tooltip("Portal transform to use when mirrorMode is false (portal mode).")]
    public Transform portalTransform;

    [Header("Shader Parameters")]
    [SerializeField] private string eyeTexLParam = "EyeTexL";
    [SerializeField] private string eyeTexRParam = "EyeTexR";
    [SerializeField] private string eyeViewMatLParam = "EyeViewMatrixL";
    [SerializeField] private string eyeViewMatRParam = "EyeViewMatrixR";
    [SerializeField] private string eyeProjMatLParam = "EyeProjMatrixL";
    [SerializeField] private string eyeProjMatRParam = "EyeProjMatrixR";
    [SerializeField] private Material targetMaterial;

    [Header("Internals (do not touch)")]
    [SerializeField] private Pose deviceEyePoseL;
    [SerializeField] private Pose deviceEyePoser;
    [SerializeField] private Pose worldEyePoseL;
    [SerializeField] private Pose worldEyePoser;

    [SerializeField] private GameObject renderCamObj;
    [SerializeField] private Camera renderCam;
    [SerializeField] private GameObject eyeDebugObjL;
    [SerializeField] private GameObject eyeDebugObjR;

    [SerializeField] private RenderTexture renderTexL = null;
    [SerializeField] private RenderTexture renderTexR = null;

    [SerializeField] private Matrix4x4 eyeProjL = Matrix4x4.identity;
    [SerializeField] private Matrix4x4 eyeProjR = Matrix4x4.identity;
    [SerializeField] private Matrix4x4 eyeViewL = Matrix4x4.identity;
    [SerializeField] private Matrix4x4 eyeViewR = Matrix4x4.identity;

    // Use the override camera if assigned; otherwise default to Camera.main.
    private Camera _srcCamera {
        get => sourceCamOverride ? sourceCamOverride : Camera.main;
    }

    #region Public Methods

    public void SetRenderTextureSize(int size) {
        // Set from JS/VS: 0 throws in new RenderTexture, and a huge size exhausts Quest memory. Even 4096 costs
        // about 192 MB per mirror (two eyes, each 64 MB colour + 32 MB 16-bit depth).
        size = Mathf.Clamp(size, MinTextureSize, MaxTextureSize);
        if (ownedL && ownedR && ownedL.width == size) { renderTargetSize = size; return; }
        renderTargetSize = size;
        if (isActiveAndEnabled) CreateRenderTextures();
        else DestroyRenderTextures(); // OnEnable creates them at the new size.
    }

    public void SetCameraClear(int clear) {
        cameraClear = (CameraClearFlags)clear;
    }

    public void SetCameraColor(string color) {
        if (renderCam) {
            renderCam.backgroundColor = ColorUtility.TryParseHtmlString(color, out Color c) ? c : Color.black;
        }
    }

    public void SetCullingLayer(int mask) {
        cameraViewMask = 1 << mask;
    }

    public void AddCullingLayer(int mask) {
        cameraViewMask |= 1 << mask;
    }

    // Optional public setters for runtime changes.
    public void SetMirrorMode(bool mode) {
        mirrorMode = mode;
    }

    public void SetPortalTransform(Transform t) {
        portalTransform = t;
    }

    #endregion

    #region Initialization & Cleanup

    const int MinTextureSize = 16, MaxTextureSize = 4096;

    // The textures this instance made. The serialized fields may instead hold a copied mirror's (see OnEnable).
    [System.NonSerialized] RenderTexture ownedL, ownedR;

    void CreateRenderTextures() {
        DestroyRenderTextures(); // Re-creating (resize, re-enable) must not leak the previous pair.
        int size = Mathf.Clamp(renderTargetSize, MinTextureSize, MaxTextureSize);
        renderTexL = ownedL = new RenderTexture(size, size, 16) { hideFlags = HideFlags.DontSave };
        renderTexR = ownedR = new RenderTexture(size, size, 16) { hideFlags = HideFlags.DontSave };
        renderTexL.Create();
        renderTexR.Create();
    }

    void OnEnable() {
        if (!Active.Contains(this)) Active.Add(this);
        _surface = GetComponent<Renderer>();
        DropInheritedObjects();
        CreateRenderTextures();

        renderCamObj = new GameObject("Render Camera");
        renderCamObj.hideFlags = HideFlags.DontSave;
        renderCamObj.transform.SetParent(transform);

        renderCam = renderCamObj.AddComponent<Camera>();
        renderCam.hideFlags = HideFlags.DontSave;
        renderCam.enabled = false;
        // Never an XR camera. stereoTargetEye only applies to the built-in renderer (a scriptable pipeline such as URP
        // logs a warning and ignores it); there, what keeps it out of XR is that it is only ever set up and rendered
        // while targeting a texture (see RenderEye).
        if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null)
            renderCam.stereoTargetEye = StereoTargetEyeMask.None;

        eyeDebugObjL = new GameObject("DebugEyeL");
        eyeDebugObjL.hideFlags = HideFlags.DontSave;
        eyeDebugObjL.transform.SetParent(transform);

        eyeDebugObjR = new GameObject("DebugEyeR");
        eyeDebugObjR.hideFlags = HideFlags.DontSave;
        eyeDebugObjR.transform.SetParent(transform);
    }

    void DestroyRenderTextures() {
        // A camera must not target a texture being released ("Releasing render texture that is set as
        // Camera.targetTexture!"), and Release alone left the RenderTexture objects behind.
        if (renderCam) renderCam.targetTexture = null;
        DestroyOwned(ref renderTexL, ref ownedL);
        DestroyOwned(ref renderTexR, ref ownedR);
    }

    // Only a texture this instance made is destroyed; anything else in the field (another mirror's) is just dropped.
    static void DestroyOwned(ref RenderTexture texture, ref RenderTexture owned) {
        if (owned) {
            owned.Release();
            DestroyRuntimeObject(owned);
        }
        texture = null;
        owned = null;
    }

    // A copy of a live mirror (JS instantiate, spawner copies) arrives with the original's runtime objects in the
    // serialized internals: its textures, and its camera and debug children (or copies of them). Leave the original's
    // alone, remove copied children, and start clean. After OnDisable it finds only this instance's own, already
    // being destroyed.
    void DropInheritedObjects() {
        DiscardChild(ref renderCamObj);
        DiscardChild(ref eyeDebugObjL);
        DiscardChild(ref eyeDebugObjR);
        renderCam = null;
        if (renderTexL != ownedL) renderTexL = null;
        if (renderTexR != ownedR) renderTexR = null;
    }

    // Runtime objects are DontSave, which authored objects can never be, so nothing a creator made is destroyed here.
    void DiscardChild(ref GameObject child) {
        if (child && child.transform.parent == transform && (child.hideFlags & HideFlags.DontSave) == HideFlags.DontSave)
            DestroyRuntimeObject(child);
        child = null;
    }

    static void DestroyRuntimeObject(Object target) {
        if (Application.isPlaying) Destroy(target); else DestroyImmediate(target);
    }

    void OnDisable() {
        Active.Remove(this);
        DestroyRenderTextures();
        if (renderCamObj != null) Destroy(renderCamObj);
        if (eyeDebugObjL != null) Destroy(eyeDebugObjL);
        if (eyeDebugObjR != null) Destroy(eyeDebugObjR);
    }

    #endregion

    #region Eye Position

    // Retrieves the XR eye (left/right) position and rotation.
    private bool TryGetEye(out Vector3 position, out Quaternion rotation, XRNode eye) {
        InputFeatureUsage<Vector3> posUsage = (eye == XRNode.RightEye) ? CommonUsages.rightEyePosition : CommonUsages.leftEyePosition;
        InputFeatureUsage<Quaternion> rotUsage = (eye == XRNode.RightEye) ? CommonUsages.rightEyeRotation : CommonUsages.leftEyeRotation;

        InputDevice device = InputDevices.GetDeviceAtXRNode(XRNode.Head);
        if (device.isValid) {
            if (device.TryGetFeatureValue(posUsage, out position) &&
                device.TryGetFeatureValue(rotUsage, out rotation))
                return true;
        }
        position = Vector3.zero;
        rotation = Quaternion.identity;
        return false;
    }

    // Update the current eye poses from XR (or fallback to Camera.main).
    void updateEyePos() {
        if (XRSettings.isDeviceActive) {
            TryGetEye(out deviceEyePoseL.position, out deviceEyePoseL.rotation, XRNode.LeftEye);
            TryGetEye(out deviceEyePoser.position, out deviceEyePoser.rotation, XRNode.RightEye);
        } else {
            deviceEyePoseL.position = deviceEyePoser.position = Camera.main.transform.position;
            deviceEyePoseL.rotation = deviceEyePoser.rotation = Camera.main.transform.rotation;
        }

        Camera cam = _srcCamera;
        Transform camParent = cam.transform.parent;
        if (camParent == null || !XRSettings.isDeviceActive) {
            worldEyePoseL = deviceEyePoseL;
            worldEyePoser = deviceEyePoser;
        } else {
            worldEyePoseL.position = camParent.TransformPoint(deviceEyePoseL.position);
            worldEyePoseL.rotation = camParent.rotation * deviceEyePoseL.rotation;
            worldEyePoser.position = camParent.TransformPoint(deviceEyePoser.position);
            worldEyePoser.rotation = camParent.rotation * deviceEyePoser.rotation;
        }

        eyeDebugObjL.transform.position = worldEyePoseL.position;
        eyeDebugObjL.transform.rotation = worldEyePoseL.rotation;
        eyeDebugObjR.transform.position = worldEyePoser.position;
        eyeDebugObjR.transform.rotation = worldEyePoser.rotation;
    }

    #endregion

    #region Rendering

    // Nearer the glass than this, an eye's reflection is not re-rendered (it keeps the last image).
    const float MinEyeDistance = 0.01f;

    // Renders the given eye view to the provided RenderTexture and outputs the view and projection matrices.
    // Returns false, rendering nothing, when this eye can see no reflection: the caller keeps the last image.
    bool renderToTexture(RenderTexture rt, Pose eyePose, out Matrix4x4 viewMat, out Matrix4x4 projMat) {
        viewMat = Matrix4x4.identity;
        projMat = Matrix4x4.identity;

        Camera srcCam = _srcCamera;
        if (!srcCam || !rt || SurfaceBehindEye(eyePose)) return false;

        Vector3 mirrorNormal = Vector3.up;
        if (mirrorMode) {
            // Only the side the mirror is drawn on is reflected (its shaders cull back faces). From behind, the oblique cut
            // below would keep the reflected camera's own side (a degenerate frustum); as the eye reaches the glass, the
            // cut's near plane reaches the camera and its depth range collapses. The cut replaces the camera's near
            // plane, so a world's near clip does not matter here: only a fixed distance from the glass.
            Coord mirrorCoord = new Coord(transform);
            mirrorNormal = DrawnFaceNormal(mirrorCoord.y);
            if (Vector3.Dot(mirrorNormal, eyePose.position - mirrorCoord.pos) <= MinEyeDistance) return false;
        }

        renderCam.transform.position = eyePose.position;
        renderCam.transform.rotation = eyePose.rotation;

        // Target the texture before the projection is set up: a camera rendering to the display is an XR camera while
        // XR is on, and then ignores fieldOfView (and logs about it). Released again on every way out below.
        renderCam.targetTexture = rt;

        // Assigning projectionMatrix (the oblique cut below) freezes it until this call. Without it each render cut the
        // previous eye's or frame's cut again, collapsing the depth range, and fov/near/far/aspect were ignored.
        renderCam.ResetProjectionMatrix();
        renderCam.nearClipPlane = srcCam.nearClipPlane;
        renderCam.farClipPlane = srcCam.farClipPlane;
        renderCam.fieldOfView = cameraFov;
        // Square textures. A mirror covers as wide a view as the viewer's screen, like the first frame's matrix that used
        // to stay frozen; a portal keeps the square aspect it always had.
        float viewerAspect = srcCam.aspect;
        renderCam.aspect = mirrorMode && viewerAspect > 1f && float.IsFinite(viewerAspect) ? viewerAspect : 1f;
        renderCam.cullingMask = cameraViewMask;
        renderCam.clearFlags = cameraClear;

        viewMat = renderCam.worldToCameraMatrix;

        Vector3 clipPlanePos = Vector3.zero;
        Vector3 clipPlaneNormal = Vector3.up;
        bool useOblique = false;

        // When mirrorMode is false, do portal mode; otherwise use mirror mode.
        if (!mirrorMode && portalTransform != null) {
            // Portal mode: transform the eye pose from this object's coordinate space into the portal's.
            Coord srcCoord = new Coord(transform);
            Coord dstCoord = new Coord(portalTransform);

            Vector3 localEyePos = srcCoord.worldToLocalPos(eyePose.position);
            Vector3 srcEyeUp = eyePose.rotation * Vector3.up;
            Vector3 srcEyeForward = eyePose.rotation * Vector3.forward;
            Vector3 localEyeUp = srcCoord.worldToLocalDir(srcEyeUp);
            Vector3 localEyeForward = srcCoord.worldToLocalDir(srcEyeForward);

            Vector3 newEyePos = dstCoord.localToWorldPos(localEyePos);
            Vector3 newEyeUp = dstCoord.localToWorldDir(localEyeUp);
            Vector3 newEyeForward = dstCoord.localToWorldDir(localEyeForward);
            Quaternion newEyeRot = Quaternion.LookRotation(newEyeForward, newEyeUp);

            eyePose.position = newEyePos;
            eyePose.rotation = newEyeRot;
            renderCam.transform.position = eyePose.position;
            renderCam.transform.rotation = eyePose.rotation;
        }
        else if (mirrorMode) {
            // Mirror mode: reflect the eye pose across a plane.
            Coord srcCoord = new Coord(transform);
            Vector3 localEyePos = srcCoord.worldToLocalPos(eyePose.position);
            Vector3 localEyeUp = srcCoord.worldToLocalDir(eyePose.rotation * Vector3.up);
            Vector3 localEyeForward = srcCoord.worldToLocalDir(eyePose.rotation * Vector3.forward);

            // Assume a horizontal mirror plane (normal = Vector3.up).
            Vector3 planeNormal = Vector3.up;
            localEyePos = Vector3.Reflect(localEyePos, planeNormal);
            localEyeUp = Vector3.Reflect(localEyeUp, planeNormal);
            localEyeForward = Vector3.Reflect(localEyeForward, planeNormal);

            Vector3 newEyePos = srcCoord.localToWorldPos(localEyePos);
            Vector3 newEyeUp = srcCoord.localToWorldDir(localEyeUp);
            Vector3 newEyeForward = srcCoord.localToWorldDir(localEyeForward);
            Quaternion newEyeRot = Quaternion.LookRotation(newEyeForward, newEyeUp);

            eyePose.position = newEyePos;
            eyePose.rotation = newEyeRot;
            renderCam.transform.position = eyePose.position;
            renderCam.transform.rotation = eyePose.rotation;

            // Set up an oblique near-clip plane so that only what is in front of the mirror is rendered.
            clipPlanePos = srcCoord.pos;
            clipPlaneNormal = mirrorNormal;
            useOblique = true;
        }

        projMat = renderCam.projectionMatrix;
        if (useOblique) {
            Vector3 camSpacePos = renderCam.worldToCameraMatrix.MultiplyPoint(clipPlanePos);
            Vector3 camSpaceNormal = renderCam.worldToCameraMatrix.MultiplyVector(clipPlaneNormal);
            Vector4 clipPlane = new Vector4(camSpaceNormal.x, camSpaceNormal.y, camSpaceNormal.z,
                                            -Vector3.Dot(camSpaceNormal, camSpacePos));
            // The cut scales the plane by 2 / dot(plane, the far frustum corner on its side). At or below zero the
            // reflected view never reaches in front of the mirror (the eye looks away from it), and the cut would flip.
            Vector4 farCorner = new Vector4((Mathf.Sign(clipPlane.x) + projMat.m02) / projMat.m00,
                (Mathf.Sign(clipPlane.y) + projMat.m12) / projMat.m11, -1f, (1f + projMat.m22) / projMat.m23);
            if (Vector4.Dot(clipPlane, farCorner) <= 1e-4f) { renderCam.targetTexture = null; return false; }
            Matrix4x4 obliqueProj = renderCam.CalculateObliqueMatrix(clipPlane);
            renderCam.projectionMatrix = obliqueProj;
            projMat = obliqueProj;
        }
        // In mirror mode flip the projection matrix along X.
        if (mirrorMode) {
            projMat *= Matrix4x4.Scale(new Vector3(-1.0f, 1.0f, 1.0f));
        }

        renderCam.enabled = true;
        renderCam.Render();
        renderCam.enabled = false;
        // Not left targeting a texture that may be released before the next render.
        renderCam.targetTexture = null;
        return true;
    }

    // Every point of the mirror is behind the eye: the eye can't see it, and the shader's projection of it is meaningless.
    bool SurfaceBehindEye(Pose eye) {
        if (!_surface) return false;
        Bounds bounds = _surface.bounds;
        Vector3 forward = eye.rotation * Vector3.forward;
        if (!Finite(bounds.center) || !Finite(bounds.extents) || !Finite(forward)) return false;
        Vector3 e = bounds.extents;
        return Vector3.Dot(forward, bounds.center - eye.position)
            + Mathf.Abs(forward.x) * e.x + Mathf.Abs(forward.y) * e.y + Mathf.Abs(forward.z) * e.z < 0f;
    }

    // Local +Y as the side the mirror is drawn on. A negative scale through the glass turns the drawn face over (Unity
    // flips culling for it), which transform.up does not show; row 1 of worldToLocalMatrix is +Y's inverse transpose.
    Vector3 DrawnFaceNormal(Vector3 up) {
        Vector4 facing = transform.worldToLocalMatrix.GetRow(1);
        return facing.x * up.x + facing.y * up.y + facing.z * up.z < 0f ? -up : up;
    }

    #endregion

    #region Shader Setup

    void setShaderParams() {
        if (targetMaterial) {
            targetMaterial.SetMatrix(eyeProjMatLParam, eyeProjL);
            targetMaterial.SetMatrix(eyeProjMatRParam, eyeProjR);
            targetMaterial.SetMatrix(eyeViewMatLParam, eyeViewL);
            targetMaterial.SetMatrix(eyeViewMatRParam, eyeViewR);
            targetMaterial.SetTexture(eyeTexLParam, renderTexL);
            targetMaterial.SetTexture(eyeTexRParam, renderTexR);
        }
        Shader.SetGlobalMatrix(eyeProjMatLParam, eyeProjL);
        Shader.SetGlobalMatrix(eyeProjMatRParam, eyeProjR);
        Shader.SetGlobalMatrix(eyeViewMatLParam, eyeViewL);
        Shader.SetGlobalMatrix(eyeViewMatRParam, eyeViewR);
        Shader.SetGlobalTexture(eyeTexLParam, renderTexL);
        Shader.SetGlobalTexture(eyeTexRParam, renderTexR);
    }

    #endregion

    void LateUpdate() {
        // Between worlds there may be no main camera, and without textures (disabled, resizing) there is nothing to draw.
        if (!renderCam || !renderTexL || !renderTexR || !_srcCamera || (!XRSettings.isDeviceActive && !Camera.main)) return;
        updateEyePos();
        bool rendered = false;
        if (renderToTexture(renderTexL, worldEyePoseL, out Matrix4x4 viewL, out Matrix4x4 projL)) {
            eyeViewL = viewL;
            eyeProjL = projL;
            rendered = true;
        }
        if (renderToTexture(renderTexR, worldEyePoser, out Matrix4x4 viewR, out Matrix4x4 projR)) {
            eyeViewR = viewR;
            eyeProjR = projR;
            rendered = true;
        }
        // An eye that saw no reflection keeps its last image. A mirror that drew nothing leaves the shared shader
        // globals to one that did.
        if (rendered) setShaderParams();
    }
}

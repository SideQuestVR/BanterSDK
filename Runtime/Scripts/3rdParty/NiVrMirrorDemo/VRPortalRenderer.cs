using UnityEngine;
using UnityEngine.XR;

public class VRPortalRenderer : MonoBehaviour {
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
        renderTargetSize = size;
        DestroyRenderTextures();
        CreateRenderTextures();
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

    void CreateRenderTextures() {
        renderTexL = new RenderTexture(renderTargetSize, renderTargetSize, 16);
        renderTexR = new RenderTexture(renderTargetSize, renderTargetSize, 16);
        renderTexL.Create();
        renderTexR.Create();
    }

    void OnEnable() {
        CreateRenderTextures();

        renderCamObj = new GameObject("Render Camera");
        renderCamObj.hideFlags = HideFlags.DontSave;
        renderCamObj.transform.SetParent(transform);

        renderCam = renderCamObj.AddComponent<Camera>();
        renderCam.hideFlags = HideFlags.DontSave;
        renderCam.enabled = false;

        eyeDebugObjL = new GameObject("DebugEyeL");
        eyeDebugObjL.hideFlags = HideFlags.DontSave;
        eyeDebugObjL.transform.SetParent(transform);

        eyeDebugObjR = new GameObject("DebugEyeR");
        eyeDebugObjR.hideFlags = HideFlags.DontSave;
        eyeDebugObjR.transform.SetParent(transform);
    }

    void DestroyRenderTextures() {
        if (renderTexL) {
            renderTexL.Release();
            renderTexL = null;
        }
        if (renderTexR) {
            renderTexR.Release();
            renderTexR = null;
        }
    }

    void OnDisable() {
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

    // Renders the given eye view to the provided RenderTexture and outputs the view and projection matrices.
    void renderToTexture(RenderTexture rt, Pose eyePose, out Matrix4x4 viewMat, out Matrix4x4 projMat) {
        viewMat = Matrix4x4.identity;
        projMat = Matrix4x4.identity;

        Camera srcCam = _srcCamera;

        renderCam.enabled = true;
        renderCam.transform.position = eyePose.position;
        renderCam.transform.rotation = eyePose.rotation;

        renderCam.nearClipPlane = srcCam.nearClipPlane;
        renderCam.farClipPlane = srcCam.farClipPlane;
        renderCam.fieldOfView = cameraFov;
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
            clipPlaneNormal = srcCoord.y;
            useOblique = true;
        }

        projMat = renderCam.projectionMatrix;
        if (useOblique) {
            Vector3 camSpacePos = renderCam.worldToCameraMatrix.MultiplyPoint(clipPlanePos);
            Vector3 camSpaceNormal = renderCam.worldToCameraMatrix.MultiplyVector(clipPlaneNormal);
            Vector4 clipPlane = new Vector4(camSpaceNormal.x, camSpaceNormal.y, camSpaceNormal.z,
                                            -Vector3.Dot(camSpaceNormal, camSpacePos));
            Matrix4x4 obliqueProj = renderCam.CalculateObliqueMatrix(clipPlane);
            renderCam.projectionMatrix = obliqueProj;
            projMat = obliqueProj;
        }
        // In mirror mode flip the projection matrix along X.
        if (mirrorMode) {
            projMat *= Matrix4x4.Scale(new Vector3(-1.0f, 1.0f, 1.0f));
        }

        renderCam.targetTexture = rt;
        renderCam.Render();
        renderCam.enabled = false;
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
        updateEyePos();
        renderToTexture(renderTexL, worldEyePoseL, out eyeViewL, out eyeProjL);
        renderToTexture(renderTexR, worldEyePoser, out eyeViewR, out eyeProjR);
        setShaderParams();
    }
}

using Banter.SDK;
using UnityEngine;
// using UnityEngine.InputSystem;
using UnityEngine.XR;

public class VRPortalRenderer: MonoBehaviour/*, IPlayerInputHandler*/{
	[Header("Camera")]
	[SerializeField] Camera sourceCamOverride = null;
	[SerializeField] LayerMask cameraViewMask;
	[SerializeField] public int renderTargetSize = 1024;
	[SerializeField] float cameraFov = 120.0f;
	[SerializeField] CameraClearFlags cameraClear = CameraClearFlags.Skybox;

	// [Header("Portals")]
	Transform portalEye;
	bool mirrorMode = true;

	[Header("Shader parameters")]
	[SerializeField] string eyeTexLParam = "EyeTexL";
	[SerializeField] string eyeTexRParam = "EyeTexR";
	[SerializeField] string eyeViewMatLParam = "EyeViewMatrixL";
	[SerializeField] string eyeViewMatRParam = "EyeViewMatrixR";
	[SerializeField] string eyeProjMatLParam = "EyeProjMatrixL";
	[SerializeField] string eyeProjMatRParam = "EyeProjMatrixR";
	[SerializeField] Material targetMaterial;

	[Header("Internals (do not touch)")]
	[SerializeField] Pose deviceEyePoseL;
	[SerializeField] Pose deviceEyePoseR;
	[SerializeField] Pose worldEyePoseL;
	[SerializeField] Pose worldEyePoseR;

	[SerializeField] GameObject renderCamObj;
	[SerializeField] Camera renderCam;
	[SerializeField] GameObject eyeDebugObjL;
	[SerializeField] GameObject eyeDebugObjR;

	[SerializeField]RenderTexture renderTexL = null;
	[SerializeField]RenderTexture renderTexR = null;

	[SerializeField] Matrix4x4 eyeProjL = Matrix4x4.identity;
	[SerializeField] Matrix4x4 eyeProjR = Matrix4x4.identity;
	[SerializeField] Matrix4x4 eyeViewL = Matrix4x4.identity;
	[SerializeField] Matrix4x4 eyeViewR = Matrix4x4.identity;

	BanterScene scene;

	Camera _srcCamera{
		get => sourceCamOverride ? sourceCamOverride: Camera.main;
	}

	void Start() {
		scene = BanterScene.Instance();
		InvokeRepeating("IsLookingAt", 0, 10f);
	}

	public void SetRenderTextureSize(int size){
		renderTargetSize = size;
		DestroyRenderTextures();
		CreateRenderTextures();
	}

	public void SetCameraClear(int clear){
		cameraClear = (CameraClearFlags)clear;
	}
	public void SetCameraColor(string color){
		if(renderCam) {
			renderCam.backgroundColor = ColorUtility.TryParseHtmlString(color, out Color c) ? c : Color.black;
		}
	}

	public void SetCullingLayer(int mask){
		cameraViewMask = 1 << mask;
	}

	public void AddCullingLayer(int mask){
		cameraViewMask |= 1 << mask;
	}
	
	void CreateRenderTextures() {
		renderTexL = new RenderTexture(renderTargetSize, renderTargetSize, 16);
		renderTexR = new RenderTexture(renderTargetSize, renderTargetSize, 16);
		renderTexL.Create();
		renderTexR.Create();
	}

	void OnEnable(){
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
		if (renderTexL){
			renderTexL.Release();
			renderTexL = null;
		}
		if (renderTexR){
			renderTexR.Release();
			renderTexR = null;
		}
	}

	void OnDisable(){
		DestroyRenderTextures();
		Destroy(renderCamObj);
		Destroy(eyeDebugObjL);
		Destroy(eyeDebugObjR);

	}

	private bool TryGetEye(out Vector3 position, out Quaternion rotation, XRNode eye)
	{
		InputFeatureUsage<Vector3> inputFeatureUsagePosition = CommonUsages.leftEyePosition;
		InputFeatureUsage<Quaternion> inputFeatureUsageRotation = CommonUsages.leftEyeRotation;

		if (eye == XRNode.RightEye) {
			inputFeatureUsagePosition = CommonUsages.rightEyePosition;
			inputFeatureUsageRotation = CommonUsages.rightEyeRotation;
		}
		
		InputDevice device = InputDevices.GetDeviceAtXRNode(XRNode.Head);
		if (device.isValid)
		{
			if (
				device.TryGetFeatureValue(inputFeatureUsagePosition, out position) && 
				device.TryGetFeatureValue(inputFeatureUsageRotation, out rotation))
					return true;
				
		}
		// This is the fail case
		position = Vector3.zero;
		rotation = Quaternion.identity;
		return false;
	}

	void IsLookingAt() {
		if(Vector3.Distance(transform.position, Camera.main.transform.position) < 3f && 
		Vector3.Angle(transform.position - Camera.main.transform.position, Camera.main.transform.forward) < 90f) {
			scene.LookedAtMirror();
		}
		// else{
		// 	Debug.Log("Not looking at mirror" + Vector3.Angle(transform.position - Camera.main.transform.position, Camera.main.transform.forward) + " " + Vector3.Distance(transform.position, Camera.main.transform.position));
		// }
	}

	void updateEyePos(){
		if( XRSettings.isDeviceActive) {
			TryGetEye(out deviceEyePoseL.position, out deviceEyePoseL.rotation, XRNode.LeftEye);
			TryGetEye(out deviceEyePoseR.position, out deviceEyePoseR.rotation, XRNode.RightEye);
		}else{
			deviceEyePoseL.position = deviceEyePoseR.position = Camera.main.transform.position;
			deviceEyePoseL.rotation = deviceEyePoseR.rotation = Camera.main.transform.rotation;
		}
		var cam = _srcCamera;
		var camParent = cam.transform.parent;
		if (!camParent || !XRSettings.isDeviceActive){
			worldEyePoseL = deviceEyePoseL;
			worldEyePoseR = deviceEyePoseR;
		}
		else{
			worldEyePoseL.position = camParent.TransformPoint(deviceEyePoseL.position);
			worldEyePoseL.rotation = camParent.rotation * deviceEyePoseL.rotation;
			worldEyePoseR.position = camParent.TransformPoint(deviceEyePoseR.position);
			worldEyePoseR.rotation = camParent.rotation * deviceEyePoseR.rotation;
		}
		eyeDebugObjL.transform.position = worldEyePoseL.position;
		eyeDebugObjL.transform.rotation = worldEyePoseL.rotation;
		eyeDebugObjR.transform.position = worldEyePoseR.position;
		eyeDebugObjR.transform.rotation = worldEyePoseR.rotation;
	}

	void renderToTexture(RenderTexture rt, Pose eyePose, out Matrix4x4 viewMat, out Matrix4x4 projMat){
		viewMat = Matrix4x4.identity;
		projMat = Matrix4x4.identity;

		var srcCam = _srcCamera;

		renderCam.enabled = true;
		renderCam.transform.position = eyePose.position;
		renderCam.transform.rotation = eyePose.rotation;

		renderCam.nearClipPlane = srcCam.nearClipPlane;
		renderCam.farClipPlane = srcCam.farClipPlane;
		renderCam.fieldOfView = cameraFov;
		renderCam.cullingMask = cameraViewMask;
		renderCam.clearFlags = cameraClear;

		viewMat = renderCam.worldToCameraMatrix;
		Vector3 mirrorPos = Vector3.zero, mirrorNormal = Vector3.up;
		bool useOblique = false;
		if (portalEye && !mirrorMode){
			Coord srcCoord = new(transform);
			Coord dstCoord = new(portalEye);

			var localEyePos = srcCoord.worldToLocalPos(eyePose.position);
			var srcEyeUp = eyePose.rotation * Vector3.up;
			var srcEyeForward = eyePose.rotation * Vector3.forward;
			var localEyeUp = srcCoord.worldToLocalDir(srcEyeUp);
			var localEyeForward = srcCoord.worldToLocalDir(srcEyeForward);

			var newEyePos = dstCoord.localToWorldPos(localEyePos);
			var newEyeUp = dstCoord.localToWorldDir(localEyeUp);
			var newEyeForward = dstCoord.localToWorldDir(localEyeForward);
			var newEyeRot = Quaternion.LookRotation(newEyeForward, newEyeUp);

			eyePose.position = newEyePos;
			eyePose.rotation = newEyeRot;
			renderCam.transform.position = eyePose.position;
			renderCam.transform.rotation = eyePose.rotation;
		}
		else if (mirrorMode){
			Coord srcCoord = new(transform);
			Coord dstCoord = srcCoord;
			
			var localEyePos = srcCoord.worldToLocalPos(eyePose.position);
			var localEyeUp = srcCoord.worldToLocalDir(eyePose.rotation * Vector3.up);
			var localEyeForward = srcCoord.worldToLocalDir(eyePose.rotation * Vector3.forward);

			var planeNormal = Vector3.up;
			localEyePos = Vector3.Reflect(localEyePos, planeNormal);
			localEyeUp = Vector3.Reflect(localEyeUp, planeNormal);
			localEyeForward = Vector3.Reflect(localEyeForward, planeNormal);

			var newEyePos = dstCoord.localToWorldPos(localEyePos);
			var newEyeUp = dstCoord.localToWorldDir(localEyeUp);
			var newEyeForward = dstCoord.localToWorldDir(localEyeForward);
			var newEyeRot = Quaternion.LookRotation(newEyeForward, newEyeUp);

			eyePose.position = newEyePos;
			eyePose.rotation = newEyeRot;
			renderCam.transform.position = eyePose.position;
			renderCam.transform.rotation = eyePose.rotation;

			mirrorPos = srcCoord.pos;
			mirrorNormal = srcCoord.y;
			useOblique = true;
		}

		projMat = renderCam.projectionMatrix;
		if (useOblique){
			var camMirrorPos = renderCam.worldToCameraMatrix.MultiplyPoint(mirrorPos);
			var camMirrorNormal = renderCam.worldToCameraMatrix.MultiplyVector(mirrorNormal);
			var camClipPlane = new Vector4(
				camMirrorNormal.x, camMirrorNormal.y, camMirrorNormal.z, 
				-Vector3.Dot(camMirrorNormal, camMirrorPos)
			);
			var mirrorProj = renderCam.CalculateObliqueMatrix(camClipPlane);
			renderCam.projectionMatrix = mirrorProj;
		}
		if (mirrorMode){
			projMat *= Matrix4x4.Scale(new Vector3(-1.0f, 1.0f, 1.0f));
		}

		renderCam.targetTexture = rt;

		renderCam.Render();

		renderCam.enabled = false;
	}

	void drawGizmos(Color c){

	}

	void OnDrawGizmosSelected(){
		drawGizmos(Color.white);
	}
	void OnDrawGizmos(){
		drawGizmos(Color.yellow);
	}

	void setShaderParams(){
		if (targetMaterial){
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

	void LateUpdate(){
		updateEyePos();
		renderToTexture(renderTexL, worldEyePoseL, out eyeViewL, out eyeProjL);
		renderToTexture(renderTexR, worldEyePoseR, out eyeViewR, out eyeProjR);
		setShaderParams();
	}
}
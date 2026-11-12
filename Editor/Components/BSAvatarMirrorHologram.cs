using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace BS.SDKEditor
{
    /// <summary>
    /// While a <see cref="BSAvatarMirror"/> is selected, the Seat's hologram robot stands on it in the Scene view (the same
    /// dummy and material as the Seat), at the height offset, so creators see where and how big the copy will be. Not
    /// part of the inspector, which the component generator writes like every other component's.
    /// </summary>
    [InitializeOnLoad]
    static class BSAvatarMirrorHologram
    {
        static SeatPosePreview preview;
        static SeatPose standing;
        static bool measured;

        static BSAvatarMirrorHologram()
        {
            RenderPipelineManager.beginCameraRendering += Draw;
            AssemblyReloadEvents.beforeAssemblyReload += Clear;
            EditorApplication.playModeStateChanged += _ => Clear();
        }

        static void Clear()
        {
            preview?.Dispose();
            preview = null;
            measured = false;
        }

        /// <summary>
        /// The robot standing with straight legs, its soles on the anchor: measured once from its own bones (hip drop,
        /// thigh and shin, the sole below the ankle) before anything poses it.
        /// </summary>
        static SeatPose Standing()
        {
            if (measured) return standing;
            measured = true;
            standing = SeatPose.Default;
            var robot = preview != null && preview.Instance != null ? preview.Instance.GetComponent<SeatPoseRobot>() : null;
            if (robot == null || !robot.IsValid) return standing;
            float thigh = Vector3.Distance(robot.leftUpperLeg.position, robot.leftLowerLeg.position);
            float shin = Vector3.Distance(robot.leftLowerLeg.position, robot.leftFoot.position);
            float hipDrop = robot.hips.position.y - robot.leftUpperLeg.position.y;
            float ankle = -(robot.leftFoot.rotation * robot.leftSoleOffset).y;
            float pelvis = hipDrop + 0.995f * (thigh + shin) + ankle;
            float lx = robot.leftUpperLeg.position.x - robot.hips.position.x;
            float rx = robot.rightUpperLeg.position.x - robot.hips.position.x;
            standing = new SeatPose
            {
                pelvisPosition = new Vector3(0f, pelvis, 0f), pelvisRotation = Quaternion.identity,
                leftFootPosition = new Vector3(lx, 0f, 0f), leftFootRotation = Quaternion.identity,
                rightFootPosition = new Vector3(rx, 0f, 0f), rightFootRotation = Quaternion.identity,
                leftKneeHint = new Vector3(lx, pelvis * 0.5f, 0.3f), rightKneeHint = new Vector3(rx, pelvis * 0.5f, 0.3f),
            };
            return standing;
        }

        static void Draw(ScriptableRenderContext context, Camera camera)
        {
            if (Application.isPlaying || camera.cameraType != CameraType.SceneView) return;
            var go = Selection.activeGameObject;
            var mirror = go != null ? go.GetComponent<BSAvatarMirror>() : null;
            if (mirror == null || !mirror.isActiveAndEnabled || EditorUtility.IsPersistent(mirror)
                || StageUtility.GetStageHandle(go) != StageUtility.GetCurrentStageHandle()
                || SceneVisibilityManager.instance.IsHidden(go)) return;
            SceneView view = null;
            foreach (SceneView candidate in SceneView.sceneViews) if (candidate.camera == camera) { view = candidate; break; }
            if (view == null || !view.drawGizmos) return;
            if (preview == null) preview = new SeatPosePreview();
            var pose = Standing();
            var lift = Vector3.up * mirror.HeightOffset;
            pose.pelvisPosition += lift;
            pose.leftFootPosition += lift;
            pose.rightFootPosition += lift;
            pose.leftKneeHint += lift;
            pose.rightKneeHint += lift;
            preview.Draw(mirror.transform, pose, true, camera, 0.8f);
        }
    }
}

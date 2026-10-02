// <mirror source="Assets/Systems/SceneEvents/BanterSceneEventHandler.cs" sha256="ee046e8061e439c34909a9015a187b387824455d5cdd6a88e9e1f07941660813" mode="port" />
// PublishLocalPose and PoseMoved (:560-598) with the serialized _posePublishHz (:42) at its 30 Hz default and the
// scene read through BSScene.Current.
using UnityEngine;

namespace BS.LocalMultiplayer.Avatars
{
    /// <summary>
    /// Publishes the local player's head and hands to the page as the <c>pose-update</c> event, the JS SDK's way to
    /// read local user transforms (<c>scene.On("pose-update", ...)</c>, detail = {head, leftHand, rightHand}). Throttled
    /// to 30 Hz and skipped while the player is perfectly still.
    /// </summary>
    public sealed class PagePoseEmitter
    {
        /// <summary>Rate (Hz) the pose goes to the page (BanterSceneEventHandler._posePublishHz).</summary>
        public const float PosePublishHz = 30f;

        // Pose publishing throttle + idle-skip state.
        float _poseTimer;
        Vector3 _lastHeadPos, _lastLeftPos, _lastRightPos;
        Quaternion _lastHeadRot, _lastLeftRot, _lastRightRot;

        /// <summary>Called every frame; sends when the throttle allows and something moved.</summary>
        public void Tick(BSScene scene, Transform head, Transform leftHand, Transform rightHand, float unscaledDeltaTime)
        {
            if (PosePublishHz <= 0f || head == null || leftHand == null || rightHand == null)
                return;
            if (scene == null) return;
            var link = scene.link;
            if (link == null) return;

            _poseTimer += unscaledDeltaTime;
            if (_poseTimer < 1f / PosePublishHz) return;
            _poseTimer = 0f;

            if (!PoseMoved(head, leftHand, rightHand)) return;
            link.OnPoseUpdate(head, leftHand, rightHand);
        }

        // True if head or either hand moved past a small threshold since the last publish; updates the
        // stored pose. Avoids spamming the string bus while the player is perfectly still.
        internal bool PoseMoved(Transform head, Transform leftHand, Transform rightHand)
        {
            const float posEps = 0.0005f; // ~0.5 mm
            const float rotEps = 0.05f;   // degrees
            bool moved =
                (head.position - _lastHeadPos).sqrMagnitude > posEps * posEps ||
                (leftHand.position - _lastLeftPos).sqrMagnitude > posEps * posEps ||
                (rightHand.position - _lastRightPos).sqrMagnitude > posEps * posEps ||
                Quaternion.Angle(head.rotation, _lastHeadRot) > rotEps ||
                Quaternion.Angle(leftHand.rotation, _lastLeftRot) > rotEps ||
                Quaternion.Angle(rightHand.rotation, _lastRightRot) > rotEps;
            if (!moved) return false;
            _lastHeadPos = head.position; _lastHeadRot = head.rotation;
            _lastLeftPos = leftHand.position; _lastLeftRot = leftHand.rotation;
            _lastRightPos = rightHand.position; _lastRightRot = rightHand.rotation;
            return true;
        }
    }
}

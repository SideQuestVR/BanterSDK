using UnityEngine;

namespace BS
{
    public partial class BSSeat
    {
        [Tooltip("Seconds the view takes to catch up with the seat's rotation (Follow Seat and Upright Follow Yaw), to soften jitter and sharp turns. The seat position is never smoothed. 0 follows exactly. Read when the player sits.")]
        [SerializeField, Range(0f, 1f)] float viewSmoothing;

        [SerializeField] SeatPose pose = SeatPose.Default;

        public float ViewSmoothing { get => Mathf.Max(0f, viewSmoothing); set => viewSmoothing = Mathf.Max(0f, value); }

        public SeatPose Pose { get => pose.Validated(); set => pose = value.Validated(); }
        public void ResetPose() => pose = SeatPose.Default;
    }
}

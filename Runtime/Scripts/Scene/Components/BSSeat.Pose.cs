using UnityEngine;

namespace BS
{
    public partial class BSSeat
    {
        [SerializeField] SeatPose pose = SeatPose.Default;

        public SeatPose Pose { get => pose.Validated(); set => pose = value.Validated(); }
        public void ResetPose() => pose = SeatPose.Default;
    }
}

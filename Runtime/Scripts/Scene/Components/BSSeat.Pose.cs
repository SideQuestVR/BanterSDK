using UnityEngine;

namespace BS
{
    public partial class BSSeat
    {
        [Tooltip("All modes use the authored pose and face the seat on entry. Legacy follows full seat rotation; upright modes keep seat tilt out of the view.")]
        [See(initial = "1")][SerializeField] internal SeatViewMode viewMode = SeatViewMode.UprightFollowYaw;

        [SerializeField] SeatPose pose = SeatPose.Default;

        public SeatPose Pose { get => pose.Validated(); set => pose = value.Validated(); }
        public void ResetPose() => pose = SeatPose.Default;
    }
}

// <mirror source="Packages/com.sidequest.flexabody/Runtime/Player/FlexaMover.cs" sha256="b0355de48c9217fd8f18a40cf5809c78a076c16aba8728456ca25a0ac1084a3c" mode="port" />
// Only the seated hip is production's: FlexaMover.AttachToSeat (:401) and the seated auto-crouch (:169-179) hold the hip
// at seat + seat rotation * (0, 0.2, 0) (_seatHipOffset, :49) for every jointed pilot, isSeat or not, and the seat joint
// (:420-435) locks all six degrees of freedom of the torso to the seat, so the seated hip turns with the seat and not
// with the player's look (desktop stays mouselook-only while seated, :271-274).
using UnityEngine;

namespace BS.LocalMultiplayer.Avatars
{
    /// <summary>
    /// Where the desktop player's hip is, which production reads off the FlexaBody torso: 0.65 m below the eye,
    /// so 0.95 m above the feet when standing at the 1.6 m eye height, and locked to the seat when seated.
    /// </summary>
    public static class DesktopHipModel
    {
        /// <summary>Eye to hip, standing.</summary>
        public const float HeadToHip = 0.65f;

        /// <summary>The desktop player's standing eye height (BSDesktopController._eyeHeight).</summary>
        public const float StandingEyeHeight = 1.6f;

        /// <summary>The hip above the seat point while seated (FlexaMover._seatHipOffset.y).</summary>
        public const float SeatHipHeight = 0.2f;

        /// <summary>The hip's height above the player root (the feet, or the seat point when seated).</summary>
        public static float HipLocalHeight(float headLocalHeight, bool seated)
            => seated ? SeatHipHeight : headLocalHeight - HeadToHip;

        /// <summary>
        /// The hip's world pose. Seated at <paramref name="seatPoint"/>, it is FlexaMover's seated hip: the seat hip
        /// offset in the seat's own frame (AttachToSeat :401, unscaled), turned with the seat, since the seat joint
        /// locks the torso to it (:420-435). The player root's facing plays no part, so looking around turns only the
        /// head. Standing, it is <see cref="HipLocalHeight"/> above <paramref name="root"/>, facing the root's way.
        /// </summary>
        /// <param name="root">The player root: the feet, turned to the player's facing.</param>
        /// <param name="headLocalHeight">The eye's height above the root.</param>
        /// <param name="seated">Whether the player is seated.</param>
        /// <param name="seatPoint">The seat the player sits at. Null standing; null while seated too in the moment
        /// between the seat being destroyed and the player standing up, when the hip stays
        /// <see cref="SeatHipHeight"/> above the root, which the seat last carried.</param>
        /// <param name="hipPosition">The hip's world position.</param>
        /// <param name="hipRotation">The hip's world rotation.</param>
        public static void WorldHip(Transform root, float headLocalHeight, bool seated, Transform seatPoint,
            out Vector3 hipPosition, out Quaternion hipRotation)
        {
            if (seated && seatPoint != null)
            {
                hipRotation = seatPoint.rotation;
                hipPosition = seatPoint.position + hipRotation * new Vector3(0f, SeatHipHeight, 0f);
                return;
            }
            hipPosition = root.TransformPoint(0f, HipLocalHeight(headLocalHeight, seated), 0f);
            hipRotation = root.rotation;
        }
    }
}

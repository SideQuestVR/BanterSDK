// <mirror source="Assets/Systems/Avatar/RemoteBonePoseApplier.cs" sha256="dd5734aee666b27342d9106b0ad5e85ca6d1160cfb8107bd3e16ecfbcff1fa08" mode="port" />
// The easing arithmetic of RemoteBonePoseApplier: _smoothing (:26), the interval clamp (:30-31), the bone blend
// (:172-184) and the seat-offset ease (:63, :204-206), pulled out so it can be tested on its own.
using UnityEngine;

namespace BS.LocalMultiplayer.Avatars
{
    /// <summary>
    /// How fast a remote avatar's bones close on the latest pose. The time constant follows the sender's frame
    /// interval (validForMs), so a sparse stream eases slowly and a dense one snappily: over one interval the pose
    /// closes about 1 - e^(-1/0.6) of the gap, whatever the frame rate.
    /// </summary>
    public static class BoneEasing
    {
        /// <summary>How much of the network update interval to ease over (RemoteBonePoseApplier._smoothing).</summary>
        public const float Smoothing = 0.6f;

        // Clamp the per-frame validity so a zero/garbage value can't spike the easing rate, and an
        // absurdly large one can't leave bones drifting for half a second.
        public const float MinIntervalMs = 20f;
        public const float MaxIntervalMs = 400f;

        /// <summary>Smoothing time (s) for the seated hip's seat-relative offset (RemoteBonePoseApplier._pilotOffsetSmoothing).</summary>
        public const float PilotOffsetSmoothing = 0.06f;

        /// <summary>The time constant, in seconds: clamp(validForMs, 20, 400) ms x 0.6.</summary>
        public static float Tau(float validForMs) => Mathf.Clamp(validForMs, MinIntervalMs, MaxIntervalMs) * 0.001f * Smoothing;

        /// <summary>This frame's blend toward the target: 1 on a snap, else 1 - e^(-dt/tau).</summary>
        public static float Blend(float deltaTime, float validForMs, bool snap)
        {
            if (snap) return 1f;
            // Exponential ease with a time-constant tied to the update interval: over one interval
            // the pose closes ~(1 - e^(-1/_smoothing)) of the gap, frame-rate independently.
            return 1f - Mathf.Exp(-deltaTime / Mathf.Max(Tau(validForMs), 1e-4f));
        }

        /// <summary>This frame's blend of the seated hip's seat-relative offset toward the broadcast one.</summary>
        public static float PilotBlend(float deltaTime)
            => PilotOffsetSmoothing > 0f ? 1f - Mathf.Exp(-deltaTime / PilotOffsetSmoothing) : 1f;
    }
}

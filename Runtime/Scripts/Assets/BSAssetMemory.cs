using System;

namespace BS
{
    /// <summary>The device's free memory. Behind an interface so the policy can be tested with fake readings.</summary>
    internal interface IBSFreeMemory
    {
        /// <summary>Free bytes, or a negative number when this platform can't say.</summary>
        long FreeBytes();
    }

    /// <summary>
    /// When to treat memory as low: a reading under <see cref="EnterLowBytes"/> or the system's low memory report, until
    /// a reading over <see cref="LeaveLowBytes"/>. The gap between the two stops it flapping around one number. With no
    /// readings, the report holds for <see cref="ReportHoldSeconds"/>.
    /// </summary>
    internal sealed class BSAssetMemoryPolicy
    {
        internal const long EnterLowBytes = 200L << 20;
        internal const long LeaveLowBytes = 300L << 20;
        internal const float ReadEverySeconds = 5f;
        internal const float ReportHoldSeconds = 30f;

        readonly IBSFreeMemory memory;
        readonly Func<float> clock;
        float nextRead = float.NegativeInfinity;
        float heldUntil = float.NegativeInfinity;

        internal BSAssetMemoryPolicy(IBSFreeMemory memory, Func<float> clock)
        {
            this.memory = memory;
            this.clock = clock;
        }

        internal bool IsLow { get; private set; }

        /// <summary>Takes a reading when one is due and says whether memory is low.</summary>
        internal bool Refresh()
        {
            var now = clock();
            if (now < nextRead) return IsLow;
            nextRead = now + ReadEverySeconds;

            var free = memory.FreeBytes();
            if (now < heldUntil) return IsLow;
            if (free < 0) IsLow = false; // only the report can say so, and its hold has ended
            else if (free < EnterLowBytes) IsLow = true;
            else if (free > LeaveLowBytes) IsLow = false;
            return IsLow;
        }

        /// <summary><c>Application.lowMemory</c>: low now, whatever the last reading said.</summary>
        internal void LowMemoryReported()
        {
            IsLow = true;
            heldUntil = clock() + ReportHoldSeconds;
        }
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    /// <summary>ActivityManager's availMem. A binder call, so the policy reads it every few seconds, not every frame.</summary>
    internal sealed class BSDeviceFreeMemory : IBSFreeMemory
    {
        UnityEngine.AndroidJavaObject activityManager;

        public long FreeBytes()
        {
            try
            {
                if (activityManager == null)
                {
                    using (var player = new UnityEngine.AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    using (var activity = player.GetStatic<UnityEngine.AndroidJavaObject>("currentActivity"))
                        activityManager = activity.Call<UnityEngine.AndroidJavaObject>("getSystemService", "activity");
                }
                using (var info = new UnityEngine.AndroidJavaObject("android.app.ActivityManager$MemoryInfo"))
                {
                    activityManager.Call("getMemoryInfo", info);
                    return info.Get<long>("availMem");
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("[BSAssetMemory] Couldn't read free memory: " + e.Message);
                return -1;
            }
        }
    }
#else
    /// <summary>No reading here: the system's low memory report is all the policy has.</summary>
    internal sealed class BSDeviceFreeMemory : IBSFreeMemory
    {
        public long FreeBytes() => -1;
    }
#endif
}

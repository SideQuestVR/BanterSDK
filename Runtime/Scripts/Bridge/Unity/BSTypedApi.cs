using System;

namespace BS
{
    /// <summary>
    /// How a client limits the typed page API (BS.Unity) from outside its build. Both are asked on every call. Left null:
    /// every tier, and every member the SDK ships.
    /// </summary>
    public static class BSTypedApi
    {
        /// <summary>The tiers worlds may use: 1 read, 2 write inside the world, 4 global. 0 turns the API off.</summary>
        public static Func<int> Caps;

        /// <summary>True for a member id (Bridge~/unity-api.policy.json) that is off, whatever its tier.</summary>
        public static Func<int, bool> MemberOff;
    }
}

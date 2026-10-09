using UnityEngine;

namespace BS
{
    /// <summary>
    /// <c>Object.FindObjectsByType</c> without a sort, on every Unity the SDK supports. Unity 6000.4 added the
    /// overloads that take no <c>FindObjectsSortMode</c> and deprecated the enum (it sorted by instance id,
    /// which EntityId replaced; 6.5 rejects it), and 6000.3 only has the overloads that take one.
    /// </summary>
    public static class FindObjects
    {
        /// <summary>Every loaded <typeparamref name="T"/>, in no particular order. Inactive ones only when asked.</summary>
        public static T[] All<T>(FindObjectsInactive inactive = FindObjectsInactive.Exclude) where T : Object
        {
#if UNITY_6000_4_OR_NEWER
            return Object.FindObjectsByType<T>(inactive);
#else
            return Object.FindObjectsByType<T>(inactive, FindObjectsSortMode.None);
#endif
        }
    }
}

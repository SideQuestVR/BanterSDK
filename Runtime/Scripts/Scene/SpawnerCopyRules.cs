using System;
using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    /// <summary>
    /// The rules every client applies to <see cref="BSSpawnerObject"/> copies, shared by Greenfield's
    /// SpawnerObjectNetwork and the SDK's local-multiplayer port so the two can't drift.
    ///
    /// A copy's id is never its spawner's: it is a local pending id (<see cref="NewPendingId"/>) on the client that
    /// grabbed it until the server mints the network id, and that network id everywhere after. Its children with a
    /// <see cref="BSObjectId"/> get "&lt;copy id&gt;/&lt;n&gt;" in hierarchy order, the same on every client.
    /// </summary>
    public static class SpawnerCopyRules
    {
        /// <summary>The synced-object type key spawner copies are created under.</summary>
        public const string TypeKey = "bs.spawned";

        /// <summary>Record metadata: the spawner's <see cref="BSObjectId"/>, which every client resolves to its own spawner.</summary>
        public const string SpawnerKey = "spawner";

        /// <summary>Record metadata: the creator's pending id, so its create broadcast finds the copy already in its hand.</summary>
        public const string NonceKey = "n";

        /// <summary>
        /// Minimum time between two spawns by one client, in seconds. A spawn and its hold cost about seven object ops
        /// (create, the grip and release commits, the unlock, an eviction), against the server's 20 a second per
        /// connection, which room-state writes share.
        /// </summary>
        public const float Cooldown = 1f;

        const string PendingPrefix = "spawn-pending:";

        public static string NewPendingId() => PendingPrefix + Guid.NewGuid().ToString("N");

        public static bool IsPendingId(string id) => id != null && id.StartsWith(PendingPrefix, StringComparison.Ordinal);

        /// <summary>
        /// Sets the ids of <paramref name="copy"/>: <paramref name="copyId"/> on its root <see cref="BSObjectId"/>,
        /// "copyId/1", "copyId/2"... on the others in hierarchy order. Also clears every javascript link id the copy
        /// carried over from its spawner, which would otherwise make the page see two objects as one.
        /// </summary>
        public static void AssignIds(GameObject copy, string copyId)
        {
            if (copy == null || string.IsNullOrEmpty(copyId))
                throw new ArgumentException("A copy and an id are required.");
            var root = copy.GetComponent<BSObjectId>();
            int index = 0;
            foreach (var objectId in copy.GetComponentsInChildren<BSObjectId>(true))
                objectId.Id = objectId == root ? copyId : copyId + "/" + ++index;
        }

        /// <summary>Clears the javascript link ids a copy inherited; the page links the copy afresh.</summary>
        public static void ClearJsIds(GameObject copy)
        {
            foreach (var objectId in copy.GetComponentsInChildren<BSObjectId>(true))
                objectId.jsId = null;
            foreach (var component in copy.GetComponentsInChildren<BSComponentBase>(true))
                component.jsId = null;
        }

        /// <summary>
        /// The collider on <paramref name="copyRoot"/> that matches <paramref name="source"/> under
        /// <paramref name="sourceRoot"/>: same child path, same position among that object's colliders. Null when the
        /// source isn't under the root or the copy's hierarchy differs.
        /// </summary>
        public static Collider Counterpart(Transform sourceRoot, Collider source, Transform copyRoot)
        {
            if (sourceRoot == null || source == null || copyRoot == null)
                return null;
            var path = new List<int>();
            for (var t = source.transform; t != sourceRoot; t = t.parent)
            {
                if (t == null)
                    return null;
                path.Add(t.GetSiblingIndex());
            }
            var target = copyRoot;
            for (int i = path.Count - 1; i >= 0; i--)
            {
                if (path[i] >= target.childCount)
                    return null;
                target = target.GetChild(path[i]);
            }
            int colliderIndex = Array.IndexOf(source.GetComponents<Collider>(), source);
            var colliders = target.GetComponents<Collider>();
            return colliderIndex >= 0 && colliderIndex < colliders.Length ? colliders[colliderIndex] : null;
        }

        /// <summary>
        /// Stops the copy's colliders and its spawner's from pushing each other apart: a copy is born inside its
        /// spawner. Call once both are active (Unity forgets an ignore set on an inactive collider).
        /// </summary>
        public static void IgnoreSpawnerCollisions(GameObject copy, BSSpawnerObject spawner)
        {
            if (copy == null || spawner == null)
                return;
            var spawnerColliders = spawner.GetComponentsInChildren<Collider>(true);
            foreach (var mine in copy.GetComponentsInChildren<Collider>(true))
                foreach (var theirs in spawnerColliders)
                    Physics.IgnoreCollision(mine, theirs, true);
        }

        /// <summary>What the cap needs to know about one live copy.</summary>
        public struct CopyInfo
        {
            public string ObjectId;
            public long CreatedAtEpochMs;
            public bool Locked;
        }

        /// <summary>
        /// The copies to remove so a new one fits under <paramref name="maxSpawned"/>: the oldest ones nobody holds
        /// (locked), as many as it takes to get back below the cap (more than one only after players overshot it by
        /// spawning at the same moment; the cap is soft). Empty when there is still room; null when the cap is reached
        /// and every copy is held, so nothing should spawn.
        /// </summary>
        public static List<string> PickCapVictims(IReadOnlyList<CopyInfo> copies, int maxSpawned)
        {
            int excess = (copies?.Count ?? 0) - Math.Max(1, maxSpawned) + 1;
            var victims = new List<string>();
            if (excess <= 0)
                return victims;
            var free = new List<CopyInfo>();
            foreach (var copy in copies)
                if (!copy.Locked && !string.IsNullOrEmpty(copy.ObjectId))
                    free.Add(copy);
            if (free.Count == 0)
                return null;
            free.Sort((a, b) => a.CreatedAtEpochMs != b.CreatedAtEpochMs
                ? a.CreatedAtEpochMs.CompareTo(b.CreatedAtEpochMs)
                : string.CompareOrdinal(a.ObjectId, b.ObjectId));
            for (int i = 0; i < excess && i < free.Count; i++)
                victims.Add(free[i].ObjectId);
            return victims;
        }
    }
}

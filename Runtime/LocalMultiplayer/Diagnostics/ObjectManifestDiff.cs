using System;
using System.Collections.Generic;

namespace BS.LocalMultiplayer.Objects
{
    /// <summary>
    /// The pure half of <see cref="ObjectIdDiagnostics"/>: building a player's object manifest (RELAY.md 6.5) and
    /// comparing two of them. Ids are compared ordinally, as the relay and BSScene.GetObjectByBid do.
    /// </summary>
    public static class ObjectManifestDiff
    {
        /// <summary>
        /// A manifest from the Ids of the objects present when the host started (<paramref name="authoredIds"/>) and
        /// the ones created since (<paramref name="runtimeIds"/>): each list sorted and without duplicates or empty Ids.
        /// An Id in both lists counts as authored.
        /// </summary>
        public static ObjectManifest Create(IEnumerable<string> authoredIds, IEnumerable<string> runtimeIds)
        {
            var authored = Normalize(authoredIds, null);
            var runtime = Normalize(runtimeIds, new HashSet<string>(authored, StringComparer.Ordinal));
            var manifest = new ObjectManifest();
            manifest.Ids.AddRange(authored);
            manifest.RuntimeIds.AddRange(runtime);
            return manifest;
        }

        static List<string> Normalize(IEnumerable<string> ids, HashSet<string> exclude)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (ids != null)
            {
                foreach (var id in ids)
                {
                    if (string.IsNullOrEmpty(id)) continue;
                    if (exclude != null && exclude.Contains(id)) continue;
                    set.Add(id);
                }
            }
            var list = new List<string>(set);
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        /// <summary>Every Id the manifest names, authored or runtime.</summary>
        public static HashSet<string> AllIds(ObjectManifest manifest)
        {
            var all = new HashSet<string>(StringComparer.Ordinal);
            if (manifest == null) return all;
            if (manifest.Ids != null) all.UnionWith(manifest.Ids);
            if (manifest.RuntimeIds != null) all.UnionWith(manifest.RuntimeIds);
            all.Remove(null);
            return all;
        }

        /// <summary>The Ids of <paramref name="ids"/> that <paramref name="other"/> has in neither of its lists, in order.</summary>
        public static List<string> MissingFrom(IReadOnlyList<string> ids, ObjectManifest other)
        {
            var result = new List<string>();
            if (ids == null) return result;
            var theirs = AllIds(other);
            for (int i = 0; i < ids.Count; i++)
            {
                var id = ids[i];
                if (!string.IsNullOrEmpty(id) && !theirs.Contains(id)) result.Add(id);
            }
            return result;
        }

        /// <summary>Same Ids in the same lists (both are sorted by <see cref="Create"/>).</summary>
        public static bool SameAs(ObjectManifest a, ObjectManifest b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            return SameList(a.Ids, b.Ids) && SameList(a.RuntimeIds, b.RuntimeIds);
        }

        static bool SameList(List<string> a, List<string> b)
        {
            int countA = a != null ? a.Count : 0;
            int countB = b != null ? b.Count : 0;
            if (countA != countB) return false;
            for (int i = 0; i < countA; i++)
            {
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return false;
            }
            return true;
        }
    }
}

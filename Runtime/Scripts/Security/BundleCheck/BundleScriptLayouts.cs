using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace BS.Security
{
    /// <summary>
    /// The app's own serialized layouts for the script types content can name, so the checker reads what Unity reads.
    /// <para>
    /// Unity loads a script object, and the [SerializeReference] values in it, with the app's compiled layout and skips
    /// the stored type tree when the type entry's stored hash equals the app's properties hash for that script
    /// (StreamedBinaryRead; the r5 crash in BundleCheckUnityBehaviourTests U10 came from that path). Otherwise it converts
    /// by name with the stored tree (SafeBinaryRead), which is what the checker reads. A forged tree with a copied hash
    /// would show the checker one layout while Unity reads another.
    /// </para>
    /// <para>
    /// The properties hash cannot be computed outside the editor: it is the MonoScript's m_PropertiesHash (a script
    /// type entry stores exactly that), which the editor derives from the player assemblies, and no MD4/MD5 formula
    /// over the stored tree reproduces it (1,536 variants tried on live bundles; native classes do hash with MD4). So
    /// the editor writes this table at build time (<c>BundleScriptLayoutsGenerator</c>): for every content script type,
    /// and every class content can hold in a [SerializeReference] field, the hash Unity writes for it and a digest of
    /// the tree Unity writes with it. When a script's stored hash would make Unity read with the app's layout, its
    /// stored tree must be the app's own, and so must the trees of the [SerializeReference] values in it (Unity writes
    /// ref types with a zero hash, so the host's hash decides); BundleChecker refuses either as TypeTreeShape. The live
    /// corpus never stores one (script, hash) with two trees, so a current table refuses no genuine bundle. With no
    /// table installed the stored trees are taken as they are: the residual this closes.
    /// </para>
    /// </summary>
    internal static class BundleScriptLayouts
    {
        internal const int FormatVersion = 1;

        /// <summary>Resources path of the generated table (Assets/Resources/Generated/BundleScriptLayouts.txt).</summary>
        internal const string ResourcePath = "Generated/BundleScriptLayouts";

        const string Header = "BundleScriptLayouts";
        const int MaxEntries = 100000;

        internal sealed class Entry
        {
            internal readonly byte[] Hash, Digest;

            internal Entry(byte[] hash, byte[] digest)
            {
                Hash = hash;
                Digest = digest;
            }
        }

        internal sealed class Table
        {
            /// <summary>Script types by <see cref="KeyOf"/> (or <see cref="BuiltinKey"/> for the engine's own scripts).</summary>
            internal readonly Dictionary<string, Entry> Scripts = new Dictionary<string, Entry>(StringComparer.Ordinal);
            /// <summary>[SerializeReference] classes by <see cref="KeyOf"/>.</summary>
            internal readonly Dictionary<string, Entry> References = new Dictionary<string, Entry>(StringComparer.Ordinal);
            internal string UnityVersion = "", Target = "";
            /// <summary>The generator's fingerprint of what the table was built from (to tell a stale table).</summary>
            internal string Inputs = "";
            /// <summary>8 hex digits of the table's content: part of the checker's Versions, so cached verdicts follow the table.</summary>
            internal string Fingerprint = "";
        }

        /// <summary>One script type entry, ref type or native type entry as a bundle stores it (the generator's input).</summary>
        internal struct Observed
        {
            /// <summary>'S' script type, 'R' [SerializeReference] class, 'N' native class (key "class {id}").</summary>
            internal char Kind;
            internal string Key;
            internal byte[] Hash, Digest;
        }

        /// <summary>One type tree node, as far as the checker's reader depends on it.</summary>
        internal struct LayoutNode
        {
            internal int Level, TypeFlags, Version, Size;
            internal bool Align;
            internal string Type, Name;
        }

        static readonly object gate = new object();
        static Table current;
        static bool installed;

        /// <summary>The installed table, or null (none generated: stored trees are taken as they are).</summary>
        internal static Table Current
        {
            get
            {
                lock (gate) return current;
            }
        }

        /// <summary>Installs a table (null removes it); a later <see cref="LoadGenerated"/> keeps it.</summary>
        internal static void Install(Table table)
        {
            lock (gate)
            {
                current = table;
                installed = true;
            }
        }

        /// <summary>Loads the generated table from Resources before the first scene (players and Play Mode).</summary>
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void LoadGenerated()
        {
            lock (gate)
            {
                if (installed) return;
            }
            var asset = UnityEngine.Resources.Load<UnityEngine.TextAsset>(ResourcePath);
            if (asset == null)
            {
#if !UNITY_EDITOR
                UnityEngine.Debug.LogWarning("[BundleCheck] no script layout table in Resources/" + ResourcePath
                                             + ": stored type trees of script types are taken as they are");
#endif
                return;
            }
            var table = Parse(asset.text, out var problem);
            UnityEngine.Resources.UnloadAsset(asset);
            if (table == null)
            {
                UnityEngine.Debug.LogError("[BundleCheck] script layout table not used: " + problem);
                return;
            }
            if (table.UnityVersion != UnityEngine.Application.unityVersion)
            {
                UnityEngine.Debug.LogWarning("[BundleCheck] script layout table was generated by Unity " + table.UnityVersion + ", not "
                                             + UnityEngine.Application.unityVersion + ": not used");
                return;
            }
            lock (gate)
            {
                if (installed) return;
                current = table;
                installed = true;
            }
        }

        // ------------------------------------------------------------------ keys and comparisons

        /// <summary>An app type's key: its assembly's simple name and full name ("Unity.Splines|UnityEngine.Splines.SplineExtrude").</summary>
        internal static string KeyOf(Type type) => (type.Assembly.GetName().Name ?? "") + "|" + (type.FullName ?? type.Name);

        /// <summary>An engine script's key: the built-in file and path id its type entry names.</summary>
        internal static string BuiltinKey(string file, long pathId) => "builtin|" + file + "|" + pathId.ToString(CultureInfo.InvariantCulture);

        internal static bool Same(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            var difference = 0;
            for (var i = 0; i < a.Length; i++) difference |= a[i] ^ b[i];
            return difference == 0;
        }

        /// <summary>A stored hash of all zeroes (or none): never written for a real script; treated as matching the app's.</summary>
        internal static bool IsZero(byte[] hash)
        {
            if (hash == null) return true;
            foreach (var b in hash)
            {
                if (b != 0) return false;
            }
            return true;
        }

        /// <summary>
        /// The checker's own digest of a tree (SHA-256, first 16 bytes) over what its reader depends on: every node's level,
        /// array flag, version, alignment, size, type and name, in order.
        /// </summary>
        internal static byte[] Digest(IEnumerable<LayoutNode> nodes)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, new UTF8Encoding(false)))
            {
                foreach (var node in nodes)
                {
                    writer.Write((byte)node.Level);
                    writer.Write((byte)node.TypeFlags);
                    writer.Write((ushort)node.Version);
                    writer.Write(node.Align);
                    writer.Write(node.Size);
                    var type = Encoding.UTF8.GetBytes(node.Type ?? "");
                    writer.Write(type.Length);
                    writer.Write(type);
                    var name = Encoding.UTF8.GetBytes(node.Name ?? "");
                    writer.Write(name.Length);
                    writer.Write(name);
                }
                writer.Flush();
                using (var sha = SHA256.Create())
                {
                    var full = sha.ComputeHash(stream.GetBuffer(), 0, (int)stream.Length);
                    var digest = new byte[16];
                    Array.Copy(full, digest, 16);
                    return digest;
                }
            }
        }

        // ------------------------------------------------------------------ building, writing and reading the table

        /// <summary>
        /// A table from the type entries a probe bundle stores. A key seen with two different hashes or trees is dropped
        /// and named in <paramref name="problems"/>: the checker then takes that type's stored trees as they are.
        /// </summary>
        internal static Table Build(IEnumerable<Observed> observed, string unityVersion, string target, string inputs, List<string> problems)
        {
            var table = new Table { UnityVersion = unityVersion ?? "", Target = target ?? "", Inputs = inputs ?? "" };
            var conflicted = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in observed)
            {
                if (item.Kind != 'S' && item.Kind != 'R') continue;
                if (string.IsNullOrEmpty(item.Key) || item.Key.IndexOfAny(new[] { '\n', '\r' }) >= 0) continue;
                var map = item.Kind == 'S' ? table.Scripts : table.References;
                var id = item.Kind + item.Key;
                if (conflicted.Contains(id)) continue;
                if (map.TryGetValue(item.Key, out var existing))
                {
                    if (Same(existing.Hash, item.Hash) && Same(existing.Digest, item.Digest)) continue;
                    map.Remove(item.Key);
                    conflicted.Add(id);
                    problems?.Add((item.Kind == 'S' ? "script " : "reference class ") + item.Key + " stored with two different layouts: left out");
                    continue;
                }
                map[item.Key] = new Entry(item.Hash, item.Digest);
            }
            table.Fingerprint = FingerprintOf(Format(table));
            return table;
        }

        /// <summary>The table as text: a header, then one "S|R hash digest key" line per entry, sorted.</summary>
        internal static string Format(Table table)
        {
            var text = new StringBuilder();
            text.Append(Header).Append(' ').Append(FormatVersion.ToString(CultureInfo.InvariantCulture)).Append('\n');
            text.Append("unity ").Append(table.UnityVersion).Append('\n');
            text.Append("target ").Append(table.Target).Append('\n');
            text.Append("inputs ").Append(table.Inputs).Append('\n');
            void Lines(char kind, Dictionary<string, Entry> map)
            {
                var keys = new List<string>(map.Keys);
                keys.Sort(StringComparer.Ordinal);
                foreach (var key in keys)
                {
                    var entry = map[key];
                    text.Append(kind).Append(' ').Append(Hex(entry.Hash)).Append(' ').Append(Hex(entry.Digest)).Append(' ').Append(key).Append('\n');
                }
            }
            Lines('S', table.Scripts);
            Lines('R', table.References);
            return text.ToString();
        }

        /// <summary>Reads a table written by <see cref="Format"/>; null with <paramref name="problem"/> set when it is not one.</summary>
        internal static Table Parse(string text, out string problem)
        {
            problem = null;
            if (text == null)
            {
                problem = "no text";
                return null;
            }
            var table = new Table();
            var lines = text.Replace("\r\n", "\n").Split('\n');
            if (lines.Length < 4 || lines[0] != Header + " " + FormatVersion.ToString(CultureInfo.InvariantCulture))
            {
                problem = "not a " + Header + " " + FormatVersion + " table";
                return null;
            }
            if (!lines[1].StartsWith("unity ", StringComparison.Ordinal) || !lines[2].StartsWith("target ", StringComparison.Ordinal)
                || !lines[3].StartsWith("inputs ", StringComparison.Ordinal))
            {
                problem = "header lines missing";
                return null;
            }
            table.UnityVersion = lines[1].Substring(6);
            table.Target = lines[2].Substring(7);
            table.Inputs = lines[3].Substring(7);
            for (var i = 4; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.Length == 0) continue;
                if (table.Scripts.Count + table.References.Count >= MaxEntries)
                {
                    problem = "more than " + MaxEntries + " entries";
                    return null;
                }
                // "S <32 hex> <32 hex> <key>"
                if (line.Length < 69 || (line[0] != 'S' && line[0] != 'R') || line[1] != ' ' || line[34] != ' ' || line[67] != ' '
                    || !TryHex(line.Substring(2, 32), out var hash) || !TryHex(line.Substring(35, 32), out var digest))
                {
                    problem = "line " + (i + 1) + " is not an entry";
                    return null;
                }
                var key = line.Substring(68);
                var map = line[0] == 'S' ? table.Scripts : table.References;
                if (map.ContainsKey(key))
                {
                    problem = "line " + (i + 1) + " repeats a key";
                    return null;
                }
                map[key] = new Entry(hash, digest);
            }
            table.Fingerprint = FingerprintOf(Format(table));
            return table;
        }

        static string FingerprintOf(string text)
        {
            using (var sha = SHA256.Create())
            {
                return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Substring(0, 8);
            }
        }

        internal static string Hex(byte[] bytes)
        {
            var text = new StringBuilder((bytes?.Length ?? 0) * 2);
            if (bytes != null)
            {
                foreach (var b in bytes) text.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            }
            return text.ToString();
        }

        static bool TryHex(string text, out byte[] bytes)
        {
            bytes = new byte[text.Length / 2];
            if (text.Length % 2 != 0) return false;
            for (var i = 0; i < bytes.Length; i++)
            {
                if (!byte.TryParse(text.Substring(i * 2, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out bytes[i])) return false;
            }
            return true;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BS.Security
{
    /// <summary>
    /// Pass/Refuse verdicts kept across sessions, keyed by the SHA-256 of the decrypted bundle bytes plus the checker,
    /// list and environment versions, so a bundle is only judged again when its bytes or the rules change. Bounded
    /// (least recently used entries go first), thread-safe, and a missing or corrupt file simply reads as empty.
    /// </summary>
    /// <remarks>
    /// The file is JSON with one entry per line; every value it holds is either hex, a version string or a cleaned
    /// reason (letters, digits and a few punctuation marks), so nothing in it needs escaping and anything else is corrupt.
    /// </remarks>
    internal sealed class BundleVerdictCache
    {
        internal const int DefaultCapacity = 512;
        const int Format = 1;

        sealed class Entry
        {
            internal string Key;
            internal bool Passed;
            internal string Reason;
            internal long Used;
        }

        readonly string path;
        readonly int capacity;
        readonly object gate = new object();
        Dictionary<string, Entry> entries;
        long clock;

        internal BundleVerdictCache(string path, int capacity = DefaultCapacity)
        {
            this.path = path;
            this.capacity = Math.Max(1, capacity);
        }

        internal string Path => path;

        internal static string Key(byte[] sha256, string versions)
        {
            var text = new StringBuilder(sha256.Length * 2 + 1 + (versions?.Length ?? 0));
            foreach (var b in sha256) text.Append(b.ToString("x2"));
            return text.Append('|').Append(versions ?? "").ToString();
        }

        internal int Count
        {
            get
            {
                lock (gate)
                {
                    Load();
                    return entries.Count;
                }
            }
        }

        internal bool TryGet(string key, out bool passed, out string reason)
        {
            lock (gate)
            {
                Load();
                if (entries.TryGetValue(key, out var entry))
                {
                    entry.Used = ++clock;
                    passed = entry.Passed;
                    reason = entry.Reason;
                    return true;
                }
            }
            passed = false;
            reason = null;
            return false;
        }

        internal void Put(string key, bool passed, string reason)
        {
            if (!IsSafe(key)) return;
            reason = Reason(reason);
            lock (gate)
            {
                Load();
                entries[key] = new Entry { Key = key, Passed = passed, Reason = reason, Used = ++clock };
                while (entries.Count > capacity)
                {
                    Entry oldest = null;
                    foreach (var entry in entries.Values)
                    {
                        if (oldest == null || entry.Used < oldest.Used) oldest = entry;
                    }
                    entries.Remove(oldest.Key);
                }
                Save();
            }
        }

        /// <summary>Drops every verdict (and the file).</summary>
        internal void Clear()
        {
            lock (gate)
            {
                entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                catch (Exception)
                {
                    // nothing to keep: the next save rewrites it
                }
            }
        }

        /// <summary>One line of plain text: no quotes, backslashes or control characters (so the file needs no escaping), capped.</summary>
        static string Reason(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var clean = new StringBuilder(Math.Min(text.Length, 200));
            foreach (var ch in text)
            {
                if (clean.Length >= 200) break;
                clean.Append(char.IsControl(ch) || ch == '"' || ch == '\\' ? '?' : ch);
            }
            return clean.ToString();
        }

        static bool IsSafe(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length > 512) return false;
            foreach (var ch in text)
            {
                if (!(char.IsLetterOrDigit(ch) || "|/._+-".IndexOf(ch) >= 0)) return false;
            }
            return true;
        }

        void Load()
        {
            if (entries != null) return;
            entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
            try
            {
                if (!File.Exists(path)) return;
                var lines = File.ReadAllLines(path, Encoding.UTF8);
                if (lines.Length == 0 || lines[0].Trim() != "{\"format\":" + Format + ",\"entries\":[") return;
                var parsed = new List<Entry>();
                for (var i = 1; i < lines.Length; i++)
                {
                    var line = lines[i].Trim();
                    if (line == "]}") break;
                    var entry = Parse(line.TrimEnd(','));
                    if (entry == null) return; // corrupt: start empty
                    parsed.Add(entry);
                }
                parsed.Sort((a, b) => a.Used.CompareTo(b.Used));
                foreach (var entry in parsed)
                {
                    entry.Used = ++clock;
                    entries[entry.Key] = entry;
                }
            }
            catch (Exception)
            {
                entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
            }
        }

        static Entry Parse(string line)
        {
            // {"k":"...","v":"P","r":"...","t":123}
            if (!line.StartsWith("{\"k\":\"", StringComparison.Ordinal) || !line.EndsWith("}", StringComparison.Ordinal)) return null;
            var key = Field(line, "\"k\":\"");
            var verdict = Field(line, "\"v\":\"");
            var reason = Field(line, "\"r\":\"");
            var usedAt = line.IndexOf("\"t\":", StringComparison.Ordinal);
            if (key == null || verdict == null || reason == null || usedAt < 0 || !IsSafe(key)) return null;
            if (verdict != "P" && verdict != "R") return null;
            var number = line.Substring(usedAt + 4).TrimEnd('}');
            if (!long.TryParse(number, out var used)) return null;
            if (reason.Length > 400) return null;
            foreach (var ch in reason)
            {
                if (char.IsControl(ch) || ch == '\\') return null;
            }
            return new Entry { Key = key, Passed = verdict == "P", Reason = reason, Used = used };
        }

        static string Field(string line, string prefix)
        {
            var start = line.IndexOf(prefix, StringComparison.Ordinal);
            if (start < 0) return null;
            start += prefix.Length;
            var end = line.IndexOf('"', start);
            return end < 0 ? null : line.Substring(start, end - start);
        }

        void Save()
        {
            try
            {
                var directory = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                var ordered = new List<Entry>(entries.Values);
                ordered.Sort((a, b) => a.Used.CompareTo(b.Used));
                var text = new StringBuilder();
                text.Append("{\"format\":").Append(Format).Append(",\"entries\":[\n");
                for (var i = 0; i < ordered.Count; i++)
                {
                    var entry = ordered[i];
                    text.Append("{\"k\":\"").Append(entry.Key).Append("\",\"v\":\"").Append(entry.Passed ? "P" : "R")
                        .Append("\",\"r\":\"").Append(entry.Reason.Replace("\"", "?").Replace("\\", "?")).Append("\",\"t\":").Append(entry.Used).Append('}')
                        .Append(i < ordered.Count - 1 ? ",\n" : "\n");
                }
                text.Append("]}\n");
                var temporary = path + ".tmp";
                File.WriteAllText(temporary, text.ToString(), new UTF8Encoding(false));
                if (File.Exists(path)) File.Delete(path);
                File.Move(temporary, path);
            }
            catch (Exception)
            {
                // A cache that cannot be written only costs a re-check next time.
            }
        }
    }
}

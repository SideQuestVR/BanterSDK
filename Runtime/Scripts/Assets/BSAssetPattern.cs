using System.Collections.Generic;

namespace BS
{
    /// <summary>
    /// What <c>BS.Assets.Find</c> searches for: a key with * for any run of characters, ignoring case. Prepared once per
    /// search, so each key then costs one pass over it, however the pattern is built: the parts between the stars are
    /// looked for in order, and a search never goes back over what it has read.
    /// </summary>
    internal sealed class BSAssetPattern
    {
        internal const int MaxLength = 256;

        readonly bool exact;
        readonly string head, tail;
        readonly List<string> middles = new List<string>();
        // For each middle, how much of it still matches after a mismatch at each position (Knuth-Morris-Pratt).
        readonly List<int[]> fallbacks = new List<int[]>();
        readonly int shortest;

        internal BSAssetPattern(string pattern)
        {
            if (pattern.Length > MaxLength) throw new BSAssetLimitException($"LIMIT: a pattern has at most {MaxLength} characters");
            var parts = pattern.ToLowerInvariant().Split('*');
            exact = parts.Length == 1;
            head = parts[0];
            tail = exact ? "" : parts[parts.Length - 1];
            shortest = head.Length + tail.Length;
            for (var i = 1; i < parts.Length - 1; i++)
            {
                if (parts[i].Length == 0) continue; // a run of stars is one star
                middles.Add(parts[i]);
                fallbacks.Add(FallbackOf(parts[i]));
                shortest += parts[i].Length;
            }
        }

        static int[] FallbackOf(string word)
        {
            var fallback = new int[word.Length];
            for (int i = 1, matched = 0; i < word.Length; i++)
            {
                while (matched > 0 && word[i] != word[matched]) matched = fallback[matched - 1];
                if (word[i] == word[matched]) matched++;
                fallback[i] = matched;
            }
            return fallback;
        }

        internal bool Matches(string text)
        {
            if (exact) return text.Length == head.Length && IsAt(text, 0, head);
            if (text.Length < shortest || !IsAt(text, 0, head) || !IsAt(text, text.Length - tail.Length, tail)) return false;
            var from = head.Length;
            var end = text.Length - tail.Length;
            for (var i = 0; i < middles.Count; i++)
            {
                from = After(middles[i], fallbacks[i], text, from, end);
                if (from < 0) return false;
            }
            return true;
        }

        static bool IsAt(string text, int start, string part)
        {
            for (var i = 0; i < part.Length; i++)
            {
                if (char.ToLowerInvariant(text[start + i]) != part[i]) return false;
            }
            return true;
        }

        /// <summary>Where the first <paramref name="word"/> at or after <paramref name="from"/> ends; -1 when there is none.</summary>
        static int After(string word, int[] fallback, string text, int from, int end)
        {
            var matched = 0;
            for (var i = from; i < end; i++)
            {
                var c = char.ToLowerInvariant(text[i]);
                while (matched > 0 && word[matched] != c) matched = fallback[matched - 1];
                if (word[matched] == c) matched++;
                if (matched == word.Length) return i + 1;
            }
            return -1;
        }
    }
}

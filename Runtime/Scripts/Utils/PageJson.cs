namespace BS
{
    /// <summary>
    /// A check to run before JsonUtility parses text that a page or another client wrote. JsonUtility's parser recurses:
    /// nesting deep enough overflows the native stack, which no catch can stop (in the Editor at depth 100,000; on IL2CPP
    /// probably a crash of the process). Everything the SDK reads from a page nests a handful of levels at most.
    /// Same rule as Ora's OraPageJson.IsSafe, without its size limit: geometry can be large, and size is not the danger here.
    /// </summary>
    public static class PageJson
    {
        /// <summary>Open brackets deep. The SDK's own shapes need 4 (a curve's segments' vectors).</summary>
        public const int MaxDepth = 64;

        /// <summary>True when <paramref name="json"/> may be parsed. One pass, no recursion, and it never throws.</summary>
        public static bool IsSafe(string json)
        {
            if (json == null) return false;
            int depth = 0;
            bool inString = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (inString)
                {
                    if (c == '\\') i++;
                    else if (c == '"') inString = false;
                }
                else if (c == '"') inString = true;
                else if (c == '/') return false; // JSON has no comments, and one could hide a quote from this count
                else if (c == '{' || c == '[')
                {
                    if (++depth > MaxDepth) return false;
                }
                else if ((c == '}' || c == ']') && depth > 0) depth--;
            }
            return true;
        }
    }
}

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// Every "&lt;mirror source=… sha256=… mode=…/&gt;" header records the source a local multiplayer file was reviewed
    /// against. A verbatim copy has to match: a drifted wire codec talks a different protocol. A port is a rewrite, so its
    /// drift is logged for review: re-port, or confirm the change doesn't apply, then update the sha256. Ignored outside the
    /// Greenfield repo, where the sources don't exist.
    /// </summary>
    public class MirrorDriftTests
    {
        static readonly Regex Header = new Regex(
            "<mirror source=\"(?<source>[^\"]+)\" sha256=\"(?<sha>[0-9a-f]{64})\" mode=\"(?<mode>port|verbatim)\"\\s*/>");
        const int HeaderLines = 40;

        [Test]
        public void MirrorHeadersMatchTheirSources()
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.sidequest.creator-sdk/package.json");
            Assert.That(package, Is.Not.Null, "creator SDK package not found");
            string sdkRoot = package.resolvedPath, repoRoot = Path.GetDirectoryName(Application.dataPath);
            int compared = 0, unavailable = 0;
            var verbatim = new List<string>();
            var ports = new List<string>();
            foreach (var file in Directory.EnumerateFiles(sdkRoot, "*.cs", SearchOption.AllDirectories))
            {
                string relative = file.Substring(sdkRoot.Length + 1).Replace('\\', '/');
                if (relative.Split('/').Any(part => part.EndsWith("~"))) continue;
                foreach (var line in File.ReadLines(file).Take(HeaderLines))
                {
                    var m = Header.Match(line);
                    if (!m.Success) continue;
                    string source = Path.Combine(repoRoot, m.Groups["source"].Value);
                    if (!File.Exists(source)) { unavailable++; continue; }
                    compared++;
                    byte[] bytes = File.ReadAllBytes(source);
                    string want = m.Groups["sha"].Value;
                    if (Sha256(bytes) == want || Sha256(CrlfToLf(bytes)) == want) continue;
                    (m.Groups["mode"].Value == "verbatim" ? verbatim : ports).Add($"{relative} <- {m.Groups["source"].Value}");
                }
            }

            if (compared == 0)
                Assert.Ignore($"None of the {unavailable} mirror sources exist here (an SDK checkout outside Greenfield).");
            Assert.That(unavailable, Is.LessThan((compared + unavailable) / 5),
                $"{unavailable} mirror sources are missing: a mass rename would hide drift, so update the headers.");
            if (ports.Count > 0)
                Debug.LogWarning($"{ports.Count} of {compared} ported sources changed since their last review:\n" + string.Join("\n", ports));
            Assert.IsEmpty(verbatim, "verbatim copies changed at their source:\n" + string.Join("\n", verbatim));
        }

        static string Sha256(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2")));
        }

        static byte[] CrlfToLf(byte[] bytes)
        {
            var output = new List<byte>(bytes.Length);
            for (int i = 0; i < bytes.Length; i++)
                if (!(bytes[i] == (byte)'\r' && i + 1 < bytes.Length && bytes[i + 1] == (byte)'\n')) output.Add(bytes[i]);
            return output.ToArray();
        }
    }
}

using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace BS.LocalMultiplayer
{
    /// <summary>
    /// Which Multiplayer Play Mode player this editor is, read from the command line. MPPM launches each
    /// clone as a full editor in &lt;Project&gt;/Library/VP/mppmXXXXXXXX with --virtual-project-clone, -vpId=...,
    /// -name "Player N" and -mainProcessId=...; the main editor has none of these. Unity's
    /// CurrentPlayer API is not used: it can throw when the package isn't installed.
    /// </summary>
    public sealed class MppmEnvironment
    {
        public const string MainEditorSlot = "Main Editor";

        public bool IsClone;
        /// <summary>"Main Editor", "Player N", or "vp-&lt;id&gt;" for a clone launched without -name.</summary>
        public string Slot = MainEditorSlot;
        /// <summary>1 for the main editor, N for "Player N", otherwise 0.</summary>
        public int SlotIndex = 1;
        public string VpId = "";
        /// <summary>The main editor's process id (this process when it is the main editor).</summary>
        public int MainProcessId;
        /// <summary>The main project's root folder, also from inside a clone.</summary>
        public string MainProjectRoot = "";
        /// <summary>FNV-1a of the normalised main project root, as 8 hex digits.</summary>
        public string ProjectHash8 = "";

        static MppmEnvironment _current;

        /// <summary>This editor, parsed once per domain.</summary>
        public static MppmEnvironment Current
        {
            get
            {
                if (_current == null)
                {
                    int pid;
                    try { pid = System.Diagnostics.Process.GetCurrentProcess().Id; }
                    catch (Exception) { pid = 0; }
                    _current = Parse(Environment.GetCommandLineArgs(), Application.dataPath, pid);
                }
                return _current;
            }
        }

        static readonly Regex PlayerSlot = new Regex(@"^Player\s*(\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static MppmEnvironment Parse(string[] args, string dataPath, int currentPid)
        {
            args = args ?? Array.Empty<string>();
            var env = new MppmEnvironment();
            string name = null;
            int mainPid = 0;
            var dataPathSlashes = (dataPath ?? "").Replace('\\', '/');
            var vpIndex = dataPathSlashes.IndexOf("/Library/VP/", StringComparison.OrdinalIgnoreCase);
            env.IsClone = vpIndex >= 0;

            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i] ?? "";
                if (arg.Equals("--virtual-project-clone", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("-scenarioClone", StringComparison.OrdinalIgnoreCase))
                {
                    env.IsClone = true;
                }
                else if (arg.StartsWith("-vpId=", StringComparison.OrdinalIgnoreCase))
                {
                    env.IsClone = true;
                    env.VpId = arg.Substring("-vpId=".Length).Trim();
                }
                else if (arg.StartsWith("-mainProcessId=", StringComparison.OrdinalIgnoreCase))
                {
                    int.TryParse(arg.Substring("-mainProcessId=".Length).Trim(), out mainPid);
                }
                else if (arg.Equals("-name", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    name = (args[i + 1] ?? "").Trim().Trim('"');
                    i++;
                }
            }

            if (env.IsClone)
            {
                if (!string.IsNullOrEmpty(name)) env.Slot = name;
                else if (!string.IsNullOrEmpty(env.VpId)) env.Slot = "vp-" + env.VpId;
                else env.Slot = "Player ?";
                var match = PlayerSlot.Match(env.Slot);
                env.SlotIndex = match.Success && int.TryParse(match.Groups[1].Value, out var n) ? n : 0;
                env.MainProcessId = mainPid > 0 ? mainPid : currentPid;
                env.MainProjectRoot = vpIndex >= 0
                    ? SafeFullPath(dataPathSlashes.Substring(0, vpIndex))
                    : SafeFullPath(dataPathSlashes + "/../../../..");
            }
            else
            {
                env.Slot = MainEditorSlot;
                env.SlotIndex = 1;
                env.MainProcessId = currentPid;
                env.MainProjectRoot = SafeFullPath(dataPathSlashes + "/..");
            }
            env.ProjectHash8 = Fnv1a32(NormalizeRoot(env.MainProjectRoot)).ToString("x8");
            return env;
        }

        /// <summary>The relay's project-root comparison form: full path, no trailing separator, backslashes, lower case.</summary>
        public static string NormalizeRoot(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            return SafeFullPath(path).TrimEnd('\\', '/').Replace('/', '\\').ToLowerInvariant();
        }

        static string SafeFullPath(string path)
        {
            try { return Path.GetFullPath(path).TrimEnd('\\', '/'); }
            catch (Exception) { return path; }
        }

        public static uint Fnv1a32(string text)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (var b in System.Text.Encoding.UTF8.GetBytes(text ?? ""))
                {
                    hash ^= b;
                    hash *= 16777619;
                }
                return hash;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            _current = null;
        }
    }
}

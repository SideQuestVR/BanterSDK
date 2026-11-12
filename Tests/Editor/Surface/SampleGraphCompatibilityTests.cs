using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.VisualScripting;
using Unity.VisualScripting.FullSerializer;
using UnityEngine;
using UnityEngine.TestTools;
using UnityObject = UnityEngine.Object;

namespace BS.SDKEditor.Tests.Surface
{
    /// <summary>
    /// Bridge 2 compatibility guard (plan §7): every Visual Scripting graph shipped in the SDK samples still loads
    /// against today's assemblies with no missing unit types, no units that fail to define (a member or type they name
    /// is gone), no connections to ports a node no longer has, and no types the serializer cannot find.
    ///
    /// Samples~ is not imported by Unity, so the graphs are read straight from the YAML (.asset/.prefab/.unity): every
    /// LudiqScriptableObject/LudiqBehaviour "_data._json" scalar, deserialized the way ScriptGraphJsonWindow does
    /// (into a fresh ScriptGraphAsset/StateGraphAsset), with Unity object references left null.
    /// </summary>
    public class SampleGraphCompatibilityTests
    {
        internal sealed class Report
        {
            public int Files, Blobs, Graphs, Units, VariableSets;
            public readonly List<string> Problems = new List<string>();
            public readonly List<string> Notes = new List<string>();
        }

        static readonly string[] Extensions = { ".asset", ".prefab", ".unity" };

        [Test]
        public void EverySampleGraph_LoadsWithNoMissingTypesMembersOrPorts()
        {
            var root = Path.Combine(SurfacePaths.PackageRoot(), "Samples~");
            Assert.IsTrue(Directory.Exists(root), "no Samples~ folder at " + root);
            var report = new Report();
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                         .Where(f => Extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                         .OrderBy(f => f, StringComparer.Ordinal))
            {
                var text = File.ReadAllText(file);
                if (text.IndexOf("_json:", StringComparison.Ordinal) < 0)
                    continue;
                report.Files++;
                var rel = file.Substring(root.Length + 1).Replace('\\', '/');
                foreach (var blob in YamlVsData.Extract(text))
                    CheckBlob(rel + ":" + blob.Line, blob.Json, blob.ObjectReferenceCount, report);
            }

            Debug.Log($"[SampleGraphs] {report.Files} files, {report.Blobs} serialized VS blobs, {report.Graphs} graphs " +
                      $"(nested included), {report.Units} units, {report.VariableSets} variable sets" +
                      (report.Notes.Count > 0 ? "\nNotes:\n  " + string.Join("\n  ", report.Notes.Take(100)) : ""));
            // 2026-11-03: 90 files, 69 graph assets + 22 embedded machine graphs + 78 variable sets. A scan that finds
            // almost nothing is itself a failure.
            Assert.Greater(report.Graphs, 50, "the sample scan found almost no graphs; did Samples~ move or the YAML reader break?");
            Assert.IsEmpty(report.Problems, $"{report.Problems.Count} problem(s) in sample graphs:\n  " + string.Join("\n  ", report.Problems.Take(200)));
        }

        /// <summary>Negative control: the same check, on a graph broken three ways, must notice every one.</summary>
        [Test]
        public void TheCheck_FlagsMissingUnitTypesMissingMembersAndPortDrift()
        {
            var ifUnit = new If();
            var invoke = new InvokeMember(new Member(typeof(Vector3), nameof(Vector3.Normalize), Type.EmptyTypes));
            var literal = new Literal(typeof(bool), true);
            var not = new Negate();
            var literal2 = new Literal(typeof(bool), false);
            var graph = new FlowGraph();
            foreach (var u in new IUnit[] { ifUnit, invoke, literal, not, literal2 })
            {
                graph.units.Add(u);
                u.EnsureDefined();
            }
            graph.valueConnections.Add(new ValueConnection(literal.output, ifUnit.condition));
            graph.valueConnections.Add(new ValueConnection(literal2.output, not.input));

            string json;
            var asset = ScriptableObject.CreateInstance<ScriptGraphAsset>();
            try
            {
                asset.graph = graph;
                json = ((object)asset).Serialize(true).json;
            }
            finally
            {
                UnityObject.DestroyImmediate(asset);
            }
            StringAssert.Contains("\"Unity.VisualScripting.If\"", json);
            StringAssert.Contains("\"name\":\"Normalize\"", json);
            StringAssert.Contains("\"destinationKey\":\"input\"", json);

            var broken = json.Replace("\"Unity.VisualScripting.If\"", "\"BS.VisualScripting.NoSuchUnitForTheCompatTest\"")
                             .Replace("\"name\":\"Normalize\"", "\"name\":\"NoSuchMemberForTheCompatTest\"")
                             .Replace("\"destinationKey\":\"input\"", "\"destinationKey\":\"noSuchPortForTheCompatTest\"");
            var report = new Report();
            var ignore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try { CheckBlob("negative-control", broken, 0, report); }
            finally { LogAssert.ignoreFailingMessages = ignore; }

            var all = string.Join("\n", report.Problems);
            Assert.AreEqual(1, report.Graphs, all);
            StringAssert.Contains("NoSuchUnitForTheCompatTest", all, "a unit type that no longer exists was not reported");
            StringAssert.Contains("failed to define", all, "a member that no longer exists was not reported");
            StringAssert.Contains("noSuchPortForTheCompatTest", all, "a connection to a port that no longer exists was not reported");

            // And the unbroken graph is clean (so the three findings above come from the edits, not the harness).
            var clean = new Report();
            CheckBlob("control", json, 0, clean);
            Assert.IsEmpty(clean.Problems, string.Join("\n", clean.Problems));
        }

        [Test]
        public void YamlReader_UnfoldsQuotedScalarsAndCountsReferences()
        {
            const string yaml =
                "MonoBehaviour:\n" +
                "  _data:\n" +
                "    _json: '{\"graph\":{\"title\":\"Set\n" +
                "      Angular X Drive\",\"q\":\"it''s\"}}'\n" +
                "    _objectReferences:\n" +
                "    - {fileID: 0}\n" +
                "    - {fileID: 2100000, guid: 0123456789abcdef0123456789abcdef, type: 2}\n" +
                "--- !u!114 &2\n" +
                "MonoBehaviour:\n" +
                "  _data:\n" +
                "    _json: \"{\\\"nest\\\":{\\\"a\\\":\\\"\\u00e9\\\\\\\\x\\\"}}\"\n" +
                "    _objectReferences: []\n";
            var blobs = YamlVsData.Extract(yaml);
            Assert.AreEqual(2, blobs.Count);
            Assert.AreEqual("{\"graph\":{\"title\":\"Set Angular X Drive\",\"q\":\"it's\"}}", blobs[0].Json);
            Assert.AreEqual(2, blobs[0].ObjectReferenceCount);
            Assert.AreEqual("{\"nest\":{\"a\":\"\u00e9\\\\x\"}}", blobs[1].Json);
            Assert.AreEqual(0, blobs[1].ObjectReferenceCount);
        }

        // -------------------------------------------------------------------------------------------------------------

        static readonly Regex StateGraphMarker = new Regex(
            "\"\\$type\":\"Unity\\.VisualScripting\\.(FlowState|SuperState|AnyState|FlowStateTransition|TriggerStateTransition)\"",
            RegexOptions.CultureInvariant);

        internal static void CheckBlob(string where, string json, int referenceCount, Report report)
        {
            report.Blobs++;
            fsData root;
            try { root = fsJsonParser.Parse(json); }
            catch (Exception e)
            {
                report.Problems.Add($"{where}: unreadable JSON: {e.Message}");
                return;
            }
            if (!root.IsDictionary)
                return;
            var dict = root.AsDictionary;
            if (dict.TryGetValue("graph", out var g) && g.IsDictionary)
                CheckGraphAsset(where, json, StateGraphMarker.IsMatch(json), referenceCount, report);
            if (dict.TryGetValue("nest", out var nest) && nest.IsDictionary)
            {
                var n = nest.AsDictionary;
                var embed = n.TryGetValue("embed", out var e) ? e : null;
                if (embed != null && embed.IsDictionary)
                {
                    var embedJson = fsJsonPrinter.CompressedJson(embed);
                    CheckGraphAsset(where + " (embedded)", "{\"graph\":" + embedJson + "}", StateGraphMarker.IsMatch(embedJson), referenceCount, report);
                }
            }
            if (dict.TryGetValue("declarations", out var declarations) && declarations.IsDictionary)
                CheckVariables(where + " (variables)", fsJsonPrinter.CompressedJson(declarations), referenceCount, report);
        }

        static void CheckGraphAsset(string where, string assetJson, bool state, int referenceCount, Report report)
        {
            var assetType = state ? Type.GetType("Unity.VisualScripting.StateGraphAsset, Unity.VisualScripting.State") : typeof(ScriptGraphAsset);
            if (assetType == null)
            {
                report.Problems.Add($"{where}: a state graph, but Unity.VisualScripting.State is not loaded");
                return;
            }
            var asset = ScriptableObject.CreateInstance(assetType);
            try
            {
                var warnings = Deserialize(assetJson, asset, referenceCount, out var error);
                if (error != null)
                {
                    report.Problems.Add($"{where}: does not deserialize: {error}");
                    return;
                }
                Classify(where, warnings, report);
                Walk(where, ((IMacro)asset).graph, report, 0);
            }
            finally
            {
                UnityObject.DestroyImmediate(asset);
            }
        }

        static void CheckVariables(string where, string json, int referenceCount, Report report)
        {
            object declarations = new VariableDeclarations();
            var warnings = Deserialize(json, declarations, referenceCount, out var error);
            if (error != null)
            {
                report.Problems.Add($"{where}: does not deserialize: {error}");
                return;
            }
            report.VariableSets++;
            Classify(where, warnings, report);
        }

        /// <summary>Deserializes into <paramref name="target"/> and returns what Visual Scripting warned about meanwhile.</summary>
        static List<string> Deserialize(string json, object target, int referenceCount, out string error)
        {
            var warnings = new List<string>();
            void Hook(string message, string stack, LogType type)
            {
                if (type != LogType.Log) warnings.Add(message);
            }
            error = null;
            Application.logMessageReceived += Hook;
            try
            {
                var boxed = target;
                new SerializationData(json, new UnityObject[referenceCount]).DeserializeInto(ref boxed, target is UnityObject);
            }
            catch (Exception e)
            {
                error = e.GetBaseException().Message;
            }
            finally
            {
                Application.logMessageReceived -= Hook;
            }
            return warnings;
        }

        static readonly Regex MissingTypeWarning = new Regex("Unable to find type|Type definition for|is missing|Missing unit type",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        static void Classify(string where, List<string> warnings, Report report)
        {
            foreach (var w in warnings)
            {
                var first = FirstLine(w);
                if (MissingTypeWarning.IsMatch(w)) report.Problems.Add($"{where}: {first}");
                else if (w.StartsWith("[Deserialization]", StringComparison.Ordinal)) report.Notes.Add($"{where}: {first}");
            }
        }

        static void Walk(string where, IGraph graph, Report report, int depth)
        {
            if (graph == null || depth > 32)
                return;
            report.Graphs++;
            foreach (var element in graph.elements.ToList())
            {
                if (element is IUnit unit)
                {
                    report.Units++;
                    unit.EnsureDefined();
                    var name = unit.GetType().FullName;
                    if (unit is MissingType missing)
                        report.Problems.Add($"{where}: missing unit type {missing.formerType}");
                    else if (unit.failedToDefine)
                        report.Problems.Add($"{where}: {name} {Describe(unit)} failed to define: {FirstLine(unit.definitionException?.Message)}");
                    var drift = unit.invalidInputs.Select(p => p.key).Concat(unit.invalidOutputs.Select(p => p.key)).ToList();
                    // This test deserializes with every object reference null, so a subgraph that points at a macro asset
                    // has no graph and defines no ports; its connections then look like drift. The macro assets are
                    // checked on their own, and in a real bundle the reference resolves.
                    bool unresolvedMacro = unit is IGraphNesterElement macroNester && macroNester.nest != null
                        && macroNester.nest.source == GraphSource.Macro && macroNester.nest.macro == null;
                    if (drift.Count > 0 && !(unit is MissingType) && !unresolvedMacro)
                        report.Problems.Add($"{where}: {name} {Describe(unit)} is connected through ports it no longer has: {string.Join(", ", drift)}");
                }
                if (element is IGraphNesterElement nester && nester.nest != null && nester.nest.source == GraphSource.Embed)
                    Walk(where + " > " + element.GetType().Name, nester.nest.embed, report, depth + 1);
            }
        }

        static string Describe(IUnit unit) => unit is MemberUnit m && m.member != null ? $"({m.member.targetTypeName}.{m.member.name})" : "";

        static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var i = s.IndexOf('\n');
            return i < 0 ? s : s.Substring(0, i);
        }
    }

    /// <summary>
    /// Pulls Visual Scripting's serialized "_json" scalars (and the size of the sibling "_objectReferences" list) out of
    /// Unity YAML without a YAML library: single-quoted scalars with line folding and '' escapes, double-quoted scalars
    /// with backslash escapes, as Unity's writer emits them.
    /// </summary>
    internal static class YamlVsData
    {
        internal struct Blob
        {
            public string Json;
            public int ObjectReferenceCount;
            public int Line;
        }

        const string Key = "_json: ";

        public static List<Blob> Extract(string yaml)
        {
            var blobs = new List<Blob>();
            var lines = yaml.Replace("\r\n", "\n").Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var at = line.IndexOf(Key, StringComparison.Ordinal);
                if (at < 0 || line.Substring(0, at).Trim().Length != 0 || at + Key.Length >= line.Length)
                    continue;
                var quote = line[at + Key.Length];
                int end;
                string json;
                if (quote == '\'') json = ReadSingleQuoted(lines, i, at + Key.Length + 1, out end);
                else if (quote == '"') json = ReadDoubleQuoted(lines, i, at + Key.Length + 1, out end);
                else { json = line.Substring(at + Key.Length).Trim(); end = i; }
                blobs.Add(new Blob { Json = json, ObjectReferenceCount = CountReferences(lines, end + 1, at), Line = i + 1 });
                i = end;
            }
            return blobs;
        }

        static int CountReferences(string[] lines, int from, int indent)
        {
            for (var j = from; j < lines.Length; j++)
            {
                var t = lines[j].TrimStart(' ');
                var ind = lines[j].Length - t.Length;
                if (t.StartsWith("_objectReferences:", StringComparison.Ordinal))
                {
                    if (t.Contains("[]")) return 0;
                    var n = 0;
                    for (var k = j + 1; k < lines.Length; k++)
                    {
                        var tk = lines[k].TrimStart(' ');
                        if (tk.StartsWith("- ", StringComparison.Ordinal) && lines[k].Length - tk.Length >= ind) n++;
                        else break;
                    }
                    return n;
                }
                if (ind < indent || t.Length == 0) break;
            }
            return 0;
        }

        static void TrimTrailingSpaces(StringBuilder sb, int floor)
        {
            while (sb.Length > floor && (sb[sb.Length - 1] == ' ' || sb[sb.Length - 1] == '\t')) sb.Length--;
        }

        static string ReadSingleQuoted(string[] lines, int row, int col, out int endRow)
        {
            var sb = new StringBuilder();
            var breaks = 0;
            for (var r = row; r < lines.Length; r++)
            {
                var s = lines[r];
                var start = col;
                if (r != row)
                {
                    var trimmed = s.TrimStart(' ', '\t');
                    if (trimmed.Length == 0) { breaks++; continue; }
                    start = s.Length - trimmed.Length;
                    // Folding: one line break between text is a space; each empty line is a newline.
                    if (breaks > 0) sb.Append('\n', breaks); else sb.Append(' ');
                    breaks = 0;
                }
                var lineStart = sb.Length;
                for (var k = start; k < s.Length; k++)
                {
                    var ch = s[k];
                    if (ch == '\'')
                    {
                        if (k + 1 < s.Length && s[k + 1] == '\'') { sb.Append('\''); k++; continue; }
                        endRow = r;
                        return sb.ToString();
                    }
                    sb.Append(ch);
                }
                TrimTrailingSpaces(sb, lineStart);
                col = 0;
            }
            throw new FormatException("unterminated single-quoted YAML scalar at line " + (row + 1));
        }

        static string ReadDoubleQuoted(string[] lines, int row, int col, out int endRow)
        {
            var sb = new StringBuilder();
            var breaks = 0;
            var escapedBreak = false;
            for (var r = row; r < lines.Length; r++)
            {
                var s = lines[r];
                var start = col;
                if (r != row)
                {
                    var trimmed = s.TrimStart(' ', '\t');
                    if (trimmed.Length == 0 && !escapedBreak) { breaks++; continue; }
                    start = s.Length - trimmed.Length;
                    if (!escapedBreak)
                    {
                        if (breaks > 0) sb.Append('\n', breaks); else sb.Append(' ');
                    }
                    breaks = 0;
                    escapedBreak = false;
                }
                var lineStart = sb.Length;
                for (var k = start; k < s.Length; k++)
                {
                    var ch = s[k];
                    if (ch == '"') { endRow = r; return sb.ToString(); }
                    if (ch != '\\') { sb.Append(ch); continue; }
                    if (k + 1 >= s.Length) { escapedBreak = true; break; }
                    var e = s[++k];
                    switch (e)
                    {
                        case '0': sb.Append('\0'); break;
                        case 'a': sb.Append('\a'); break;
                        case 'b': sb.Append('\b'); break;
                        case 't': case '\t': sb.Append('\t'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'v': sb.Append('\v'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'r': sb.Append('\r'); break;
                        case 'e': sb.Append('\u001b'); break;
                        case 'N': sb.Append('\u0085'); break;
                        case '_': sb.Append('\u00a0'); break;
                        case 'L': sb.Append('\u2028'); break;
                        case 'P': sb.Append('\u2029'); break;
                        case 'x': sb.Append((char)int.Parse(s.Substring(k + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)); k += 2; break;
                        case 'u': sb.Append((char)int.Parse(s.Substring(k + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture)); k += 4; break;
                        case 'U': sb.Append(char.ConvertFromUtf32(int.Parse(s.Substring(k + 1, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture))); k += 8; break;
                        default: sb.Append(e); break; // \" \\ \/ and "\ "
                    }
                }
                if (!escapedBreak) TrimTrailingSpaces(sb, lineStart);
                col = 0;
            }
            throw new FormatException("unterminated double-quoted YAML scalar at line " + (row + 1));
        }
    }
}

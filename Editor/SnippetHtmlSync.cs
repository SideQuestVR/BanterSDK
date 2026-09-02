using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using UnityEditor;
using UnityEngine;

namespace BS.SDKEditor
{
    /*
     * Single source of truth for the "<!-- snippet section -->" block in Assets/WebRoot/index.html.
     *
     * A static class, not a ScriptableSingleton: the persistent model IS index.html — a domain
     * reload just calls Load() again. The only state that could be lost across a reload is a
     * pending debounced write, which is flushed in beforeAssemblyReload/quitting.
     *
     * The file as a whole is never parsed as XML (it can't be — "<html android-bundle
     * windows-bundle>" has valueless attributes). Only the fragment between the two markers is
     * parsed, under a synthetic <snippets> root; everything outside the markers is preserved
     * byte-for-byte because writes re-read the file fresh and splice only the section.
     */
    [InitializeOnLoad]
    public static class SnippetHtmlSync
    {
        public const string BeginMarker = "<!-- snippet section -->";
        public const string EndMarker = "<!-- end snippet section -->";
        public const string ElementName = "bs-snippet";
        public const string GizmoElementName = "bs-gizmo";
        public const string InstanceAttribute = "instance";

        public enum ChangeKind { Reloaded, ElementUpserted, ElementRemoved, AttributeSet }
        // (kind, instanceId or null, attribute name or null)
        public static event Action<ChangeKind, string, string> Changed;

        public static string AssetPath => "Assets/" + BSStarterUpper.WEB_ROOT + "/index.html";
        static string FullPath => Path.GetFullPath(AssetPath);

        // Debounce is short because gizmo drags spam SetAttribute every frame; the flush itself is
        // one small file write. Pattern: ProjectPrefs.cs (dirty timestamp + update pump + flush on
        // teardown).
        const double WriteDelay = 0.75;
        const double PollInterval = 1.0;

        static XDocument _sectionDoc = new XDocument(new XElement("snippets"));
        static readonly Dictionary<string, XElement> _byInstance = new Dictionary<string, XElement>();
        static bool _dirty;
        static double _lastDirtyTime;
        static bool _loadFailed;
        static string _lastSelfWriteText;
        static DateTime _lastLoadedMtimeUtc;
        static double _lastPollTime;
        static int _inspectorRefs;

        static SnippetHtmlSync()
        {
            Load();
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += FlushNow;
            EditorApplication.quitting += FlushNow;
        }

        public static bool LoadFailed => _loadFailed;

        public static XElement Get(string instanceId) =>
            !string.IsNullOrEmpty(instanceId) && _byInstance.TryGetValue(instanceId, out var e) ? e : null;

        public static IEnumerable<XElement> All() => _sectionDoc.Root.Elements(ElementName);

        public static XElement FindAnyBySlug(string slug) =>
            string.IsNullOrEmpty(slug) ? null : All().FirstOrDefault(e => (string)e.Attribute("name") == slug);

        public static void Upsert(string instanceId, XElement element)
        {
            if (RefuseWhenBroken() || string.IsNullOrEmpty(instanceId) || element == null) return;
            element.SetAttributeValue(InstanceAttribute, instanceId);
            var existing = Get(instanceId);
            if (existing != null)
                existing.ReplaceWith(element);
            else
                _sectionDoc.Root.Add(element);
            _byInstance[instanceId] = element;
            MarkDirty();
            Changed?.Invoke(ChangeKind.ElementUpserted, instanceId, null);
        }

        public static void Remove(string instanceId)
        {
            if (RefuseWhenBroken()) return;
            var existing = Get(instanceId);
            if (existing == null) return;
            existing.Remove();
            _byInstance.Remove(instanceId);
            MarkDirty();
            Changed?.Invoke(ChangeKind.ElementRemoved, instanceId, null);
        }

        // For section elements without a usable instance attribute (hand-authored or mangled) —
        // the instance-id overload above is the normal path.
        public static void RemoveElement(XElement element)
        {
            if (RefuseWhenBroken() || element == null || element.Parent != _sectionDoc.Root) return;
            var id = (string)element.Attribute(InstanceAttribute);
            element.Remove();
            if (!string.IsNullOrEmpty(id)) _byInstance.Remove(id);
            MarkDirty();
            Changed?.Invoke(ChangeKind.ElementRemoved, id, null);
        }

        public static void SetAttribute(string instanceId, string name, string value)
        {
            if (RefuseWhenBroken()) return;
            var existing = Get(instanceId);
            if (existing == null || string.IsNullOrEmpty(name)) return;
            if ((string)existing.Attribute(name) == value) return;
            existing.SetAttributeValue(name, value);
            MarkDirty();
            Changed?.Invoke(ChangeKind.AttributeSet, instanceId, name);
        }

        // Inspectors register themselves so the mtime poll (which catches saves from external
        // editors before Unity regains focus and reimports) only runs while someone is looking.
        public static void AddInspectorRef() => _inspectorRefs++;
        public static void RemoveInspectorRef() => _inspectorRefs = Math.Max(0, _inspectorRefs - 1);

        public static void Load()
        {
            string text = null;
            if (File.Exists(FullPath))
            {
                try
                {
                    text = File.ReadAllText(FullPath);
                    _lastLoadedMtimeUtc = File.GetLastWriteTimeUtc(FullPath);
                }
                catch (IOException)
                {
                    // Mid-write by another process; the poll or postprocessor will retry.
                    return;
                }
            }

            var doc = new XDocument(new XElement("snippets"));
            if (text != null)
            {
                var begin = text.IndexOf(BeginMarker, StringComparison.Ordinal);
                var end = text.IndexOf(EndMarker, StringComparison.Ordinal);
                if (begin >= 0 && end > begin)
                {
                    var fragment = text.Substring(begin + BeginMarker.Length, end - (begin + BeginMarker.Length));
                    try
                    {
                        doc = XDocument.Parse("<snippets>" + fragment + "</snippets>");
                    }
                    catch (Exception e)
                    {
                        // Keep the previous in-memory model and block all writes — never risk
                        // clobbering a hand-edited-but-broken section. Fixing the file (and
                        // letting it reimport / get polled) clears the flag.
                        Debug.LogError($"[BSSnippet] Could not parse the snippet section in {AssetPath}: {e.Message}. Snippet editing is disabled until the section is fixed.");
                        _loadFailed = true;
                        return;
                    }
                }
                else if (begin >= 0 || end >= 0)
                {
                    Debug.LogError($"[BSSnippet] {AssetPath} has a mangled snippet section (one marker is missing). Snippet editing is disabled until both '{BeginMarker}' and '{EndMarker}' are present, in order.");
                    _loadFailed = true;
                    return;
                }
            }

            _loadFailed = false;
            _sectionDoc = doc;
            _byInstance.Clear();
            foreach (var element in _sectionDoc.Root.Elements(ElementName))
            {
                var id = (string)element.Attribute(InstanceAttribute);
                if (!string.IsNullOrEmpty(id) && !_byInstance.ContainsKey(id))
                    _byInstance[id] = element;
            }
            Changed?.Invoke(ChangeKind.Reloaded, null, null);
        }

        public static void FlushNow()
        {
            if (!_dirty) return;
            _dirty = false;
            try
            {
                WriteToDisk();
            }
            catch (Exception e)
            {
                Debug.LogError($"[BSSnippet] Failed to write {AssetPath}: {e.Message}");
            }
        }

        static bool RefuseWhenBroken()
        {
            if (!_loadFailed) return false;
            Debug.LogError($"[BSSnippet] The snippet section in {AssetPath} is unreadable — fix it (or delete the section) before editing snippets.");
            return true;
        }

        static void MarkDirty()
        {
            _dirty = true;
            _lastDirtyTime = EditorApplication.timeSinceStartup;
        }

        static void Update()
        {
            if (_dirty && EditorApplication.timeSinceStartup - _lastDirtyTime >= WriteDelay)
                FlushNow();

            // Skip the poll while dirty: we are the writer, and a reload here would discard the
            // pending in-memory edits.
            if (_inspectorRefs > 0 && !_dirty && EditorApplication.timeSinceStartup - _lastPollTime >= PollInterval)
            {
                _lastPollTime = EditorApplication.timeSinceStartup;
                try
                {
                    var mtime = File.Exists(FullPath) ? File.GetLastWriteTimeUtc(FullPath) : DateTime.MinValue;
                    if (mtime != _lastLoadedMtimeUtc)
                        OnExternalMaybeChanged();
                }
                catch (IOException) { }
            }
        }

        static void OnExternalMaybeChanged()
        {
            string text = null;
            try
            {
                if (File.Exists(FullPath)) text = File.ReadAllText(FullPath);
            }
            catch (IOException)
            {
                return;
            }
            if (text != null && text == _lastSelfWriteText)
            {
                // Our own write coming back through the importer — not an external edit. This is
                // the guard that stops the write→import→reload feedback loop.
                _lastLoadedMtimeUtc = File.GetLastWriteTimeUtc(FullPath);
                return;
            }
            Load();
        }

        static void WriteToDisk()
        {
            string text;
            try
            {
                // Re-read fresh so a concurrent external edit outside the markers is preserved —
                // only the section between the markers is ours.
                text = File.Exists(FullPath) ? File.ReadAllText(FullPath) : null;
            }
            catch (IOException)
            {
                MarkDirty(); // file busy — retry after another delay
                return;
            }
            if (text == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FullPath));
                text = "<html android-bundle windows-bundle>\n<head>\n  <meta charset=\"utf-8\">\n  <title>Space</title>\n</head>\n<body>\n</body>\n</html>\n";
            }

            var section = BuildSectionBody();
            var begin = text.IndexOf(BeginMarker, StringComparison.Ordinal);
            var end = text.IndexOf(EndMarker, StringComparison.Ordinal);
            string result;
            if (begin >= 0 && end > begin)
            {
                result = text.Substring(0, begin + BeginMarker.Length) + section + text.Substring(end);
            }
            else
            {
                var block = "  " + BeginMarker + section + EndMarker + "\n";
                var bodyClose = text.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
                result = bodyClose >= 0
                    ? text.Substring(0, bodyClose) + block + text.Substring(bodyClose)
                    : text + "\n" + block;
            }

            File.WriteAllText(FullPath, result, new UTF8Encoding(false));
            _lastSelfWriteText = result;
            _lastLoadedMtimeUtc = File.GetLastWriteTimeUtc(FullPath);
            AssetDatabase.ImportAsset(AssetPath);
        }

        static string BuildSectionBody()
        {
            // Serialize child NODES, not just elements: comments a user added inside the section
            // survive. LINQ-to-XML round-trips attribute order, so diffs only churn where an
            // attribute actually changed.
            var sb = new StringBuilder();
            foreach (var node in _sectionDoc.Root.Nodes())
            {
                sb.Append("\n  ");
                sb.Append(node.ToString().Replace("\n", "\n  "));
            }
            sb.Append("\n  ");
            return sb.ToString();
        }

        class Importer : AssetPostprocessor
        {
            static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                foreach (var path in imported)
                {
                    if (string.Equals(path, AssetPath, StringComparison.OrdinalIgnoreCase))
                    {
                        OnExternalMaybeChanged();
                        return;
                    }
                }
            }
        }
    }
}

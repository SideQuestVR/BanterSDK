using System;
using System.IO;
using System.Text;
using BS;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace BS.SDKEditor
{
    // One pose per file. JsonUtility reads and writes Unity vectors directly, but leaves missing values at their
    // defaults, so JObject first checks that the file really holds a whole pose.
    static class BSPosePresetFile
    {
        internal const string HandKind = "hand", SeatKind = "seat";
        const string Format = "sidequest.pose-preset";
        const int Version = 1;
        const string HandTitle = "Load Hand Pose Preset", SeatTitle = "Load Seat Pose Preset";
        static readonly string[] HandVectors = { "position", "rotation" };
        static readonly string[] HandFingers = { "thumb", "index", "middle", "ring", "little" };
        static readonly string[] SeatVectors = { "pelvisPosition", "leftFootPosition", "rightFootPosition", "leftKneeHint", "rightKneeHint" };
        static readonly string[] SeatRotations = { "pelvisRotation", "leftFootRotation", "rightFootRotation" };

        [Serializable]
        internal sealed class HandPreset
        {
            public string format = Format;
            public int version = Version;
            public string kind = HandKind;
            public string name;
            public bool separateLeft;
            // The grab radius moves the hand as well as the offset, so loading onto another radius has to allow for it.
            public float sourceGrabRadius;
            public BSPointHandPose right = new BSPointHandPose();
            public BSPointHandPose left = new BSPointHandPose();
        }

        [Serializable]
        internal sealed class SeatPreset
        {
            public string format = Format;
            public int version = Version;
            public string kind = SeatKind;
            public string name;
            public SeatPose pose = SeatPose.Default;
        }

        internal static HandPreset Capture(Component source, string name)
        {
            var fields = new SerializedObject(source);
            var settings = fields.FindProperty("customPointPose");
            var separate = settings.FindPropertyRelative("separateLeft").boolValue;
            var right = BSPointGrabPoseEditor.ReadHand(settings.FindPropertyRelative("right"));
            return new HandPreset
            {
                name = name,
                separateLeft = separate,
                sourceGrabRadius = fields.FindProperty("grabRadius").floatValue,
                right = right,
                left = separate ? BSPointGrabPoseEditor.ReadHand(settings.FindPropertyRelative("left")) : right.Copy(true)
            };
        }

        internal static SeatPreset Capture(BSSeat seat, string name) => new SeatPreset { name = name, pose = seat.Pose };

        internal static void Apply(Component source, HandPreset preset)
        {
            var fields = new SerializedObject(source);
            var settings = fields.FindProperty("customPointPose");
            // The right hand sits at +radius + x. A mirrored left follows it; a separate left sits at -radius + x.
            var shift = preset.sourceGrabRadius - fields.FindProperty("grabRadius").floatValue;
            settings.FindPropertyRelative("separateLeft").boolValue = preset.separateLeft;
            BSPointGrabPoseEditor.WriteHand(settings.FindPropertyRelative("right"), Shifted(preset.right, shift));
            if (preset.separateLeft)
                BSPointGrabPoseEditor.WriteHand(settings.FindPropertyRelative("left"), Shifted(preset.left, -shift));
            Undo.SetCurrentGroupName("Load hand pose preset");
            fields.ApplyModifiedProperties();
            SceneView.RepaintAll();
        }

        internal static void Apply(BSSeat seat, SeatPreset preset)
        {
            var fields = new SerializedObject(seat);
            BSSeatEditor.WritePose(fields.FindProperty("pose"), preset.pose);
            Undo.SetCurrentGroupName("Load seat pose preset");
            fields.ApplyModifiedProperties();
            SceneView.RepaintAll();
        }

        static BSPointHandPose Shifted(BSPointHandPose hand, float x)
        {
            var copy = hand.Copy();
            copy.position.x += x;
            return copy;
        }

        internal static string ToJson(HandPreset preset) => JsonUtility.ToJson(preset, true);
        internal static string ToJson(SeatPreset preset) => JsonUtility.ToJson(preset, true);

        internal static bool TryParse(string json, out HandPreset preset, out string error)
        {
            preset = null;
            try
            {
                if (!TryHeader(json, HandKind, out var root, out error)) return false;
                var separate = root["separateLeft"];
                var radius = root["sourceGrabRadius"];
                if (separate?.Type != JTokenType.Boolean || radius == null
                    || radius.Type != JTokenType.Float && radius.Type != JTokenType.Integer
                    || !HasHand(root["right"]) || (bool)separate && !HasHand(root["left"]))
                    return Fail("This hand preset is incomplete.", out error);
                var parsed = JsonUtility.FromJson<HandPreset>(json);
                if (!Finite(parsed.sourceGrabRadius) || !Finite(parsed.right) || parsed.separateLeft && !Finite(parsed.left))
                    return Fail("This hand preset contains invalid numbers.", out error);
                Clamp(parsed.right);
                if (parsed.separateLeft) Clamp(parsed.left);
                preset = parsed;
                return true;
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                return Fail("This file could not be read as a hand preset: " + e.Message, out error);
            }
        }

        internal static bool TryParse(string json, out SeatPreset preset, out string error)
        {
            preset = null;
            try
            {
                if (!TryHeader(json, SeatKind, out var root, out error)) return false;
                if (!Has(root["pose"], SeatVectors, "xyz") || !Has(root["pose"], SeatRotations, "xyzw"))
                    return Fail("This seat preset is incomplete.", out error);
                var parsed = JsonUtility.FromJson<SeatPreset>(json);
                // Validated() would quietly swap a bad pose for the default.
                if (!parsed.pose.IsValid) return Fail("This seat preset contains invalid numbers.", out error);
                parsed.pose = parsed.pose.Validated();
                preset = parsed;
                return true;
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                return Fail("This file could not be read as a seat preset: " + e.Message, out error);
            }
        }

        static bool TryHeader(string json, string kind, out JObject root, out string error)
        {
            root = JObject.Parse(json);
            if (root["format"]?.ToString() != Format) return Fail("This is not a pose preset file.", out error);
            var version = root["version"];
            if (version?.Type != JTokenType.Integer || (long)version < 1)
                return Fail("This pose preset has no valid version.", out error);
            if ((long)version > Version)
                return Fail("This pose preset was saved by a newer SDK. Update the SDK to load it.", out error);
            var found = root["kind"]?.ToString();
            if (found != kind) return Fail($"This is a {found} preset, not a {kind} preset.", out error);
            error = null;
            return true;
        }

        static bool HasHand(JToken token) => Has(token, HandVectors, "xyz") && Has(token, HandFingers, "xyzw");

        static bool Has(JToken token, string[] names, string axes)
        {
            if (!(token is JObject value)) return false;
            foreach (var name in names)
            {
                if (!(value[name] is JObject vector)) return false;
                foreach (var axis in axes)
                {
                    var type = vector[axis.ToString()]?.Type;
                    if (type != JTokenType.Float && type != JTokenType.Integer) return false;
                }
            }
            return true;
        }

        static bool Fail(string message, out string error)
        {
            error = message;
            return false;
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool Finite(Vector4 value) => Finite(value.x) && Finite(value.y) && Finite(value.z) && Finite(value.w);

        static bool Finite(BSPointHandPose hand)
        {
            if (!Finite((Vector4)hand.position) || !Finite((Vector4)hand.rotation)) return false;
            for (var i = 0; i < 5; i++) if (!Finite(hand.GetFinger(i))) return false;
            return true;
        }

        // Files can be edited by hand; keep them inside what the pose controls can produce.
        static void Clamp(BSPointHandPose hand)
        {
            for (var i = 0; i < 5; i++) hand.SetFinger(i, BSPointGrabPoseEditor.ClampFinger(i, hand.GetFinger(i)));
        }

        internal static void Save(Component source)
        {
            if (source == null) return;
            if (TryChooseSavePath("Save Hand Pose Preset", HandKind, source.name, out var path, out var name))
                Write("Save Hand Pose Preset", path, ToJson(Capture(source, name)));
        }

        internal static void Save(BSSeat seat)
        {
            if (seat == null) return;
            if (TryChooseSavePath("Save Seat Pose Preset", SeatKind, seat.name, out var path, out var name))
                Write("Save Seat Pose Preset", path, ToJson(Capture(seat, name)));
        }

        internal static void Load(Component source)
        {
            var path = ChooseOpenPath(HandTitle, HandKind);
            if (!string.IsNullOrEmpty(path)) Load(source, path);
        }

        internal static void Load(Component source, string path)
        {
            if (source == null || !TryRead(HandTitle, path, out var json)) return;
            if (TryParse(json, out HandPreset preset, out var error)) Apply(source, preset);
            else EditorUtility.DisplayDialog(HandTitle, error, "OK");
        }

        internal static void Load(BSSeat seat)
        {
            var path = ChooseOpenPath(SeatTitle, SeatKind);
            if (!string.IsNullOrEmpty(path)) Load(seat, path);
        }

        internal static void Load(BSSeat seat, string path)
        {
            if (seat == null || !TryRead(SeatTitle, path, out var json)) return;
            if (TryParse(json, out SeatPreset preset, out var error)) Apply(seat, preset);
            else EditorUtility.DisplayDialog(SeatTitle, error, "OK");
        }

        // The folder is read when the menu opens, and each item keeps its own path.
        internal static void DrawFolderPresets(string kind, Action<string> load)
        {
            var folder = PresetFolder;
            var hasFolder = !string.IsNullOrEmpty(folder);
            using (new EditorGUILayout.HorizontalScope())
            {
                var rect = EditorGUI.PrefixLabel(EditorGUILayout.GetControlRect(), new GUIContent("Folder Preset"));
                using (new EditorGUI.DisabledScope(!hasFolder))
                    if (EditorGUI.DropdownButton(rect, new GUIContent(hasFolder ? "Select to load…" : "Choose a folder first"), FocusType.Keyboard))
                    {
                        var menu = new GenericMenu();
                        var files = FindPresets(folder, kind);
                        foreach (var file in files)
                        {
                            var path = file;
                            menu.AddItem(new GUIContent(PresetName(path, kind)), false, () => load(path));
                        }
                        if (files.Length == 0) menu.AddDisabledItem(new GUIContent("No presets in folder"));
                        menu.DropDown(rect);
                    }
                var tooltip = hasFolder ? folder : "Choose the folder that holds your pose presets.";
                if (GUILayout.Button(new GUIContent("Folder…", tooltip), GUILayout.Width(60f)))
                    EditorApplication.delayCall += ChooseFolder;
            }
        }

        internal static string[] FindPresets(string folder, string kind)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return Array.Empty<string>();
            try
            {
                var files = Directory.GetFiles(folder, "*" + Suffix(kind) + ".json");
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                return files;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return Array.Empty<string>();
            }
        }

        internal static string WithSuffix(string path, string kind) =>
            Path.Combine(Path.GetDirectoryName(path), PresetName(path, kind) + Suffix(kind) + ".json").Replace('\\', '/');

        internal static string PresetName(string path, string kind)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var suffix = Suffix(kind);
            return name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? name.Substring(0, name.Length - suffix.Length) : name;
        }

        static void ChooseFolder()
        {
            var folder = EditorUtility.OpenFolderPanel("Pose Preset Folder", StartDirectory(HandKind), "");
            if (string.IsNullOrEmpty(folder)) return;
            PresetFolder = folder;
        }

        static string Suffix(string kind) => "." + kind + "pose";
        static string DirectoryKey(string kind) => "BS.PosePreset.Directory." + kind;

        // Each project has its own preset folder.
        static string FolderKey => "BS.PosePreset.Folder." + Application.dataPath;
        static string PresetFolder
        {
            get => EditorPrefs.GetString(FolderKey, "");
            set => EditorPrefs.SetString(FolderKey, value);
        }

        static string StartDirectory(string kind)
        {
            var folder = PresetFolder;
            if (Directory.Exists(folder)) return folder;
            var directory = EditorPrefs.GetString(DirectoryKey(kind), Directory.GetCurrentDirectory());
            return Directory.Exists(directory) ? directory : Directory.GetCurrentDirectory();
        }

        static void Remember(string kind, string path) => EditorPrefs.SetString(DirectoryKey(kind), Path.GetDirectoryName(path));

        static bool TryChooseSavePath(string title, string kind, string suggested, out string path, out string name)
        {
            foreach (var invalid in Path.GetInvalidFileNameChars()) suggested = suggested.Replace(invalid, '_');
            path = EditorUtility.SaveFilePanel(title, StartDirectory(kind), suggested + Suffix(kind), "json");
            name = null;
            if (string.IsNullOrEmpty(path)) return false;
            // Without the suffix the file would not show up in the folder list. The dialog only confirmed the typed name.
            var named = WithSuffix(path, kind);
            if (named != path && File.Exists(named)
                && !EditorUtility.DisplayDialog(title, Path.GetFileName(named) + " already exists. Replace it?", "Replace", "Cancel"))
                return false;
            path = named;
            Remember(kind, path);
            name = PresetName(path, kind);
            return true;
        }

        static void Write(string title, string path, string json)
        {
            try { File.WriteAllText(path, json, new UTF8Encoding(false)); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                EditorUtility.DisplayDialog(title, "The preset could not be saved: " + e.Message, "OK");
            }
        }

        static string ChooseOpenPath(string title, string kind)
        {
            var path = EditorUtility.OpenFilePanel(title, StartDirectory(kind), "json");
            if (!string.IsNullOrEmpty(path)) Remember(kind, path);
            return path;
        }

        static bool TryRead(string title, string path, out string json)
        {
            json = null;
            try
            {
                json = File.ReadAllText(path);
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                EditorUtility.DisplayDialog(title, "The preset could not be read: " + e.Message, "OK");
                return false;
            }
        }
    }
}

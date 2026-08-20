using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace SideQuest.BundleAnalyzer
{
    public class BundleAnalyzerWindow : EditorWindow
    {
        const string PackageEditorDir = "Packages/com.sidequest.creator-sdk/Editor/BundleAnalyzer";
        const int ChunkSize = 80;

        // Rough correction for the archive-level LZ4/LZMA compression Unity applies to the
        // whole bundle on top of each asset's own per-platform compression, which the per-asset
        // estimate below doesn't model (raw mesh/text/script data especially compresses a lot
        // further at that layer). Calibrated from comparing this tool's estimate against two
        // real builds of this project (Windows: 227MB est. / 60MB real, Android: 165MB est. /
        // 43MB real - both ~0.26x). This ratio is content-dependent (varies with how redundant/
        // compressible the actual data is) and will NOT generalize precisely to other projects -
        // treat the corrected total as a ballpark, not a guarantee. "Build & Measure" (build a
        // disposable bundle and read its real size) would replace this with ground truth if
        // ever added.
        const double ArchiveCompressionFactor = 0.26;

        List<AssetDependencyEntry> m_AllEntries = new();
        List<AssetDependencyEntry> m_FilteredEntries = new();

        string m_SearchText = "";
        readonly HashSet<string> m_SelectedTypes = new();
        Dictionary<string, Color> m_TypeColors = new();
        List<(string Label, long Bytes, Color Color)> m_SizeBreakdown;

        string m_RootAssetPath;
        string m_Platform = "Standalone";

        string[] m_PendingPaths = Array.Empty<string>();
        int m_PendingIndex;
        bool m_IsAnalyzing;
        bool m_CancelRequested;

        GUIStyle m_BlockLabelStyle;

        MultiColumnListView m_ListView;
        Label m_StatusLabel;
        Label m_FooterLabel;
        ToolbarMenu m_TypeFilterMenu;
        VisualElement m_EmptyStateContainer;
        Label m_EmptyStateLabel;
        VisualElement m_MainContent;
        IMGUIContainer m_PieImgui;
        IMGUIContainer m_BlockViewImgui;
        Label m_HoverInfoLabel;
        ObjectField m_TargetField;
        DropdownField m_PlatformDropdown;
        ToolbarButton m_CancelButton;
        ProgressBar m_LoadProgressBar;

        [MenuItem("Altspace/Tools/Bundle Analyzer")]
        public static void Open()
        {
            var window = GetWindow<BundleAnalyzerWindow>();
            window.titleContent = new GUIContent("Bundle Analyzer");
            window.minSize = new Vector2(720, 420);
        }

        /// <summary>
        /// Opens (or focuses) the window and immediately starts analyzing the given Scene or
        /// Prefab asset path - the entry point external callers (e.g. an "Analyze" button
        /// injected into another tool's window) use to jump straight to a specific target.
        /// </summary>
        public static void OpenAndAnalyze(string assetPath)
        {
            var window = GetWindow<BundleAnalyzerWindow>();
            window.titleContent = new GUIContent("Bundle Analyzer");
            window.minSize = new Vector2(720, 420);
            window.Focus();

            if (string.IsNullOrEmpty(assetPath))
                return;

            var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (obj == null)
            {
                Debug.LogWarning($"Bundle Analyzer: no asset found at '{assetPath}'.");
                return;
            }

            // CreateGUI runs on the next editor update after a freshly-created window, so
            // defer setting the target field by one frame if it isn't wired up yet.
            if (window.m_TargetField != null)
                window.m_TargetField.value = obj;
            else
                EditorApplication.delayCall += () => window.m_TargetField.value = obj;
        }

        void OnEnable()
        {
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        }

        void OnDisable()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            EditorApplication.update -= ProcessChunk;
        }

        void OnBeforeAssemblyReload()
        {
            EditorApplication.update -= ProcessChunk;
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;

            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>($"{PackageEditorDir}/BundleAnalyzerWindow.uxml");
            visualTree.CloneTree(root);

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>($"{PackageEditorDir}/BundleAnalyzerWindow.uss");
            root.styleSheets.Add(styleSheet);

            m_TargetField = root.Q<ObjectField>("TargetField");
            m_TargetField.objectType = typeof(UnityEngine.Object);
            m_TargetField.RegisterValueChangedCallback(OnTargetChanged);

            m_PlatformDropdown = root.Q<DropdownField>("PlatformDropdown");
            m_PlatformDropdown.choices = new List<string> { "Standalone", "Android", "iPhone", "WebGL" };
            m_Platform = DefaultPlatformForActiveBuildTarget();
            m_PlatformDropdown.SetValueWithoutNotify(m_Platform);
            m_PlatformDropdown.RegisterValueChangedCallback(evt =>
            {
                m_Platform = evt.newValue;
                if (!string.IsNullOrEmpty(m_RootAssetPath))
                    StartAnalysis(m_RootAssetPath);
            });

            m_CancelButton = root.Q<ToolbarButton>("CancelButton");
            m_CancelButton.clicked += () => m_CancelRequested = true;

            m_LoadProgressBar = root.Q<ProgressBar>("LoadProgressBar");
            m_StatusLabel = root.Q<Label>("StatusLabel");
            m_TypeFilterMenu = root.Q<ToolbarMenu>("TypeFilterMenu");
            m_EmptyStateContainer = root.Q<VisualElement>("EmptyStateContainer");
            m_EmptyStateLabel = root.Q<Label>("EmptyStateLabel");
            m_MainContent = root.Q<VisualElement>("MainContent");
            m_PieImgui = root.Q<IMGUIContainer>("PieImgui");
            m_BlockViewImgui = root.Q<IMGUIContainer>("BlockViewImgui");
            m_HoverInfoLabel = root.Q<Label>("HoverInfoLabel");
            m_HoverInfoLabel.style.color = EditorGUIUtility.isProSkin ? new Color(0.92f, 0.92f, 0.92f) : new Color(0.08f, 0.08f, 0.08f);
            m_FooterLabel = root.Q<Label>("FooterLabel");

            m_PieImgui.onGUIHandler = DrawPieAndLegend;
            m_BlockViewImgui.onGUIHandler = DrawBlockViewGUI;

            root.Q<ToolbarSearchField>("SearchField").RegisterValueChangedCallback(evt =>
            {
                m_SearchText = evt.newValue ?? "";
                ApplyFilter();
            });

            m_ListView = BuildListView();
            m_ListView.style.flexGrow = 1;
            root.Q<VisualElement>("ListViewContainer").Add(m_ListView);

            UpdateLoadingUI();
            ApplyFilter();
        }

        static string DefaultPlatformForActiveBuildTarget()
        {
            return EditorUserBuildSettings.activeBuildTarget switch
            {
                BuildTarget.Android => "Android",
                BuildTarget.iOS => "iPhone",
                BuildTarget.WebGL => "WebGL",
                _ => "Standalone",
            };
        }

        void OnTargetChanged(ChangeEvent<UnityEngine.Object> evt)
        {
            var obj = evt.newValue;
            if (obj == null)
            {
                EditorApplication.update -= ProcessChunk;
                m_IsAnalyzing = false;
                m_RootAssetPath = null;
                m_AllEntries = new List<AssetDependencyEntry>();
                m_SizeBreakdown = null;
                UpdateLoadingUI();
                ApplyFilter();
                return;
            }

            var path = AssetDatabase.GetAssetPath(obj);
            bool isScene = path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase);
            bool isPrefab = path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);

            if (!isScene && !isPrefab)
            {
                m_StatusLabel.text = "Pick a Scene (.unity) or Prefab (.prefab) asset.";
                m_TargetField.SetValueWithoutNotify(null);
                return;
            }

            StartAnalysis(path);
        }

        void StartAnalysis(string rootAssetPath)
        {
            m_RootAssetPath = rootAssetPath;
            m_AllEntries = new List<AssetDependencyEntry>();
            m_SizeBreakdown = null;
            m_TypeColors = new Dictionary<string, Color>();
            m_SelectedTypes.Clear();
            RebuildTypeFilterMenu();

            m_PendingPaths = DependencySizeAnalyzer.GetDependencyPaths(rootAssetPath);
            m_PendingIndex = 0;
            m_CancelRequested = false;
            m_IsAnalyzing = true;

            UpdateLoadingUI();
            m_StatusLabel.text = $"Analyzing {Path.GetFileNameWithoutExtension(rootAssetPath)}...";
            m_LoadProgressBar.value = 0;
            m_LoadProgressBar.title = $"0 / {m_PendingPaths.Length}";

            ApplyFilter();

            EditorApplication.update -= ProcessChunk;
            EditorApplication.update += ProcessChunk;
        }

        void ProcessChunk()
        {
            if (m_CancelRequested)
            {
                FinishAnalysis(cancelled: true);
                return;
            }

            int end = Mathf.Min(m_PendingIndex + ChunkSize, m_PendingPaths.Length);
            for (int i = m_PendingIndex; i < end; i++)
            {
                var entry = SafeEstimate(m_PendingPaths[i]);
                if (entry != null)
                    m_AllEntries.Add(entry);
            }
            m_PendingIndex = end;

            RebuildTypeColors();
            m_SizeBreakdown = BuildSizeBreakdown();
            RebuildTypeFilterMenu();
            ApplyFilter();
            m_PieImgui.MarkDirtyRepaint();
            m_BlockViewImgui.MarkDirtyRepaint();

            m_LoadProgressBar.value = m_PendingPaths.Length > 0 ? (float)m_PendingIndex / m_PendingPaths.Length * 100f : 100f;
            m_LoadProgressBar.title = $"{m_PendingIndex} / {m_PendingPaths.Length}";

            if (m_PendingIndex >= m_PendingPaths.Length)
                FinishAnalysis(cancelled: false);
        }

        AssetDependencyEntry SafeEstimate(string path)
        {
            try
            {
                return DependencySizeAnalyzer.EstimateEntry(path, m_Platform);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Bundle Analyzer: failed to estimate '{path}': {e.Message}");
                return null;
            }
        }

        void FinishAnalysis(bool cancelled)
        {
            EditorApplication.update -= ProcessChunk;
            m_IsAnalyzing = false;
            UpdateLoadingUI();

            long total = m_AllEntries.Sum(e => e.EstimatedSizeBytes);
            long roughFinal = (long)(total * ArchiveCompressionFactor);
            m_StatusLabel.text = $"{Path.GetFileNameWithoutExtension(m_RootAssetPath)} — {m_AllEntries.Count} assets" +
                (cancelled ? " (cancelled)" : "") +
                $", ~{new AssetDependencyEntry { EstimatedSizeBytes = roughFinal }.SizeDisplay} estimated final bundle size";
            m_StatusLabel.tooltip = "Per-asset sizes (and the list/pie/block view) show each asset's estimated compressed size " +
                "from its own per-platform import settings, before archive packing.\n\n" +
                $"The final bundle size above additionally applies a rough ×{ArchiveCompressionFactor:0.00} correction for the " +
                "LZ4/LZMA compression Unity applies to the whole archive on top of that - calibrated from comparing this " +
                "estimate against real Windows/Android builds, but that ratio is content-dependent and will vary by project. " +
                $"Sum of per-asset estimates before that correction: {new AssetDependencyEntry { EstimatedSizeBytes = total }.SizeDisplay}.";

            RebuildTypeFilterMenu();
            ApplyFilter();
        }

        void UpdateLoadingUI()
        {
            m_LoadProgressBar.style.display = m_IsAnalyzing ? DisplayStyle.Flex : DisplayStyle.None;
            m_CancelButton.style.display = m_IsAnalyzing ? DisplayStyle.Flex : DisplayStyle.None;
            m_TargetField.SetEnabled(!m_IsAnalyzing);
            m_PlatformDropdown.SetEnabled(!m_IsAnalyzing);
        }

        MultiColumnListView BuildListView()
        {
            var columns = new Columns { reorderable = true, resizable = true };

            columns.Add(MakeNameColumn());
            columns.Add(MakeColumn("Type", 130, e => e.ClassName));
            columns.Add(MakeColumn("Path", 300, e => e.AssetPath));
            columns.Add(MakeSizeColumn());

            var listView = new MultiColumnListView(columns)
            {
                itemsSource = m_FilteredEntries,
                selectionType = SelectionType.Single,
                showAlternatingRowBackgrounds = AlternatingRowBackground.All,
                sortingMode = ColumnSortingMode.Default,
            };

            listView.columnSortingChanged += () => SortEntries(listView);
            listView.itemsChosen += OnItemsChosen;
            listView.selectionChanged += OnSelectionChanged;

            return listView;
        }

        Column MakeNameColumn()
        {
            var column = new Column
            {
                name = "Name",
                title = "Name",
                width = 260,
                sortable = true,
                makeCell = () =>
                {
                    var cell = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, paddingLeft = 4 } };
                    cell.AddToClassList("name-cell");
                    var icon = new Image();
                    icon.AddToClassList("name-cell-icon");
                    var label = new Label();
                    label.AddToClassList("name-cell-label");
                    cell.Add(icon);
                    cell.Add(label);
                    return cell;
                },
            };
            column.bindCell = (element, index) =>
            {
                if (index < 0 || index >= m_FilteredEntries.Count) return;
                var entry = m_FilteredEntries[index];
                var icon = element.Q<Image>();
                var label = element.Q<Label>();
                label.text = entry.Name;
                ApplySelectionTextColor(label, index);

                var asset = ResolveAsset(entry);
                icon.image = asset != null ? AssetDatabase.GetCachedIcon(entry.AssetPath) : null;
            };
            return column;
        }

        // MultiColumnListView's built-in selected-row text-color handling doesn't reliably
        // reach labels nested inside a custom cell hierarchy (like the Name column's icon+label
        // row), so cell text can end up unreadable against the selection highlight. Fix it
        // explicitly instead of relying on Unity's automatic behavior.
        void ApplySelectionTextColor(Label label, int index)
        {
            bool selected = m_ListView.selectedIndices.Contains(index);
            label.style.color = selected ? new StyleColor(Color.white) : new StyleColor(StyleKeyword.Null);
        }

        Column MakeColumn(string title, int width, Func<AssetDependencyEntry, string> getText)
        {
            var column = new Column { name = title, title = title, width = width, sortable = true, makeCell = () => new Label { style = { unityTextAlign = TextAnchor.MiddleLeft, paddingLeft = 4 } } };
            column.bindCell = (element, index) =>
            {
                if (index < 0 || index >= m_FilteredEntries.Count) return;
                var label = (Label)element;
                label.text = getText(m_FilteredEntries[index]) ?? "";
                ApplySelectionTextColor(label, index);
            };
            return column;
        }

        Column MakeSizeColumn()
        {
            var column = new Column
            {
                name = "Size",
                title = "Size",
                width = 110,
                sortable = true,
                makeCell = () => new Label { style = { unityTextAlign = TextAnchor.MiddleRight, paddingRight = 6 } },
            };
            column.bindCell = (element, index) =>
            {
                if (index < 0 || index >= m_FilteredEntries.Count) return;
                var label = (Label)element;
                label.text = m_FilteredEntries[index].SizeDisplay;
                ApplySelectionTextColor(label, index);
            };
            return column;
        }

        void SortEntries(MultiColumnListView listView)
        {
            var desc = listView.sortColumnDescriptions.FirstOrDefault();
            if (desc == null)
                return;

            Comparison<AssetDependencyEntry> comparison = desc.columnName switch
            {
                "Name" => (a, b) => string.CompareOrdinal(a.Name ?? "", b.Name ?? ""),
                "Type" => (a, b) => string.CompareOrdinal(a.ClassName ?? "", b.ClassName ?? ""),
                "Path" => (a, b) => string.CompareOrdinal(a.AssetPath ?? "", b.AssetPath ?? ""),
                "Size" => (a, b) => a.EstimatedSizeBytes.CompareTo(b.EstimatedSizeBytes),
                _ => null
            };

            if (comparison == null)
                return;

            if (desc.direction == SortDirection.Descending)
            {
                var asc = comparison;
                comparison = (a, b) => -asc(a, b);
            }

            m_FilteredEntries.Sort(comparison);
            listView.RefreshItems();
        }

        void OnItemsChosen(IEnumerable<object> items)
        {
            var entry = items.OfType<AssetDependencyEntry>().FirstOrDefault();
            if (entry != null)
                PingEntry(entry);
        }

        void OnSelectionChanged(IEnumerable<object> items)
        {
            var entry = items.OfType<AssetDependencyEntry>().FirstOrDefault();
            if (entry != null)
                PingEntry(entry);

            m_ListView.RefreshItems(); // rebind visible rows so selection text-color updates immediately
        }

        UnityEngine.Object ResolveAsset(AssetDependencyEntry entry)
        {
            if (entry.ResolvedAsset == null)
                entry.ResolvedAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(entry.AssetPath);
            return entry.ResolvedAsset;
        }

        void PingEntry(AssetDependencyEntry entry)
        {
            var asset = ResolveAsset(entry);
            if (asset == null)
            {
                m_StatusLabel.text = $"Couldn't load asset at '{entry.AssetPath}'.";
                return;
            }

            EditorGUIUtility.PingObject(asset);
            Selection.activeObject = asset;
        }

        void RebuildTypeColors()
        {
            var types = m_AllEntries.Select(e => e.ClassName).Distinct().OrderBy(t => t).ToList();
            m_TypeColors = new Dictionary<string, Color>();
            for (int i = 0; i < types.Count; i++)
                m_TypeColors[types[i]] = Color.HSVToRGB(i / (float)Mathf.Max(types.Count, 1), 0.48f, 0.8f);
        }

        List<(string Label, long Bytes, Color Color)> BuildSizeBreakdown()
        {
            var byType = m_AllEntries
                .GroupBy(e => e.ClassName)
                .Select(g => (Label: g.Key, Bytes: g.Sum(e => e.EstimatedSizeBytes)))
                .OrderByDescending(t => t.Bytes)
                .ToList();

            const int maxSlices = 7;
            var top = byType.Take(maxSlices).ToList();
            var otherBytes = byType.Skip(maxSlices).Sum(t => t.Bytes);

            var result = top.Select(t => (t.Label, t.Bytes, m_TypeColors.TryGetValue(t.Label, out var c) ? c : OtherColor)).ToList();
            if (otherBytes > 0)
                result.Add(("Other", otherBytes, OtherColor));
            return result;
        }

        void DrawPieAndLegend()
        {
            if (m_SizeBreakdown == null || m_SizeBreakdown.Count == 0)
            {
                GUILayout.Label(m_IsAnalyzing ? "Scanning…" : "No data loaded.", EditorStyles.miniLabel);
                return;
            }

            long total = m_SizeBreakdown.Sum(s => s.Bytes);

            var rect = GUILayoutUtility.GetRect(280, 200);
            DrawPieChart(rect, total);

            GUILayout.Space(6);

            foreach (var slice in m_SizeBreakdown)
            {
                var row = GUILayoutUtility.GetRect(280, 20);
                DrawSwatch(new Rect(row.x, row.y + 3, 12, 12), slice.Color);

                var pct = total > 0 ? slice.Bytes / (float)total * 100f : 0f;
                var label = $"{slice.Label}  —  {new AssetDependencyEntry { EstimatedSizeBytes = slice.Bytes }.SizeDisplay} ({pct:0.#}%)";
                var labelRect = new Rect(row.x + 18, row.y, row.width - 18, row.height);

                if (GUI.Button(labelRect, label, EditorStyles.label) && slice.Label != "Other")
                    SelectSingleType(slice.Label);
            }
        }

        // Approximates the Editor's actual chrome background so grout lines/gaps read as a
        // real gap rather than a stray line, regardless of light/dark theme.
        static readonly Color OtherColor = new(0.62f, 0.62f, 0.62f);

        static Color WindowBackgroundColor => EditorGUIUtility.isProSkin
            ? new Color(0.22f, 0.22f, 0.22f)
            : new Color(0.78f, 0.78f, 0.78f);

        static void DrawSwatch(Rect r, Color color)
        {
            EditorGUI.DrawRect(r, WindowBackgroundColor);
            EditorGUI.DrawRect(new Rect(r.x + 1, r.y + 1, r.width - 2, r.height - 2), color);
        }

        static Color ReadableTextColor(Color background)
        {
            float luminance = 0.299f * background.r + 0.587f * background.g + 0.114f * background.b;
            return luminance > 0.6f ? Color.black : Color.white;
        }

        void DrawPieChart(Rect rect, long total)
        {
            if (total <= 0) return;

            var center = new Vector3(rect.x + rect.width * 0.5f, rect.y + rect.height * 0.5f, 0);
            float radius = Mathf.Min(rect.width, rect.height) * 0.45f;

            Handles.BeginGUI();

            float startAngle = 0f;
            foreach (var slice in m_SizeBreakdown)
            {
                float sweep = 360f * (slice.Bytes / (float)total);
                Handles.color = slice.Color;
                Handles.DrawSolidArc(center, Vector3.forward, Quaternion.Euler(0, 0, startAngle) * Vector3.up, sweep, radius);
                startAngle += sweep;
            }

            // Grout lines between wedges + an outer ring, drawn on top of the fills so
            // adjacent same-ish-hued slices stay visually distinct.
            Handles.color = WindowBackgroundColor;
            startAngle = 0f;
            foreach (var slice in m_SizeBreakdown)
            {
                var dir = Quaternion.Euler(0, 0, startAngle) * Vector3.up;
                Handles.DrawAAPolyLine(2.5f, center, center + dir * radius);
                startAngle += 360f * (slice.Bytes / (float)total);
            }
            Handles.DrawAAPolyLine(2.5f, center, center + Vector3.up * radius);
            Handles.DrawWireDisc(center, Vector3.forward, radius);

            Handles.EndGUI();
        }

        void DrawBlockViewGUI()
        {
            if (m_FilteredEntries == null || m_FilteredEntries.Count == 0)
            {
                GUILayout.Label(m_IsAnalyzing ? "Scanning…" : "No data.", EditorStyles.miniLabel);
                return;
            }

            m_BlockLabelStyle ??= new GUIStyle(EditorStyles.whiteMiniLabel) { clipping = TextClipping.Clip, fontSize = 11 };

            var rect = GUILayoutUtility.GetRect(280, 260, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            var blocks = BlockView.Layout(rect, m_FilteredEntries, e => (double)e.EstimatedSizeBytes);

            var evt = Event.current;
            AssetDependencyEntry hovered = null;
            const float gap = 1.5f;

            foreach (var block in blocks)
            {
                var color = m_TypeColors.TryGetValue(block.Item.ClassName, out var c) ? c : OtherColor;
                var inset = new Rect(
                    block.Rect.x + gap, block.Rect.y + gap,
                    Mathf.Max(0, block.Rect.width - gap * 2), Mathf.Max(0, block.Rect.height - gap * 2));

                EditorGUI.DrawRect(inset, color);

                if (inset.width > 36 && inset.height > 14)
                {
                    m_BlockLabelStyle.normal.textColor = ReadableTextColor(color);
                    GUI.Label(new Rect(inset.x + 3, inset.y + 1, inset.width - 6, inset.height - 2), block.Item.Name, m_BlockLabelStyle);
                }

                // Hover/click hit-test against the full (pre-gap) rect - a more forgiving
                // target than requiring a pixel-precise click inside the inset.
                if (block.Rect.Contains(evt.mousePosition))
                    hovered = block.Item;

                if (evt.type == EventType.MouseDown && block.Rect.Contains(evt.mousePosition))
                {
                    PingEntry(block.Item);
                    evt.Use();
                }
            }

            if (evt.type == EventType.Repaint)
                m_HoverInfoLabel.text = hovered != null ? $"{hovered.Name}  —  {hovered.ClassName}, {hovered.SizeDisplay}" : "";

            if (evt.type == EventType.MouseMove)
                m_BlockViewImgui.MarkDirtyRepaint();
        }

        void SelectSingleType(string type)
        {
            m_SelectedTypes.Clear();
            m_SelectedTypes.Add(type);
            m_TypeFilterMenu.text = $"Type: {type}";
            ApplyFilter();
        }

        void RebuildTypeFilterMenu()
        {
            m_TypeFilterMenu.menu.MenuItems().Clear();

            var types = m_AllEntries.Select(e => e.ClassName).Distinct().OrderBy(t => t).ToList();

            m_TypeFilterMenu.menu.AppendAction("All", _ =>
            {
                m_SelectedTypes.Clear();
                m_TypeFilterMenu.text = "Type: All";
                ApplyFilter();
            }, _ => m_SelectedTypes.Count == 0 ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);

            m_TypeFilterMenu.menu.AppendSeparator();

            foreach (var type in types)
            {
                m_TypeFilterMenu.menu.AppendAction(type, _ =>
                {
                    if (!m_SelectedTypes.Add(type))
                        m_SelectedTypes.Remove(type);

                    m_TypeFilterMenu.text = m_SelectedTypes.Count == 0
                        ? "Type: All"
                        : $"Type: {m_SelectedTypes.Count} selected";

                    ApplyFilter();
                }, _ => m_SelectedTypes.Contains(type) ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            }

            if (m_SelectedTypes.Count == 0)
                m_TypeFilterMenu.text = "Type: All";
        }

        void ApplyFilter()
        {
            IEnumerable<AssetDependencyEntry> query = m_AllEntries;

            if (m_SelectedTypes.Count > 0)
                query = query.Where(e => m_SelectedTypes.Contains(e.ClassName));

            if (!string.IsNullOrEmpty(m_SearchText))
                query = query.Where(e => (e.Name ?? "").IndexOf(m_SearchText, StringComparison.OrdinalIgnoreCase) >= 0);

            m_FilteredEntries = query.ToList();

            if (m_AllEntries.Count == 0)
            {
                if (m_IsAnalyzing || m_RootAssetPath == null)
                {
                    // Still scanning (nothing found yet) or nothing picked - not an error state.
                    m_EmptyStateContainer.style.display = m_RootAssetPath == null ? DisplayStyle.Flex : DisplayStyle.None;
                    m_MainContent.style.display = m_RootAssetPath == null ? DisplayStyle.None : DisplayStyle.Flex;
                    if (m_RootAssetPath != null)
                    {
                        m_ListView.itemsSource = m_FilteredEntries;
                        m_ListView.RefreshItems();
                    }
                }
                else
                {
                    ShowEmptyState("This asset has no dependencies to report.");
                }
            }
            else if (m_FilteredEntries.Count == 0)
            {
                ShowEmptyState("No assets match the current filter/search.");
            }
            else
            {
                m_EmptyStateContainer.style.display = DisplayStyle.None;
                m_MainContent.style.display = DisplayStyle.Flex;
                m_ListView.itemsSource = m_FilteredEntries;
                m_ListView.RefreshItems();
            }

            long totalSize = m_FilteredEntries.Sum(e => e.EstimatedSizeBytes);
            m_FooterLabel.text = $"{m_FilteredEntries.Count} asset(s) shown, ~{new AssetDependencyEntry { EstimatedSizeBytes = totalSize }.SizeDisplay} estimated" +
                "  ·  click a row to locate it in the Project window";
        }

        void ShowEmptyState(string message)
        {
            m_EmptyStateLabel.text = message;
            m_EmptyStateContainer.style.display = DisplayStyle.Flex;
            m_MainContent.style.display = DisplayStyle.None;
        }
    }
}

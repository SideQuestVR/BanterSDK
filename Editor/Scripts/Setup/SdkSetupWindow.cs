using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BS.SDKEditor.Setup
{
    /// <summary>
    /// The Creator SDK's Setup panel: buttons for the Builder and the documentation, and the project
    /// setup checklist, where every item has its own fix button and Fix All runs the Required and Recommended
    /// ones. Hovering an item explains why it's needed, what goes wrong without it and what its fix changes.
    /// <see cref="ProjectSetup"/> opens it the first time the SDK loads in a project.
    /// </summary>
    public sealed class SdkSetupWindow : EditorWindow
    {
        const string StyleSheetPath = "Packages/com.sidequest.creator-sdk/Editor/Scripts/Setup/SdkSetupWindow.uss";
        // The list's look, shared with the Builder's pre-build checklist.
        public const string ChecklistStyleSheetPath = "Packages/com.sidequest.creator-sdk/Editor/Scripts/Setup/Checklist.uss";
        // Used when the package doesn't name its documentation (package.json "documentationUrl").
        const string FallbackDocumentationUrl = "https://greenfield-registry.sdq.st/-/web/detail/com.sidequest.creator-sdk";
        static readonly Vector2 DefaultSize = new Vector2(580, 760);

        VisualElement _list;
        Label _summary;
        Button _fixAll;
        VisualElement _restartBanner;
        bool _refreshQueued;
        IVisualElementScheduledItem _poll;

#if !GREENFIELD_PROJECT
        [MenuItem("Creator SDK/Setup", false, 0)]
#endif
        public static void Open()
        {
            var isNew = !HasOpenInstances<SdkSetupWindow>();
            var window = GetWindow<SdkSetupWindow>(false, "Setup", true);
            window.titleContent = new GUIContent("Setup", Resources.Load<Texture2D>("UI/Images/altspace-window-icon"));
            window.minSize = new Vector2(440, 480);
            if (isNew)
                window.position = FirstPosition(window.minSize);
        }

        // Centred on Unity's main window, and shrunk to fit inside it: Unity doesn't keep a floating window on
        // screen, and on a small or scaled-up laptop screen the full size would put its title bar off the top.
        static Rect FirstPosition(Vector2 minSize)
        {
            var main = EditorGUIUtility.GetMainWindowPosition();
            var size = new Vector2(
                Mathf.Max(minSize.x, Mathf.Min(DefaultSize.x, main.width - 40)),
                Mathf.Max(minSize.y, Mathf.Min(DefaultSize.y, main.height - 80)));
            // At least 40 points below the main window's top, which leaves room for this window's title bar.
            return new Rect(main.x + Mathf.Max(0, (main.width - size.x) / 2), main.y + Mathf.Max(40, (main.height - size.y) / 2),
                size.x, size.y);
        }

        public static string DocumentationUrl
        {
            get
            {
                var url = PackageManagerUtility.documentationUrl;
                return string.IsNullOrEmpty(url) ? FallbackDocumentationUrl : url;
            }
        }

        /// <summary>
        /// Opens the documentation in Ora's web browser panel inside Unity, docked next to the Game view, or in the system
        /// browser when this project's Ora has no such panel. Pressing it again brings the same panel back.
        /// </summary>
        public static void OpenDocumentation()
        {
            if (!OpenInEditorBrowser(DocumentationUrl, "creator-sdk-docs"))
                Application.OpenURL(DocumentationUrl);
        }

        // SideQuest.Ora.OraEditorBrowser, Ora's way in for other assemblies. Found by name, not referenced: older Ora
        // builds (the registry's 0.0.2, and 0.1.0 before the browser panel) don't have it, and the version number doesn't
        // say which one this is. False when it's missing or its editor half hasn't loaded yet.
        static bool OpenInEditorBrowser(string url, string key)
        {
            var open = Type.GetType("SideQuest.Ora.OraEditorBrowser, SideQuest.Ora")
                ?.GetMethod("Open", new[] { typeof(string), typeof(string), typeof(bool) });
            if (open == null)
                return false;
            try
            {
                return open.Invoke(null, new object[] { url, key, false }) is bool opened && opened;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Creator SDK] Ora's browser panel couldn't open the documentation, so it opens in your browser: " +
                                 (e.InnerException ?? e).Message);
                return false;
            }
        }

        void OnEnable()
        {
            // Here as well as in Open: a window restored with the layout keeps whatever title it was saved with.
            titleContent = new GUIContent("Setup", Resources.Load<Texture2D>("UI/Images/altspace-window-icon"));
            ProjectSetup.Changed += QueueRefresh;
            EditorApplication.projectChanged += QueueRefresh;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        void OnDisable()
        {
            ProjectSetup.Changed -= QueueRefresh;
            EditorApplication.projectChanged -= QueueRefresh;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.update -= RefreshOnce;
            _refreshQueued = false;
        }

        void OnFocus() => QueueRefresh();

        void OnPlayModeChanged(PlayModeStateChange change) => QueueRefresh();

        void CreateGUI()
        {
            var root = rootVisualElement;
            foreach (var path in new[] { ChecklistStyleSheetPath, StyleSheetPath })
            {
                var styles = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
                if (styles != null)
                    root.styleSheets.Add(styles);
            }
            root.AddToClassList("sdk-setup");

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("sdk-setup__scroll");
            var content = Element("sdk-setup__content");
            content.Add(BuildHeader());
            content.Add(BuildActions());
            content.Add(BuildSetupSection());
            content.Add(BuildGettingStarted());
            scroll.Add(content);
            root.Add(scroll);
            root.Add(BuildFooter());

            Refresh();
        }

        // -- Layout -------------------------------------------------------------------

        VisualElement BuildHeader()
        {
            var header = Element("sdk-setup__header");
            var top = Element("sdk-setup__header-top");
            top.Add(Element("sdk-setup__logo"));
            var titles = Element("sdk-setup__titles");
            titles.Add(Text("Creator SDK", "sdk-setup__title"));
            var version = PackageManagerUtility.currentVersion;
            if (!string.IsNullOrEmpty(version))
                titles.Add(Text("Version " + version, "sdk-setup__version"));
            top.Add(titles);
            header.Add(top);
            header.Add(Text("Build multiplayer worlds in Unity, then publish your world from the Builder. Start by finishing the " +
                            "project setup below.", "sdk-setup__intro"));
            return header;
        }

        VisualElement BuildActions()
        {
            var actions = Element("sdk-setup__actions");
            var builder = ActionCard("sdk-setup__action-icon--builder", "Open Builder",
                "Sign in, pick your world, then build and publish it.", BuilderWindow.ShowMainWindow);
            builder.AddToClassList("sdk-setup__action--primary");
            builder.AddToClassList("sdk-setup__action--first");
            actions.Add(builder);
            actions.Add(ActionCard("sdk-setup__action-icon--docs", "Documentation",
                "Getting started, every component and the scripting API.", OpenDocumentation));
            return actions;
        }

        static Button ActionCard(string iconClass, string title, string caption, Action onClick)
        {
            var card = new Button(onClick);
            card.AddToClassList("sdk-setup__action");
            var icon = Element("sdk-setup__action-icon");
            icon.AddToClassList(iconClass);
            card.Add(icon);
            var text = Element("sdk-setup__action-text");
            text.Add(Text(title, "sdk-setup__action-title"));
            text.Add(Text(caption, "sdk-setup__action-caption"));
            card.Add(text);
            return card;
        }

        VisualElement BuildSetupSection()
        {
            var section = Element("sdk-setup__section");
            var heading = Element("checklist__heading");
            heading.Add(Text("Project setup", "checklist__title"));
            heading.Add(Element("checklist__spacer"));
            var recheck = new Button(Refresh) { text = "Re-check" };
            recheck.AddToClassList("checklist__recheck");
            heading.Add(recheck);
            _fixAll = new Button(FixAll);
            _fixAll.AddToClassList("checklist__fix-all");
            heading.Add(_fixAll);
            section.Add(heading);

            _summary = Text("", "checklist__summary");
            section.Add(_summary);

            _restartBanner = Element("sdk-setup__banner");
            _restartBanner.Add(Text("Restart Unity to finish: a change you made only takes effect after a restart.", "sdk-setup__banner-text"));
            var restart = new Button(ProjectSetup.RestartEditor) { text = "Restart Unity" };
            restart.AddToClassList("sdk-setup__banner-button");
            _restartBanner.Add(restart);
            section.Add(_restartBanner);

            _list = Element("checklist__list");
            section.Add(_list);
            section.Add(Text("Hover over an item to see why it's needed and what its fix changes. Fix All runs the Required and " +
                             "Recommended fixes; Optional ones only run from their own button.", "sdk-setup__hint"));
            return section;
        }

        // Closed at first; the view data key makes Unity remember whether it was left open.
        VisualElement BuildGettingStarted()
        {
            var foldout = new Foldout { text = "Getting started", value = false, viewDataKey = "sdk-setup-getting-started" };
            foldout.AddToClassList("sdk-setup__getting-started");
            foldout.Add(Text("Layers", "sdk-setup__topic-title"));
            foldout.Add(Text("Put your own objects on UserLayer1–15. Default, Water and UserLayer1–8 are what the player stands on, " +
                             "teleports to and grapples, except that the grapple ignores UserLayer1 and the player's body passes " +
                             "through UserLayer2–8. UserLayer9–15 are ignored by all three but still stop the body and hands. Use " +
                             "Grabbable for things to pick up and Invisible for things the player shouldn't see (they're still solid). " +
                             "CharacterColliders and CharacterHandColliders are the player's own body and hands, so keep your objects " +
                             "off them and off every layer above them. The documentation's Layers section has the full table.",
                "sdk-setup__topic-text"));
            return foldout;
        }

        VisualElement BuildFooter()
        {
            var footer = Element("sdk-setup__footer");
            var startup = new Toggle { text = "Show this panel when Unity starts", value = ProjectSetup.ShowAtStartup };
            startup.AddToClassList("sdk-setup__startup");
            startup.RegisterValueChangedCallback(change => ProjectSetup.ShowAtStartup = change.newValue);
            footer.Add(startup);
            footer.Add(Text("Creator SDK > Setup opens it again.", "sdk-setup__hint"));
            return footer;
        }

        // -- Checklist ----------------------------------------------------------------

        // A fix runs inside its own button's click, so the list is rebuilt on the next editor tick rather than
        // mid-event. EditorApplication.update, not the panel's scheduler: that only runs while the window repaints,
        // so a fix made while the editor was in the background left the list stale.
        void QueueRefresh()
        {
            if (_list == null || _refreshQueued)
                return;
            _refreshQueued = true;
            EditorApplication.update += RefreshOnce;
        }

        void RefreshOnce()
        {
            // The click that focused this window (which queues a refresh) is still held down. Rebuilding the rows now
            // would swap its button for a new one, which never sees the press and ignores the release.
            if (PointerHeld)
                return;
            EditorApplication.update -= RefreshOnce;
            _refreshQueued = false;
            Refresh();
            Repaint();
        }

        bool PointerHeld => rootVisualElement.panel?.GetCapturingElement(PointerId.mousePointerId) != null;

        void Refresh()
        {
            if (_list == null)
                return;
            // Here, not once in CreateGUI: the editor theme can change while the window is open.
            rootVisualElement.EnableInClassList("sdk-setup--dark", EditorGUIUtility.isProSkin);
            rootVisualElement.EnableInClassList("sdk-setup--light", !EditorGUIUtility.isProSkin);
            var rows = ProjectSetup.Checks.Select(check => (check, status: ProjectSetup.Evaluate(check))).ToList();
            var busy = EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling;

            _list.Clear();
            for (var i = 0; i < rows.Count; i++)
            {
                var row = BuildRow(rows[i].check, rows[i].status, busy);
                if (i == rows.Count - 1)
                    row.AddToClassList("checklist-row--last");
                _list.Add(row);
            }

            var fixable = rows.Count(row => row.check.InFixAll && row.status.State == SetupState.NeedsFix);
            _fixAll.text = fixable > 0 ? $"Fix All ({fixable})" : "Fix All";
            _fixAll.SetEnabled(fixable > 0 && !busy);
            _summary.text = SummaryText(rows.Select(row => (row.check.Importance, row.status)).ToList(), busy);
            _restartBanner.style.display = ProjectSetup.AnyRestartPending ? DisplayStyle.Flex : DisplayStyle.None;

            // Background fixes (an import, a package install) and compiles finish on their own; look again until they
            // do. Play mode ending is an event of its own.
            var waiting = EditorApplication.isCompiling || rows.Any(row => row.status.State == SetupState.Working);
            if (waiting && _poll == null)
                _poll = rootVisualElement.schedule.Execute(() =>
                {
                    if (!PointerHeld)
                        Refresh();
                }).Every(1000);
            else if (!waiting && _poll != null)
            {
                _poll.Pause();
                _poll = null;
            }
        }

        static string SummaryText(List<(SetupImportance importance, SetupStatus status)> rows, bool busy)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return "Stop Play mode to change the project setup.";
            if (EditorApplication.isCompiling)
                return "Waiting for scripts to finish compiling...";
            // Optional items are offers, not problems: they keep their own row and button but don't count here.
            var open = rows.Where(row => row.status.NeedsAttention && row.importance != SetupImportance.Optional).ToList();
            if (open.Count == 0)
            {
                if (rows.Any(row => row.status.State == SetupState.Working))
                    return "Finishing up...";
                return rows.Any(row => row.status.State == SetupState.RestartPending) ? "Restart Unity to finish setting up." : "Everything's set up.";
            }
            var parts = new List<string>();
            foreach (var importance in new[] { SetupImportance.Required, SetupImportance.Recommended })
            {
                var count = open.Count(row => row.importance == importance);
                if (count > 0)
                    parts.Add($"{count} {importance.ToString().ToLowerInvariant()}");
            }
            return (open.Count == 1 ? "1 item needs" : $"{open.Count} items need") + " attention: " + string.Join(", ", parts) + ".";
        }

        // Unity's own console and test icons: drawn for this size, and in both skins.
        static Texture StatusIcon(SetupState state, SetupImportance importance)
        {
            switch (state)
            {
                case SetupState.Done: return EditorGUIUtility.IconContent("TestPassed").image;
                case SetupState.RestartPending:
                case SetupState.Working: return EditorGUIUtility.IconContent("Refresh").image;
                default:
                    return EditorGUIUtility.IconContent(importance == SetupImportance.Required ? "console.warnicon.sml" : "console.infoicon.sml").image;
            }
        }

        static VisualElement BuildRow(SetupCheck check, SetupStatus status, bool busy)
        {
            var row = Element("checklist-row");
            row.AddToClassList("checklist-row--" + StateClass(status.State));
            row.AddToClassList("checklist-row--" + check.Importance.ToString().ToLowerInvariant());
            row.tooltip = Tooltip(check);

            var icon = Element("checklist-row__icon");
            var image = StatusIcon(status.State, check.Importance);
            if (image != null)
                icon.style.backgroundImage = new StyleBackground((Texture2D)image);
            row.Add(icon);

            var body = Element("checklist-row__body");
            var titleLine = Element("checklist-row__title-line");
            titleLine.Add(Text(check.Title, "checklist-row__title"));
            var badge = Text(check.Importance.ToString(), "checklist-row__badge");
            badge.AddToClassList("checklist-row__badge--" + check.Importance.ToString().ToLowerInvariant());
            titleLine.Add(badge);
            body.Add(titleLine);
            if (!string.IsNullOrEmpty(status.Summary))
                body.Add(Text(status.Summary, "checklist-row__summary"));
            if (!string.IsNullOrEmpty(status.Details) && status.State != SetupState.Done)
                body.Add(Text(status.Details, "checklist-row__details"));
            row.Add(body);

            var action = ActionButton(check, status);
            if (action != null)
            {
                action.AddToClassList("checklist-row__action");
                action.SetEnabled(!busy);
                // The explanation is the row's; the button itself shows none.
                action.RegisterCallback<TooltipEvent>(evt =>
                {
                    evt.tooltip = null;
                    evt.rect = Rect.zero;
                    evt.StopImmediatePropagation();
                });
                row.Add(action);
            }
            return row;
        }

        static Button ActionButton(SetupCheck check, SetupStatus status)
        {
            switch (status.State)
            {
                case SetupState.NeedsFix:
                    return new Button(() => ProjectSetup.Fix(check)) { text = check.FixLabel };
                case SetupState.NeedsManualFix when !string.IsNullOrEmpty(check.ManualActionLabel):
                    return new Button(check.ManualAction) { text = check.ManualActionLabel };
                default:
                    return null;
            }
        }

        static string Tooltip(SetupCheck check)
        {
            var text = new StringBuilder();
            text.Append("Why it's needed\n").Append(check.Why);
            text.Append("\n\nWithout it\n").Append(check.WithoutIt);
            if (!string.IsNullOrEmpty(check.FixChanges))
                text.Append("\n\nWhat the fix changes\n").Append(check.FixChanges);
            return text.ToString();
        }

        static string StateClass(SetupState state)
        {
            switch (state)
            {
                case SetupState.Done: return "done";
                case SetupState.NeedsFix: return "needs-fix";
                case SetupState.NeedsManualFix: return "manual";
                case SetupState.RestartPending: return "restart";
                default: return "working";
            }
        }

        void FixAll()
        {
            var result = ProjectSetup.FixAll();
            if (result.Failed.Count > 0)
                Debug.LogWarning("[Creator SDK] Fix All couldn't finish: " + string.Join(", ", result.Failed.Select(check => check.Title)) +
                                 ". See the messages above, or fix those items on their own.");
            QueueRefresh();
        }

        // -- Helpers ------------------------------------------------------------------

        static VisualElement Element(string className)
        {
            var element = new VisualElement();
            element.AddToClassList(className);
            return element;
        }

        static Label Text(string text, string className)
        {
            var label = new Label(text);
            label.AddToClassList(className);
            return label;
        }
    }
}

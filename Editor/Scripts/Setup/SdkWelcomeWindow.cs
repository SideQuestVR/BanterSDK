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
    /// The Creator SDK's Welcome window: buttons for the World Builder and the documentation, and the project
    /// setup checklist, where every item has its own fix button and Fix All runs the Required and Recommended
    /// ones. Hovering an item explains why it's needed, what goes wrong without it and what its fix changes.
    /// <see cref="ProjectSetup"/> opens it the first time the SDK loads in a project.
    /// </summary>
    public sealed class SdkWelcomeWindow : EditorWindow
    {
        const string StyleSheetPath = "Packages/com.sidequest.creator-sdk/Editor/Scripts/Setup/SdkWelcomeWindow.uss";
        // Used when the package doesn't name its documentation (package.json "documentationUrl").
        const string FallbackDocumentationUrl = "https://github.com/SideQuestVR/BanterSDK/blob/feature/greenfield/README.md";
        static readonly Vector2 DefaultSize = new Vector2(580, 760);

        VisualElement _list;
        Label _summary;
        Button _fixAll;
        VisualElement _restartBanner;
        bool _refreshQueued;
        IVisualElementScheduledItem _poll;

#if !GREENFIELD_PROJECT
        [MenuItem("Altspace/Welcome", false, 0)]
#endif
        public static void Open()
        {
            var isNew = !HasOpenInstances<SdkWelcomeWindow>();
            var window = GetWindow<SdkWelcomeWindow>(false, "Welcome", true);
            window.titleContent = new GUIContent("Welcome", Resources.Load<Texture2D>("UI/Images/altspace-window-icon"));
            window.minSize = new Vector2(440, 480);
            if (isNew)
            {
                var main = EditorGUIUtility.GetMainWindowPosition();
                window.position = new Rect(main.x + (main.width - DefaultSize.x) / 2, main.y + (main.height - DefaultSize.y) / 2,
                    DefaultSize.x, DefaultSize.y);
            }
        }

        public static string DocumentationUrl
        {
            get
            {
                var url = PackageManagerUtility.documentationUrl;
                return string.IsNullOrEmpty(url) ? FallbackDocumentationUrl : url;
            }
        }

        void OnEnable()
        {
            ProjectSetup.Changed += QueueRefresh;
            EditorApplication.projectChanged += QueueRefresh;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        void OnDisable()
        {
            ProjectSetup.Changed -= QueueRefresh;
            EditorApplication.projectChanged -= QueueRefresh;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        }

        void OnFocus() => QueueRefresh();

        void OnPlayModeChanged(PlayModeStateChange change) => QueueRefresh();

        void CreateGUI()
        {
            var root = rootVisualElement;
            var styles = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            if (styles != null)
                root.styleSheets.Add(styles);
            root.AddToClassList("sdk-welcome");
            root.AddToClassList(EditorGUIUtility.isProSkin ? "sdk-welcome--dark" : "sdk-welcome--light");

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("sdk-welcome__scroll");
            var content = Element("sdk-welcome__content");
            content.Add(BuildHeader());
            content.Add(BuildActions());
            content.Add(BuildSetupSection());
            scroll.Add(content);
            root.Add(scroll);
            root.Add(BuildFooter());

            Refresh();
        }

        // -- Layout -------------------------------------------------------------------

        VisualElement BuildHeader()
        {
            var header = Element("sdk-welcome__header");
            var top = Element("sdk-welcome__header-top");
            top.Add(Element("sdk-welcome__logo"));
            var titles = Element("sdk-welcome__titles");
            titles.Add(Text("SideQuest Creator SDK", "sdk-welcome__title"));
            var version = PackageManagerUtility.currentVersion;
            if (!string.IsNullOrEmpty(version))
                titles.Add(Text("Version " + version, "sdk-welcome__version"));
            top.Add(titles);
            header.Add(top);
            header.Add(Text("Build multiplayer worlds in Unity and publish them to SideQuest. Start by finishing the project setup below, " +
                            "then open the World Builder.", "sdk-welcome__intro"));
            return header;
        }

        VisualElement BuildActions()
        {
            var actions = Element("sdk-welcome__actions");
            var builder = ActionCard("sdk-welcome__action-icon--builder", "Open World Builder",
                "Sign in, pick your world, then build and upload it.", BuilderWindow.ShowMainWindow);
            builder.AddToClassList("sdk-welcome__action--primary");
            builder.AddToClassList("sdk-welcome__action--first");
            actions.Add(builder);
            actions.Add(ActionCard("sdk-welcome__action-icon--docs", "Documentation",
                "Getting started, every component and the scripting API.", () => Application.OpenURL(DocumentationUrl)));
            return actions;
        }

        static Button ActionCard(string iconClass, string title, string caption, Action onClick)
        {
            var card = new Button(onClick);
            card.AddToClassList("sdk-welcome__action");
            var icon = Element("sdk-welcome__action-icon");
            icon.AddToClassList(iconClass);
            card.Add(icon);
            var text = Element("sdk-welcome__action-text");
            text.Add(Text(title, "sdk-welcome__action-title"));
            text.Add(Text(caption, "sdk-welcome__action-caption"));
            card.Add(text);
            return card;
        }

        VisualElement BuildSetupSection()
        {
            var section = Element("sdk-welcome__section");
            var heading = Element("sdk-welcome__section-heading");
            heading.Add(Text("Project setup", "sdk-welcome__section-title"));
            heading.Add(Element("sdk-welcome__spacer"));
            var recheck = new Button(Refresh) { text = "Re-check" };
            recheck.AddToClassList("sdk-welcome__recheck");
            heading.Add(recheck);
            _fixAll = new Button(FixAll);
            _fixAll.AddToClassList("sdk-welcome__fix-all");
            heading.Add(_fixAll);
            section.Add(heading);

            _summary = Text("", "sdk-welcome__summary");
            section.Add(_summary);

            _restartBanner = Element("sdk-welcome__banner");
            _restartBanner.Add(Text("Restart Unity to finish: a change you made only takes effect after a restart.", "sdk-welcome__banner-text"));
            var restart = new Button(ProjectSetup.RestartEditor) { text = "Restart Unity" };
            restart.AddToClassList("sdk-welcome__banner-button");
            _restartBanner.Add(restart);
            section.Add(_restartBanner);

            _list = Element("sdk-welcome__list");
            section.Add(_list);
            section.Add(Text("Hover over an item to see why it's needed and what its fix changes. Fix All runs the Required and " +
                             "Recommended fixes; Optional ones only run from their own button.", "sdk-welcome__hint"));
            return section;
        }

        VisualElement BuildFooter()
        {
            var footer = Element("sdk-welcome__footer");
            var startup = new Toggle { text = "Show this window when Unity starts", value = ProjectSetup.ShowAtStartup };
            startup.AddToClassList("sdk-welcome__startup");
            startup.RegisterValueChangedCallback(change => ProjectSetup.ShowAtStartup = change.newValue);
            footer.Add(startup);
            footer.Add(Text("Altspace > Welcome opens it again.", "sdk-welcome__hint"));
            return footer;
        }

        // -- Checklist ----------------------------------------------------------------

        // Fixes and the events that follow them can arrive mid-click; rebuild the list on the next tick.
        void QueueRefresh()
        {
            if (_list == null || _refreshQueued)
                return;
            _refreshQueued = true;
            rootVisualElement.schedule.Execute(() =>
            {
                _refreshQueued = false;
                Refresh();
            });
        }

        void Refresh()
        {
            if (_list == null)
                return;
            var rows = ProjectSetup.Checks.Select(check => (check, status: ProjectSetup.Evaluate(check))).ToList();
            var busy = EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling;

            _list.Clear();
            for (var i = 0; i < rows.Count; i++)
            {
                var row = BuildRow(rows[i].check, rows[i].status, busy);
                if (i == rows.Count - 1)
                    row.AddToClassList("setup-row--last");
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
                _poll = rootVisualElement.schedule.Execute(Refresh).Every(1000);
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
            var open = rows.Where(row => row.status.NeedsAttention).ToList();
            if (open.Count == 0)
                return rows.Any(row => row.status.State == SetupState.Working) ? "Finishing up..." : "Everything's set up.";
            var parts = new List<string>();
            foreach (var importance in new[] { SetupImportance.Required, SetupImportance.Recommended, SetupImportance.Optional })
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
            var row = Element("setup-row");
            row.AddToClassList("setup-row--" + StateClass(status.State));
            row.AddToClassList("setup-row--" + check.Importance.ToString().ToLowerInvariant());
            row.tooltip = Tooltip(check);

            var icon = Element("setup-row__icon");
            var image = StatusIcon(status.State, check.Importance);
            if (image != null)
                icon.style.backgroundImage = new StyleBackground((Texture2D)image);
            row.Add(icon);

            var body = Element("setup-row__body");
            var titleLine = Element("setup-row__title-line");
            titleLine.Add(Text(check.Title, "setup-row__title"));
            var badge = Text(check.Importance.ToString(), "setup-row__badge");
            badge.AddToClassList("setup-row__badge--" + check.Importance.ToString().ToLowerInvariant());
            titleLine.Add(badge);
            body.Add(titleLine);
            if (!string.IsNullOrEmpty(status.Summary))
                body.Add(Text(status.Summary, "setup-row__summary"));
            if (!string.IsNullOrEmpty(status.Details) && status.State != SetupState.Done)
                body.Add(Text(status.Details, "setup-row__details"));
            row.Add(body);

            var action = ActionButton(check, status);
            if (action != null)
            {
                action.AddToClassList("setup-row__action");
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

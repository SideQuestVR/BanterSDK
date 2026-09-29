using System;
using System.Linq;
using BS.SDKEditor.BuildChecks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The builder's Checklist section: a summary, a button to run the checks, and a row per issue with
/// Select and Fix. Also fills the issue list in the build confirmation.
/// </summary>
internal sealed class BuildChecklistView
{
    readonly Label summary;
    readonly VisualElement rows;

    public event Action RunRequested;
    public event Action<BuildCheckIssue> SelectRequested;
    public event Action<BuildCheckIssue> FixRequested;

    public BuildChecklistView(VisualElement host)
    {
        var header = new VisualElement();
        header.AddToClassList("alt-checklist-header");
        var heading = new Label("CHECKLIST");
        heading.AddToClassList("alt-checklist-heading");
        summary = new Label();
        summary.AddToClassList("alt-muted");
        summary.AddToClassList("alt-checklist-summary");
        var run = new Button(() => RunRequested?.Invoke())
        {
            text = "RUN CHECKS",
            tooltip = "Check the scene for problems before building. The checks also run on every build.",
        };
        header.Add(heading);
        header.Add(summary);
        header.Add(run);

        rows = new VisualElement();
        rows.AddToClassList("alt-checklist-rows");
        host.Add(header);
        host.Add(rows);
        ShowNotRun();
    }

    public void ShowNotRun()
    {
        rows.Clear();
        summary.text = "Not run yet. The checks also run before every build.";
    }

    public void Show(BuildChecklistResult result)
    {
        rows.Clear();
        summary.text = result.Summary;
        foreach (var issue in result.Issues.OrderByDescending(issue => issue.Severity))
            rows.Add(Row(issue, SelectRequested, FixRequested));
    }

    /// <summary>The problems (not the notes) for the build confirmation, without buttons.</summary>
    public static void FillConfirm(VisualElement container, BuildChecklistResult result)
    {
        container.Clear();
        var heading = new Label("Checklist: " + result.Summary + ". Click an item for details.");
        heading.AddToClassList(result.Count(BuildCheckSeverity.Error) > 0 ? "alt-negative" : "alt-warning");
        heading.AddToClassList("alt-checklist-confirm-heading");
        container.Add(heading);

        var list = new ScrollView();
        list.AddToClassList("alt-checklist-confirm-list");
        foreach (var issue in result.Issues.Where(issue => issue.Severity != BuildCheckSeverity.Info).OrderByDescending(issue => issue.Severity))
            list.Add(Row(issue, null, null));
        container.Add(list);
    }

    static VisualElement Row(BuildCheckIssue issue, Action<BuildCheckIssue> select, Action<BuildCheckIssue> fix)
    {
        var container = new VisualElement();
        container.AddToClassList("alt-checklist-issue");

        var line = new VisualElement();
        line.AddToClassList("alt-checklist-row");
        var icon = new Image { image = Icon(issue.Severity) };
        icon.AddToClassList("alt-checklist-icon");
        var blocks = issue.Severity == BuildCheckSeverity.Error && !issue.Overridable;
        var title = new Label(issue.Title + (blocks ? " (blocks the build)" : ""));
        title.AddToClassList("alt-checklist-title");
        line.Add(icon);
        line.Add(title);
        container.Add(line);

        var detailsText = issue.Details ?? "";
        if (issue.Targets.Count > 0)
        {
            detailsText += (detailsText.Length > 0 ? "\n" : "") + string.Join("\n", issue.Targets.Take(8).Select(target => "  " + target.Label));
            if (issue.Targets.Count > 8)
                detailsText += $"\n  ...and {issue.Targets.Count - 8} more (see the Console)";
        }
        if (issue.Fix != null)
            detailsText += (detailsText.Length > 0 ? "\n" : "") + "Fix: " + issue.Fix.Label + ".";
        if (detailsText.Length > 0)
        {
            var details = new Label(detailsText);
            details.AddToClassList("alt-checklist-details");
            details.style.display = DisplayStyle.None;
            container.Add(details);
            title.tooltip = "Click for details";
            title.AddToClassList("alt-checklist-expandable");
            title.RegisterCallback<MouseUpEvent>(_ =>
                details.style.display = details.style.display == DisplayStyle.None ? DisplayStyle.Flex : DisplayStyle.None);
        }

        if (select != null && issue.Targets.Count > 0)
            line.Add(SmallButton("SELECT", "Select what this is about", () => select(issue)));
        if (fix != null && issue.Fix != null)
            line.Add(SmallButton("FIX", issue.Fix.Label, () => fix(issue)));
        return container;
    }

    static Button SmallButton(string text, string tooltip, Action clicked)
    {
        var button = new Button(clicked) { text = text, tooltip = tooltip };
        button.AddToClassList("alt-checklist-button");
        return button;
    }

    static Texture Icon(BuildCheckSeverity severity)
    {
        switch (severity)
        {
            case BuildCheckSeverity.Error: return EditorGUIUtility.IconContent("console.erroricon.sml").image;
            case BuildCheckSeverity.Warning: return EditorGUIUtility.IconContent("console.warnicon.sml").image;
            default: return EditorGUIUtility.IconContent("console.infoicon.sml").image;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using BS.SDKEditor.BuildChecks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The Builder's Checklist section, in the Setup panel's look (Editor/Scripts/Setup/Checklist.uss, which both load):
/// a heading with Re-check and Fix All, a summary, a row for each check that found something, with Select and Fix for
/// each problem, and one line for the checks that passed, which lists what each of them looked at when opened.
/// Hovering a row explains its check. Also fills the problem list in the build confirmation.
/// </summary>
internal sealed class BuildChecklistView
{
    static List<BuildCheck> s_Checks;
    static List<BuildCheck> Checks => s_Checks ?? (s_Checks = BuildChecklist.CreateChecks());

    readonly Label summary;
    readonly VisualElement list;
    readonly Button fixAll;
    // The run on screen: Show/Hide redraws it without checking again, so it keeps the run's time.
    BuildChecklistResult shown;
    DateTime checkedAt;
    bool logged;
    // The checks that passed are one line until opened: a list of names, all ticked, says little on its own.
    bool showPassed;

    public event Action RunRequested;
    public event Action FixAllRequested;
    public event Action<BuildCheckIssue> SelectRequested;
    public event Action<BuildCheckIssue> FixRequested;

    sealed class CheckRow
    {
        public string Title;
        public string Description;
        public List<BuildCheckIssue> Issues;
    }

    public BuildChecklistView(VisualElement host)
    {
        var heading = Element("checklist__heading");
        heading.Add(Text("Checklist", "checklist__title"));
        heading.Add(Element("checklist__spacer"));
        var recheck = new Button(() => RunRequested?.Invoke()) { text = "Re-check" };
        recheck.AddToClassList("checklist__recheck");
        heading.Add(recheck);
        fixAll = new Button(() => FixAllRequested?.Invoke());
        fixAll.AddToClassList("checklist__fix-all");
        heading.Add(fixAll);

        summary = Text("", "checklist__summary");
        list = Element("checklist__list");
        host.Add(heading);
        host.Add(summary);
        host.Add(list);
        ShowNotRun();
    }

    /// <summary>What Fix All runs: every fix that just changes the project. Ones that ask first (a restart) keep their own button.</summary>
    public static List<BuildCheckIssue> FixAllIssues(BuildChecklistResult result) =>
        result == null ? new List<BuildCheckIssue>() : result.Issues.Where(issue => issue.Fix != null && !issue.Fix.Interactive).ToList();

    /// <param name="reason">Why there's no result; by default, when the checks run.</param>
    public void ShowNotRun(string reason = null)
    {
        shown = null;
        list.Clear();
        list.style.display = DisplayStyle.None;
        summary.text = reason ?? "Not checked yet. The checks run when the Builder opens on a scene, and before every build.";
        SetFixAll(0);
    }

    /// <param name="logged">Whether the run also wrote its findings to the Console (the automatic runs don't).</param>
    public void Show(BuildChecklistResult result, bool logged)
    {
        shown = result;
        checkedAt = DateTime.Now;
        this.logged = logged;
        Render();
    }

    void Render()
    {
        list.Clear();
        list.style.display = DisplayStyle.Flex;
        var checks = Group(shown.Issues);
        var problems = WorstFirst(checks.Where(check => check.Issues.Count > 0));
        foreach (var check in problems)
            list.Add(Row(check, logged, SelectRequested, FixRequested));
        var passed = checks.Where(check => check.Issues.Count == 0).ToList();
        if (passed.Count > 0)
            list.Add(PassedRow(passed, all: problems.Count == 0));
        MarkLast(list);
        summary.text = SummaryText(shown, checkedAt);
        SetFixAll(FixAllIssues(shown).Count);
    }

    /// <summary>The problems (not the notes) for the build confirmation, without buttons.</summary>
    public static void FillConfirm(VisualElement container, BuildChecklistResult result)
    {
        container.Clear();
        var heading = new Label("Checklist: " + result.Summary + ".");
        heading.AddToClassList(result.Count(BuildCheckSeverity.Error) > 0 ? "alt-negative" : "alt-warning");
        heading.AddToClassList("alt-checklist-confirm-heading");
        container.Add(heading);

        var scroll = new ScrollView();
        scroll.AddToClassList("alt-checklist-confirm-list");
        var box = Element("checklist__list");
        var problems = Group(result.Issues.Where(issue => issue.Severity != BuildCheckSeverity.Info)).Where(check => check.Issues.Count > 0);
        // A build's own run, which logs.
        foreach (var check in WorstFirst(problems))
            box.Add(Row(check, true, null, null));
        MarkLast(box);
        scroll.Add(box);
        container.Add(scroll);
    }

    // Every check with what it found, in list order; issues from a check this list doesn't know still get a row.
    static List<CheckRow> Group(IEnumerable<BuildCheckIssue> issues)
    {
        var byCheck = issues.GroupBy(issue => issue.CheckId).ToDictionary(group => group.Key, group => group.ToList());
        var rows = new List<CheckRow>();
        foreach (var check in Checks)
        {
            rows.Add(new CheckRow
            {
                Title = check.Title,
                Description = check.Description,
                Issues = byCheck.TryGetValue(check.Id, out var found) ? found : new List<BuildCheckIssue>(),
            });
            byCheck.Remove(check.Id);
        }
        rows.AddRange(byCheck.Select(pair => new CheckRow { Title = pair.Key, Description = "", Issues = pair.Value }));
        return rows;
    }

    // Worst first; checks with the same severity keep their usual order (OrderBy is stable).
    static List<CheckRow> WorstFirst(IEnumerable<CheckRow> checks) =>
        checks.OrderByDescending(check => check.Issues.Max(issue => issue.Severity)).ToList();

    static void MarkLast(VisualElement container)
    {
        if (container.childCount > 0)
            container[container.childCount - 1].AddToClassList("checklist-row--last");
    }

    static VisualElement Row(CheckRow check, bool logged, Action<BuildCheckIssue> select, Action<BuildCheckIssue> fix)
    {
        var worst = check.Issues.Max(issue => issue.Severity);
        var row = Element("checklist-row");
        row.AddToClassList("checklist-row--needs-fix");
        row.tooltip = check.Description;
        row.Add(Icon(worst));

        var body = Element("checklist-row__body");
        var titleLine = Element("checklist-row__title-line");
        titleLine.Add(Text(check.Title, "checklist-row__title"));
        var blocks = check.Issues.Any(issue => issue.Severity == BuildCheckSeverity.Error && !issue.Overridable);
        var badge = Text(blocks ? "Blocks the build" : BadgeText(worst), "checklist-row__badge");
        badge.AddToClassList("checklist-row__badge--" + BadgeText(worst).ToLowerInvariant());
        titleLine.Add(badge);
        body.Add(titleLine);
        foreach (var issue in check.Issues.OrderByDescending(issue => issue.Severity))
            body.Add(Issue(issue, logged, select, fix));
        row.Add(body);
        return row;
    }

    // One line for every check that passed. Opened, it lists each one with what it looked at.
    VisualElement PassedRow(List<CheckRow> passed, bool all)
    {
        var row = Element("checklist-row");
        row.AddToClassList("checklist-row--done");
        row.AddToClassList("checklist-row--compact");
        row.Add(Icon(null));

        var body = Element("checklist-row__body");
        var titleLine = Element("checklist-row__title-line");
        var title = all
            ? (passed.Count == 1 ? "The check passed" : $"All {passed.Count} checks passed")
            : (passed.Count == 1 ? "1 other check passed" : $"{passed.Count} other checks passed");
        titleLine.Add(Text(title, "checklist-row__title"));
        titleLine.Add(Element("checklist__spacer"));
        titleLine.Add(SmallButton(showPassed ? "Hide" : "Show", () =>
        {
            showPassed = !showPassed;
            if (shown != null)
                Render();
        }));
        body.Add(titleLine);

        if (showPassed)
        {
            foreach (var check in passed)
            {
                var item = Element("checklist-row__passed");
                item.Add(Text(check.Title, "checklist-row__passed-name"));
                if (!string.IsNullOrEmpty(check.Description))
                    item.Add(Text(check.Description, "checklist-row__passed-description"));
                body.Add(item);
            }
        }
        row.Add(body);
        return row;
    }

    static VisualElement Issue(BuildCheckIssue issue, bool logged, Action<BuildCheckIssue> select, Action<BuildCheckIssue> fix)
    {
        var line = Element("checklist-row__issue");
        var text = Element("checklist-row__issue-text");
        text.Add(Text(issue.Title, "checklist-row__summary"));
        var details = DetailsText(issue, logged);
        if (details.Length > 0)
            text.Add(Text(details, "checklist-row__details"));
        line.Add(text);

        var actions = Element("checklist-row__actions");
        if (select != null && issue.Targets.Count > 0)
            actions.Add(SmallButton("Select", () => select(issue)));
        // A fix that asks first or opens something (Open Setup) says so on its button.
        if (fix != null && issue.Fix != null)
            actions.Add(SmallButton(issue.Fix.Interactive ? issue.Fix.Label : "Fix", () => fix(issue)));
        if (actions.childCount > 0)
            line.Add(actions);
        return line;
    }

    static string DetailsText(BuildCheckIssue issue, bool logged)
    {
        var lines = new List<string>();
        if (!string.IsNullOrEmpty(issue.Details))
            lines.Add(issue.Details);
        foreach (var target in issue.Targets.Take(5))
            lines.Add("• " + target.Label);
        if (issue.Targets.Count > 5)
            lines.Add($"...and {issue.Targets.Count - 5} more" + (logged ? " (see the Console)" : "; Re-check lists them all in the Console"));
        if (issue.Fix != null && !issue.Fix.Interactive)
            lines.Add("Fix: " + issue.Fix.Label + ".");
        return string.Join("\n", lines);
    }

    static string SummaryText(BuildChecklistResult result, DateTime time)
    {
        var checkedAt = $" Checked at {time:HH:mm}.";
        if (result.Issues.Count == 0)
            return "No problems found." + checkedAt;
        var blockers = result.Blockers.Count();
        return result.Summary + "." + (blockers == 0 ? "" : blockers == 1 ? " 1 blocks the build." : $" {blockers} block the build.") + checkedAt;
    }

    void SetFixAll(int fixable)
    {
        fixAll.text = fixable > 0 ? $"Fix All ({fixable})" : "Fix All";
        fixAll.SetEnabled(fixable > 0);
    }

    static string BadgeText(BuildCheckSeverity severity) =>
        severity == BuildCheckSeverity.Error ? "Error" : severity == BuildCheckSeverity.Warning ? "Warning" : "Note";

    // Unity's own icons, as the Setup panel uses: drawn for this size, and in both skins.
    static VisualElement Icon(BuildCheckSeverity? severity)
    {
        var icon = Element("checklist-row__icon");
        if (IconTexture(severity) is Texture2D image)
            icon.style.backgroundImage = new StyleBackground(image);
        return icon;
    }

    static Texture IconTexture(BuildCheckSeverity? severity)
    {
        switch (severity)
        {
            case null: return EditorGUIUtility.IconContent("TestPassed").image;
            case BuildCheckSeverity.Error: return EditorGUIUtility.IconContent("console.erroricon.sml").image;
            case BuildCheckSeverity.Warning: return EditorGUIUtility.IconContent("console.warnicon.sml").image;
            default: return EditorGUIUtility.IconContent("console.infoicon.sml").image;
        }
    }

    // The row's explanation belongs to the row; its buttons show none, as in the Setup panel.
    static Button SmallButton(string text, Action clicked)
    {
        var button = new Button(clicked) { text = text };
        button.AddToClassList("checklist-row__action");
        button.RegisterCallback<TooltipEvent>(evt =>
        {
            evt.tooltip = null;
            evt.rect = Rect.zero;
            evt.StopImmediatePropagation();
        });
        return button;
    }

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

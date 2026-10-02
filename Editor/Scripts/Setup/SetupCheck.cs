namespace BS.SDKEditor.Setup
{
    public enum SetupImportance
    {
        /// <summary>The SDK doesn't work properly without it. Included in Fix All.</summary>
        Required,
        /// <summary>Avoids a problem most creators hit sooner or later. Included in Fix All.</summary>
        Recommended,
        /// <summary>Only for some workflows. Never part of Fix All; fixed on its own button.</summary>
        Optional,
    }

    public enum SetupState
    {
        /// <summary>Nothing to do.</summary>
        Done,
        /// <summary>The item's fix button sorts it out.</summary>
        NeedsFix,
        /// <summary>Has to be done by hand (Unity Hub, another window...); the details say how.</summary>
        NeedsManualFix,
        /// <summary>Fixed this session; takes effect once Unity restarts.</summary>
        RestartPending,
        /// <summary>A fix is still running in the background (a package install, an import).</summary>
        Working,
    }

    /// <summary>What a <see cref="SetupCheck"/> found.</summary>
    public sealed class SetupStatus
    {
        public SetupState State { get; }
        /// <summary>One line: what's set, or what's wrong.</summary>
        public string Summary { get; }
        /// <summary>Optional specifics: the layers it renames, the assemblies it adds...</summary>
        public string Details { get; }

        SetupStatus(SetupState state, string summary, string details)
        {
            State = state;
            Summary = summary ?? "";
            Details = details;
        }

        public bool NeedsAttention => State == SetupState.NeedsFix || State == SetupState.NeedsManualFix;

        public static SetupStatus Done(string summary, string details = null) => new SetupStatus(SetupState.Done, summary, details);
        public static SetupStatus NeedsFix(string summary, string details = null) => new SetupStatus(SetupState.NeedsFix, summary, details);
        public static SetupStatus NeedsManualFix(string summary, string details = null) => new SetupStatus(SetupState.NeedsManualFix, summary, details);
        public static SetupStatus RestartPending(string summary) => new SetupStatus(SetupState.RestartPending, summary, null);
        public static SetupStatus Working(string summary) => new SetupStatus(SetupState.Working, summary, null);
    }

    /// <summary>
    /// One item in the Welcome window's project setup checklist. Subclasses with a parameterless constructor
    /// are found by type, in any editor assembly, so adding one is enough to list it. Checks only look:
    /// <see cref="Evaluate"/> never changes the project. <see cref="Fix"/> runs only when the creator asks,
    /// on its own button or through Fix All.
    /// </summary>
    public abstract class SetupCheck
    {
        /// <summary>Stable id, e.g. "sdk.layers-tags". Used in logs and session state.</summary>
        public abstract string Id { get; }

        public abstract string Title { get; }

        /// <summary>Why the SDK needs it. Shown in the item's tooltip.</summary>
        public abstract string Why { get; }

        /// <summary>What goes wrong without it. Shown in the item's tooltip.</summary>
        public abstract string WithoutIt { get; }

        /// <summary>What the fix changes in the project, side effects included. Shown in the item's tooltip.</summary>
        public virtual string FixChanges => null;

        public virtual SetupImportance Importance => SetupImportance.Required;

        /// <summary>Items are listed, and fixed by Fix All, in ascending order.</summary>
        public virtual int Order => 100;

        public virtual string FixLabel => "Fix";

        /// <summary>Whether a fix only takes effect after Unity restarts.</summary>
        public virtual bool FixNeedsRestart => false;

        /// <summary>Whether Fix All includes it (when it needs a fix).</summary>
        public virtual bool InFixAll => Importance != SetupImportance.Optional;

        public abstract SetupStatus Evaluate();

        /// <summary>
        /// Changes the project. Called only when <see cref="Evaluate"/> said <see cref="SetupState.NeedsFix"/>,
        /// never in batch mode by the window. Returns whether anything changed.
        /// </summary>
        public abstract bool Fix();

        /// <summary>For a manual item: a button that gets the creator there (opens a window, a page). Null for none.</summary>
        public virtual string ManualActionLabel => null;

        public virtual void ManualAction() { }
    }
}

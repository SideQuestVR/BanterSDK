using BS.SDKEditor.Setup;
using UnityEditor;

namespace BS.LocalMultiplayer.Editor
{
    // Local multiplayer's items in the Welcome window's setup checklist. Like the rest of this assembly they only
    // exist when local multiplayer can run (an Ora with the relay, no FlexaBody).

    /// <summary>The extra editor players are Multiplayer Play Mode virtual players.</summary>
    sealed class MppmSetupCheck : SetupCheck
    {
        public override string Id => "local-mp.mppm";
        public override string Title => "Multiplayer Play Mode";
        public override SetupImportance Importance => SetupImportance.Optional;
        public override int Order => 200;
        public override string FixLabel => "Install";
        public override string ManualActionLabel => "Open Package Manager";
        public override string Why =>
            "Local multiplayer testing runs up to three extra players next to this editor as Multiplayer Play Mode virtual " +
            "players, all in one room over a local relay.";
        public override string WithoutIt =>
            "You can only test as one player: no other avatars, and no synced objects, space state or one-shots coming from someone else.";
        public override string FixChanges =>
            $"Adds {LocalMpPackages.MppmName} {LocalMpPackages.MppmPinnedVersion} to the project's packages; Unity then recompiles. " +
            "Add players in Window > Multiplayer > Multiplayer Play Mode.";

        public override SetupStatus Evaluate()
        {
            if (LocalMpPackages.Installing)
                return SetupStatus.Working("Installing...");
            var mppm = LocalMpPackages.Find(LocalMpPackages.MppmName);
            if (mppm == null)
                return SetupStatus.NeedsFix("Not installed. Only needed to test with several players.");
            if (!LocalMpPackages.IsUsableMppm(mppm.version))
                return SetupStatus.NeedsManualFix($"Version {mppm.version} is installed; local multiplayer needs 2.0 or later",
                    "Update it in Window > Package Manager.");
            return SetupStatus.Done($"Version {mppm.version}. Add players in Window > Multiplayer > Multiplayer Play Mode.");
        }

        public override bool Fix()
        {
            LocalMpPackages.InstallFinished -= ProjectSetup.NotifyChanged;
            LocalMpPackages.InstallFinished += ProjectSetup.NotifyChanged;
            LocalMpPackages.InstallMppm();
            return true;
        }

        public override void ManualAction() => UnityEditor.PackageManager.UI.Window.Open(LocalMpPackages.MppmName);
    }

    /// <summary>Each virtual player is its own editor, which only keeps playing unfocused with Run In Background on.</summary>
    sealed class RunInBackgroundSetupCheck : SetupCheck
    {
        public override string Id => "local-mp.run-in-background";
        public override string Title => "Run In Background";
        public override SetupImportance Importance => SetupImportance.Recommended;
        public override int Order => 210;
        public override string FixLabel => "Turn on";
        public override string Why =>
            "Each Multiplayer Play Mode player is a separate editor, and only one window has focus at a time. The others keep playing " +
            "only with Run In Background on.";
        public override string WithoutIt => "Players whose window isn't focused stall, so everyone else sees their avatar freeze.";
        public override string FixChanges =>
            "Turns on Player Settings > Resolution and Presentation > Run In Background. It changes nothing in the spaces you build.";

        public override SetupStatus Evaluate()
        {
            var mppm = LocalMpPackages.Find(LocalMpPackages.MppmName);
            if (mppm == null || !LocalMpPackages.IsUsableMppm(mppm.version))
                return SetupStatus.Done("Only needed once Multiplayer Play Mode is installed.");
            return PlayerSettings.runInBackground ? SetupStatus.Done("On.") : SetupStatus.NeedsFix("It's off");
        }

        public override bool Fix()
        {
            if (PlayerSettings.runInBackground)
                return false;
            PlayerSettings.runInBackground = true;
            return true;
        }
    }
}

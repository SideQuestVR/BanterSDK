using UnityEditor;
using UnityEngine;

namespace BS.SDKEditor.Setup
{
    // The SDK's own rows in the Setup panel's Tools section. Local multiplayer adds its window from its own assembly.

    sealed class DesktopControllerSetupTool : SetupTool
    {
        public override string Title => "Desktop controller";
        public override string Description =>
            "In Play mode, fly the camera and grab, click and type with the mouse and keyboard. Switch it off to drive the " +
            "player another way. It takes effect the next time you press Play.";
        public override int Order => 20;
        public override bool? IsOn => !BSStarterUpper.AutoStartDisabled;
        public override void Run() => BSStarterUpper.ToggleAutoStart();
    }

    sealed class PageDeveloperToolsSetupTool : SetupTool
    {
        public override string Title => "Page developer tools";
        public override string Description => "Chrome's developer tools for your world's page: its console, network requests and elements.";
        public override int Order => 30;
        public override bool Available => EditorApplication.isPlaying;
        public override string UnavailableReason => "Press Play first.";

        public override void Run()
        {
            var link = BSScene.Instance().link;
            if (link == null)
            {
                Debug.LogWarning("[Creator SDK] The page hasn't started yet. Try again once the space has loaded.");
                return;
            }
            link.ToggleDevTools(true);
        }
    }

    sealed class RecoverSnippetsSetupTool : SetupTool
    {
        public override string Title => "Recover orphaned snippets";
        public override string Description =>
            "Lists the snippets in index.html that no scene or prefab uses, then gives each a new object (every setting kept) " +
            "or removes them.";
        public override int Order => 40;
        public override string ButtonLabel => "Recover...";
        public override bool Available => !EditorApplication.isPlayingOrWillChangePlaymode;
        public override string UnavailableReason => "Stop Play mode first.";
        public override void Run() => SnippetReconciler.RecoverOrphanedSnippets();
    }

    sealed class ClearAssetBundlesSetupTool : SetupTool
    {
        public override string Title => "Clear asset bundles";
        public override string Description =>
            "Removes the AssetBundle name from every asset and empties Unity's cache of downloaded asset bundles, so Play mode " +
            "loads them fresh.";
        public override int Order => 50;
        public override string ButtonLabel => "Clear";
        public override bool Available => !EditorApplication.isPlayingOrWillChangePlaymode;
        public override string UnavailableReason => "Stop Play mode first.";
        public override void Run() => BuilderWindow.ClearAllAssetBundles();
    }
}

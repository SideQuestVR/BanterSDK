# Installation

Requires Unity **6000.3.21f1**, the version the app itself is built with, with the Android and Windows build support modules. Use exactly this version: worlds built with another Unity version may not load in the app.

In short: import the installer package, press **Fix All** in the Setup panel that opens, then import the **Basics** sample to explore a working world.

?> **New to Unity?** Install [Unity Hub](https://unity.com/download), add Unity 6000.3.21f1 with the **Android Build Support** and **Windows Build Support** modules, and create a new project from the **Universal 3D** template. The Setup panel takes care of the rest.

## Installer Package

Download the installer and import it into your Unity project:

**[Install-com.sidequest.creator-sdk-latest.unitypackage](https://altvr.app/files/Install-com.sidequest.creator-sdk-latest.unitypackage)**

Double-click the downloaded file with your project open, or use `Assets > Import Package > Custom Package...`, then import everything it offers.

The installer adds the SideQuest package registry to your project's `Packages/manifest.json`, installs the latest version of the SDK from it (with the packages the SDK depends on), then removes itself. Later updates come from the same registry: `Window > Package Manager`, **SideQuest Creator SDK**, **Update**.

## Without the Installer

The SDK and some of the packages it depends on (Ora, the Basis packages and BouncyCastle) come from the SideQuest package registry, so the project needs that registry either way. Add this to `Packages/manifest.json`, next to `"dependencies"`; it's the entry the installer adds:

```json
"scopedRegistries": [
  {
    "name": "Greenfield-registry.sdq.st",
    "url": "https://greenfield-registry.sdq.st",
    "scopes": [
      "com.sidequest.creator-sdk",
      "com.basis.bundlemanagement",
      "com.basis.common",
      "com.basis.sdk",
      "com.sidequest.ora",
      "com.sidequest.thirdparty.bouncycastle"
    ]
  }
]
```

Then either:

- **From the registry:** add `"com.sidequest.creator-sdk": "4.0.17"` (or a later version) to `"dependencies"`; or
- **As an embedded package:** place the `com.sidequest.creator-sdk` folder directly in your project's `Packages/` folder. Unity picks it up as an embedded package on the next refresh, and fetches its dependencies from the registry.

## First Run: the Setup Panel

The **Setup** panel opens by itself the first time the SDK loads in a project, and again after an SDK update that leaves a Required item below unfinished. `Creator SDK > Setup` opens it any time. To have it open every time Unity starts, tick **Show this panel when Unity starts** at the bottom of the panel. These choices are yours alone: they're kept in the project's `UserSettings` folder, which isn't shared through version control.

Whenever a Required item is unfinished when Unity starts, the Console also says so, starting `[Creator SDK] Project setup isn't finished.`

The panel has buttons for the Builder and this documentation (which opens in a browser panel inside Unity, docked next to the Game view, where available), and a project setup checklist:

| Item | | What it sets up |
|------|-|-----------------|
| Android and Windows build support | Required | Spaces are built for both. Add missing modules in Unity Hub. |
| SDK layers and tags | Required | Scenes store layer numbers, so each SDK layer has to be in the client's slot (Grabbable is 20). |
| API compatibility level | Required | .NET Standard 2.1, which the SDK needs. |
| Space page (WebRoot) | Required | `Assets/WebRoot/index.html`, which Play mode serves and the Builder uploads. |
| One scene per project | Recommended | Every scene in a project shares the one page, `Assets/WebRoot/index.html`, so a world built from one scene is published with the scripts and snippets placed for the others, and they run in it. Keep one world per project. **Show scenes** selects the project's scenes, so you can move the others to projects of their own or delete them. |
| TextMesh Pro essentials | Required | The fonts BS Text draws with, so Unity's TMP importer doesn't interrupt Play mode. |
| Universal Render Pipeline | Required | Forward on Quest and Forward+ on Windows, as the client renders. |
| Graphics APIs | Required | Auto Graphics API on Android (Vulkan, OpenGL ES 3) and the client's Direct3D 11, Direct3D 12 and Vulkan on Windows, so spaces carry the shaders the client runs. |
| Linear color space | Required | As the client renders; baked lighting depends on it. |
| Visual Scripting nodes | Required | Puts BS components and the SDK's nodes in the fuzzy finder. |
| Active Input Handling | Required | `Both`, which SDK Play mode needs for mouse, keyboard and browser input. Takes effect after a Unity restart. |

**Fix All** runs every Required and Recommended fix. Each item also has its own button, and hovering over an item explains why it's needed, what goes wrong without it and what its fix changes. Items that need a step by hand (build support, one scene per project) have a button that takes you there instead, and Fix All leaves them to you. Nothing in the project changes until you press a button. When local multiplayer is available, the list also offers Multiplayer Play Mode (Optional) and Run In Background (Recommended); see [Testing Multiplayer Locally](../multiplayer/local-testing.md).

Below the checklist, **Tools** has the SDK's other windows and actions (the `Creator SDK` menu only lists Setup and Builder):

| Tool | What it does |
|------|--------------|
| Local Multiplayer | Opens the window for testing your world's multiplayer in the editor with extra players (when local multiplayer is available). |
| Configure Visual Scripting | Rebuilds the node library, as the checklist's Visual Scripting nodes item does. Run it again if BS components or the SDK's nodes are missing from the fuzzy finder. |
| Page developer tools | Opens the developer tools for your world's page: its console, network requests and elements. Works in Play mode. |
| Recover orphaned snippets | Finds snippets in `index.html` that no scene or prefab uses; see [Snippets](../building-in-unity/snippets.md). |
| Clear asset bundles | Removes the AssetBundle name from every asset and empties Unity's cache of downloaded asset bundles. |

The Builder covers the rest: its checklist runs the build checks (and fixes convex colliders), and its **Analyze bundle** button opens the Bundle Analyzer.

## Samples

Import samples via `Window > Package Manager > SideQuest Creator SDK > Samples`. Unity copies them into `Assets/Samples/`; open a sample's scene from there and press **Play** to try it.

| Sample | Description |
|--------|-------------|
| Basics | Getting-started worlds, each with its own scene: Basics (`BasicScene`: learn how to build worlds), Gadgets (`GadgetsExample`: fun tools to add to your world), Gravity Maze (`RealmsOfGravity`: an example space that manipulates gravity), Networking (`Networking`: learn how to use networking components) |
| FlexaWorld | A physics-fuelled playground showcasing the best of the FlexaBody system (scene `FlexaWorld.unity`) |

The samples' graphs are listed in [Visual Scripting](../visual-scripting/overview.md#sample-graphs). Because every scene in a project shares one page, import samples into a project of their own, not the one you're building your world in.

# Installation

Requires Unity 6000.3.10f1 or newer, with the Android and Windows build support modules.

In short: import the installer package, press **Fix All** in the Setup panel that opens, then import the **Basics** sample to explore a working world.

?> **New to Unity?** Install [Unity Hub](https://unity.com/download), add Unity 6000.3.10f1 or newer with the **Android Build Support** and **Windows Build Support** modules, and create a new project from the **Universal 3D** template. The Setup panel takes care of the rest.

## Installer Package

Download the installer and import it into your Unity project:

**[Install-com.sidequest.creator-sdk-latest.unitypackage](https://altvr.app/files/Install-com.sidequest.creator-sdk-latest.unitypackage)**

Double-click the downloaded file with your project open, or use `Assets > Import Package > Custom Package...`, then import everything it offers.

## Embedded Package

Alternatively, place the `com.sidequest.creator-sdk` folder directly in your project's `Packages/` folder. Unity picks it up as an embedded package on the next refresh.

## First Run: the Setup Panel

The first time the SDK loads in a project, the **Setup** panel opens (`Creator SDK/Setup` opens it again). It has buttons for the Builder and this documentation (which opens in a browser panel inside Unity, docked next to the Game view, where available), and a project setup checklist:

| Item | | What it sets up |
|------|-|-----------------|
| Android and Windows build support | Required | Spaces are built for both. Add missing modules in Unity Hub. |
| SDK layers and tags | Required | Scenes store layer numbers, so each SDK layer has to be in the client's slot (Grabbable is 20). |
| API compatibility level | Required | .NET Standard 2.1, which the SDK needs. |
| Space page (WebRoot) | Required | `Assets/WebRoot/index.html`, which Play mode serves and the Builder uploads. |
| TextMesh Pro essentials | Required | The fonts BS Text draws with, so Unity's TMP importer doesn't interrupt Play mode. |
| Universal Render Pipeline | Required | Forward on Quest and Forward+ on Windows, as the client renders. |
| Graphics APIs | Required | Auto Graphics API on Android (Vulkan, OpenGL ES 3) and the client's Direct3D 11, Direct3D 12 and Vulkan on Windows, so spaces carry the shaders the client runs. |
| Linear color space | Required | As the client renders; baked lighting depends on it. |
| Visual Scripting nodes | Required | Puts BS components and the SDK's nodes in the fuzzy finder. |
| Active Input Handling | Required | `Both`, which SDK Play mode needs for mouse, keyboard and browser input. Takes effect after a Unity restart. |

**Fix All** runs every Required and Recommended fix. Each item also has its own button, and hovering over an item explains why it's needed, what goes wrong without it and what its fix changes. Nothing in the project changes until you press a button. When local multiplayer is available, the list also offers Multiplayer Play Mode (Optional) and Run In Background (Recommended).

## Samples

Import samples via `Window > Package Manager > SideQuest Creator SDK > Samples`. Unity copies them into `Assets/Samples/`; open a sample's scene from there and press **Play** to try it.

| Sample | Description |
|--------|-------------|
| Basics | Getting-started worlds: Basics (learn how to build worlds), Gadgets (fun tools to add to your world), Gravity Maze (an example space that manipulates gravity), Networking (learn how to use networking components) |
| FlexaWorld | A physics-fuelled playground showcasing the best of the FlexaBody system |

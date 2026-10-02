# The Builder Window

The Builder builds and publishes your world without leaving Unity. Open it via `Creator SDK/Builder`, or **Open Builder** in the Setup panel; the window docks next to the Inspector.

![The Builder window](images/builder-window.png)

## Opening & Signing In

While you're signed out, the header shows **Sign in** and a device code.

1. Open sdq.st/link in a browser and sign in to your SideQuest account.
2. Enter the code shown in the window.
3. The window polls until the account is linked, then greets you by name.

If polling stops (it gives up after ~10 unsuccessful checks, or on an API error), close and reopen the window to get a fresh code. `Sign out` in the header switches accounts.

Building works while signed out; the world list and every upload action require signing in.

## Building a World (Scene Mode)

Drop a `.unity` scene file onto the drop area to enter Scene mode. The selected scene path is shown in place of the drop area and remembered between sessions; **Reset** clears it.

Pick a destination from the **World** dropdown — its hosting URL appears underneath, and the last-used world is reselected automatically. No world yet? Click **Create one** to name and create one right in the window.

| Button | Action |
|--------|--------|
| Build | Builds the scene into `Assets/WebRoot` as `asset.world`, a single platform-agnostic bundle that every platform loads |
| Build & upload | Same button with **Upload after building** ticked (and signed in): uploads to the selected world when the build finishes |
| Upload HTML + JS | Uploads just the web files from `Assets/WebRoot` — fast iteration on scripts without rebuilding |
| Upload all | Uploads everything: `asset.world` plus the web files |
| WebRoot folder | Highlights the `Assets/WebRoot` output folder in the Project window |
| Analyze bundle | Previews the AssetBundle contents and estimated size of the currently open scene (the scene must be saved to disk) |

The **Upload after building** toggle is remembered per project.

## Build Validation & Logs

A confirmation dialog summarizes every build before it runs — build mode, plus the scene file and destination world. **Cancel** backs out without building.

Once confirmed, every build first validates the scene's visual scripting graphs (see [Visual Scripting](visual-scripting.md)). Disallowed nodes stop the build, with details in the logs.

The **Logs** pane at the bottom of the window streams build and upload progress; the status bar mirrors the latest entry, and a progress bar appears above it during uploads. **Clear logs** empties the pane.

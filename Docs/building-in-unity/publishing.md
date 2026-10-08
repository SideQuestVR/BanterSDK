# Publishing Your World

Publishing builds your scene, uploads it with your world's page, and puts the world at its own address, where you and other players can visit it in the app. This page is the whole path; [The Builder Window](builder.md) covers the window itself.

## Before you start

- **The Setup panel's checklist is done.** **Creator SDK > Setup** sets up the build support, layers, tags and render settings a world needs ([Installation](../getting-started/installation.md)).
- **The project holds one world.** Every scene in a project shares the one page, `Assets/WebRoot/index.html`, so the snippets and scripts written for another scene are published with this world and run in it. Keep one scene per project. The Setup panel's **One scene per project** item and the Builder's checklist both warn when there are more.
- **It works in Play mode.** See [Testing in Play Mode](../getting-started/testing-in-play-mode.md).

## Step by step

1. **Open the Builder:** **Creator SDK > Builder**, or **Open Builder** in the Setup panel.
2. **Sign in** with your SideQuest account: **Sign in** opens https://altvr.app/link with the window's code filled in. See [The Builder Window](builder.md).
3. **Drop your scene** (`.unity` file) from the Project window onto the drop area.
4. **Pick a world** in the **World** dropdown, or press **Create one** and name a new one. The world's address appears under the dropdown.
5. **Read the checklist** and fix what it finds (see [The checklist](#the-checklist)).
6. **Press Build & upload.** The button says **Build & upload** while you're signed in and **Upload after building** is ticked. Unity asks to save modified scenes, the checklist runs again, and a confirmation shows the scene, the world and any problems. Confirm, and the Builder builds the scene, then uploads it. The **Logs** pane follows along.
7. **Visit your world** in the app (see [Visiting your world](#visiting-your-world)).

## What gets built

A build makes one file, `Assets/WebRoot/asset.world`, which holds your scene twice: once for Quest (Android) and once for Windows. Each device downloads only its own part. Building switches Unity between the two platforms, so it can take a while, the first time especially. The Builder opens your scene to build it and reopens whatever you had open afterwards. If `Assets/WebRoot/index.html` doesn't exist, the build creates the starter page.

[Platform Filter](platform-filter.md) objects are left out of the platform they're filtered from, and objects tagged **EditorOnly** are left out of both.

## What gets uploaded

| Button | Uploads |
|---|---|
| **Build & upload**, or **Upload all** | `asset.world`, `index.html` and the script file |
| **Upload HTML + JS** | `index.html` and the script file: for page changes, without rebuilding |

All of them come from `Assets/WebRoot`, and a file that isn't there is skipped (the log says `File not found, skipping`). **Upload all** sends the `asset.world` that's already in the folder, so build again first if you've changed the scene.

**A world has one script file**, served at both `script.js` and `bullshcript.js`. The Builder uploads `script.js`, or `bullshcript.js` when there's no `script.js`. If the folder has both, only `script.js` goes up and the log says so: move what `bullshcript.js` does into `script.js`. Load it from your page with `<script src="script.js"></script>`.

**Nothing else in `Assets/WebRoot` is uploaded.** Stylesheets, images, extra scripts and data files work in Play mode, where the whole folder is served, but are missing from the published world. Either:

- put the CSS and JavaScript inside `index.html` or `script.js`;
- load the file from a full `https://` address on a server of your own; or
- upload it on altvr.app: open your world's page, choose **Edit world**, then **Assets**, and **Upload files**. Files there are served from your world's address by name: `.css`, `.js`, `.json`, `.html`, `.png`, `.jpg`, `.jpeg`, `.webp`, `.ico`, `.mp3`, `.ogg` and `.wav`, up to 50 MB each. Names are made lower-case and there are no folders, so refer to them as `style.css`, not `css/style.css`.

**Size limits:** the built `asset.world` can be up to 300 MB, and `index.html` and the script file up to 10 MB each.

If an upload fails, the log shows a `FAILED UPLOADING` line and the world keeps its previous copy of that file: a file is only swapped in once the new one has arrived.

## Your world's address

Each world has its own address, `https://<address>.worldspace.host`, shown under the Builder's **World** dropdown. To change it, open the world's page on altvr.app and choose **Edit world**: the **Address** is under **Hosting** (at least 4 characters: lower-case letters, numbers and dashes). Changing it moves the world to the new address.

## Visiting your world

- **In the app** (Quest or Windows): open the menu's **WORLDS** section and choose **MY WORLDS**, then select your world.
- **From altvr.app:** your world's page has **Enter world**, which opens it in the app on that device.

**Who can join:** worlds the Builder creates are **Private**, so only you can go in. To let others in, open the world's page on altvr.app, choose **Edit world**, and change **Who can join**: Private, Friends, Friends of friends, the mods or members of the world's group, or Public. Only Public worlds show up in the directory. The world's name, description, images and **Capacity** (20 for a world the Builder creates) are set there too.

Test on a Quest as well as on Windows before you share the world: a Quest renders differently and has far less to spare. See [Performance](../reference/performance.md).

## Updating your world

- **Scene changes:** build and upload again (**Build & upload**). It replaces the world's files.
- **Page changes only:** **Upload HTML + JS**. No rebuild needed.

Players get the new version the next time they load the world; anyone already in it keeps the old one until they rejoin. The page and script are never cached. The app does keep a copy of your world's built scene: it checks for a newer one when you visit, but trusts a copy it checked in the last five minutes, so if you rejoin straight after uploading you may still see the old scene. Wait a few minutes and rejoin.

## The checklist

Before every build, and before **Upload all**, the Builder checks the scene for the problems that most often break a world in the app: setup that isn't finished, other scenes in the project, Visual Scripting nodes the app won't run, convex floors, tags and layers the app doesn't have, scene cameras, pink materials, a scene too heavy for a Quest, and prefabs set up wrongly. It also runs when the Builder opens on a scene and when you press **Re-check**.

- Fix what it lists before you publish. Many issues have a **Fix** button (with undo), and **Fix All** runs every fix that doesn't ask first.
- Issues marked **Blocks the build**, such as Visual Scripting nodes the app won't run, stop the build until they're fixed.
- Anything else is listed in the confirmation, whose button becomes **Build anyway** (or **Upload anyway**).

**Upload HTML + JS** doesn't run the checklist. Every check is listed in [The Build Checklist](builder.md#the-build-checklist); [Troubleshooting](../reference/troubleshooting.md) explains the common ones.

## Scene stats

Under the scene's path, the Builder shows its size at a glance, for example `250K triangles (40 meshes) · 180.0 MB texture memory (65 textures)`. It's counted from the assets the scene uses, each mesh and texture once however many times it appears. **Analyze bundle** opens the Bundle Analyzer for the open scene, with a breakdown by asset. Both are explained in [Performance](../reference/performance.md).

## Page attributes

The app reads two attributes on your page's `<html>` tag:

| Attribute | What it does |
|---|---|
| `world-asset` | Loads your world's built scene from the world's address. The starter page has it: keep it. Without it the scene doesn't load, and if the page makes nothing else either, after about 30 seconds the app decides there's no world there and opens the address in a browser instead. |
| `hide-space-info` | Stops the SDK drawing an information card (the world's name, image and description) and a background image onto your page. Players only see your page if you show its texture (see [The Space Browser](../browser/space-browser.md)), so this only matters then. |

The page's `<title>` and its `og:` tags aren't used for your world's listing: the app shows the name, description and images you set on altvr.app. (`og:title`, `og:description` and `og:image` are only a fallback for that information card.)

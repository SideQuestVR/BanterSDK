# SideQuest Creator SDK Documentation

Build multiplayer VR worlds in Unity, make them interactive with JavaScript or Visual Scripting, and publish them without leaving the Editor.

## How it works

1. **Lay out the world in Unity.** Scenes, models, lighting and physics work as in any Unity project, plus the SDK's components and [ready-made prefabs](building-in-unity/easy-prefabs.md): seats, mirrors, portals, grabbable objects and more.
2. **Make it interactive.** Script it in JavaScript in the world's page, `Assets/WebRoot/index.html`, or with Visual Scripting graphs if you would rather not code.
3. **Build and publish.** The Builder window builds the scene and uploads it as a world.

## Start here

- [Installation](getting-started/installation.md): add the SDK to a Unity project and run the Setup panel.
- [Quick Start](getting-started/quick-start.md): a first scripted object in a few lines of JavaScript.
- [Testing in Play Mode](getting-started/testing-in-play-mode.md): try your world in the Editor with the desktop controls.
- [Publishing Your World](building-in-unity/publishing.md): build it, upload it and visit it in the app.

Keep one world per Unity project: every scene in a project shares the one page, `Assets/WebRoot/index.html`.

The sidebar has everything else: setting a world up in Unity, the JavaScript API, multiplayer, every component,
browsers, Visual Scripting, troubleshooting and the reference tables.

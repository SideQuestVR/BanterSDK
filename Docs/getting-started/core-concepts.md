# Core Concepts

A world is a Unity scene plus a web page. The scene holds everything you lay out in the Editor; the page, `Assets/WebRoot/index.html`, runs your JavaScript, which reaches into the scene through the `BS` API. Objects you place in Unity and objects you create from JavaScript live in the same scene, so you can mix the two freely.

## Scene
The scene is the top-level singleton that manages all GameObjects, components, users, and communication with Unity. Access it via `BS.Scene.GetInstance()`.

## GameObject
GameObjects are the basic building blocks - containers that hold components. Create them with `new BS.GameObject({...})`. Every GameObject has a Transform for position, rotation, and scale.

## Components
Components add functionality to GameObjects. Physics, rendering, audio, interaction - all are components. Add them with `gameObject.AddComponent(new BS.ComponentName({...}))`.

## Transform
Every GameObject has a transform controlling its position, rotation, and scale in 3D space. Set these in the constructor or modify later with methods like `SetPosition()`.

## Assets
Large content such as textures, audio, and 3D models is tracked as assets rather than passed inline. See [Asset System](../javascript-api/asset-system.md).

## Node Graphs
Worlds can also be scripted without JavaScript, using node graphs authored in the Unity Editor. See [Visual Scripting](../visual-scripting/overview.md).

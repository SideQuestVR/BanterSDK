# Core Concepts

A world is a Unity scene plus a web page. The scene holds everything you lay out in the Editor; the page, `Assets/WebRoot/index.html`, runs your JavaScript, which reaches into the scene through the `BS` API. Objects you place in Unity and objects you create from JavaScript live in the same scene, so you can mix the two freely.

## Scene
The scene is the top-level singleton that manages all GameObjects, components, users, and communication with Unity. Access it via `BS.Scene.GetInstance()`. See [Scene API](../javascript-api/scene-api.md).

## GameObject
GameObjects are the basic building blocks - containers that hold components. Create them with `new BS.GameObject({...})`, or reach objects placed in Unity with `await scene.Find("Name")`. Every GameObject has a Transform for position, rotation, and scale. See [GameObject API](../javascript-api/gameobject-api.md).

## Components
Components add functionality to GameObjects. Physics, rendering, audio, interaction - all are components. Add them in Unity's Inspector, or from JavaScript with `await gameObject.AddComponent(new BS.ComponentName({...}))`: `AddComponent` returns a Promise, so `await` it before using the component. See [Components](../components/overview.md).

## Transform
Every GameObject has a transform controlling its position, rotation, and scale in 3D space. Set these in the constructor (`localPosition`, `localEulerAngles` or `localRotation`, `localScale`) or change them later, through `obj.transform` or with methods like `obj.SetPosition()`.

## Textures, Models and Media
Large content loads straight from a URL that you give a component: a [Material](../components/rendering.md#material)'s texture, a [GLTF](../components/media.md#gltf) model, a [VideoPlayer](../components/media.md#videoplayer)'s video. Materials can also show live web pages; see [Browser Textures](../browser/textures.md).

## Node Graphs
Worlds can also be scripted without JavaScript, using node graphs authored in the Unity Editor. See [Visual Scripting](../visual-scripting/overview.md).

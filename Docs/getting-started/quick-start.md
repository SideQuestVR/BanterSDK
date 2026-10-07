# Quick Start

A red ball that drops under gravity and reports where you click it, in about 20 lines of JavaScript. You need the SDK installed and the Setup panel's checklist done first ([Installation](installation.md)).

```js
window.addEventListener("bs-loaded", async () => {
    // Get the scene singleton
    const scene = BS.Scene.GetInstance();

    // Wait for the scene to be ready
    scene.On("unity-loaded", () => {

        // Create a simple object with a red sphere
        const sphere = new BS.GameObject({
            name: "MySphere",
            layer: BS.L.UI, // Layer 5 for UI.
            localPosition: new BS.Vector3(0, 1.5, 2)
        });

        // Add visual geometry
        sphere.AddComponent(new BS.Sphere({ radius: 0.5 }));
        sphere.AddComponent(new BS.Material({
            color: new BS.Vector4(1, 0, 0, 1)
        }));

        // Add physics
        sphere.AddComponent(new BS.SphereCollider({ radius: 0.5 }));
        sphere.AddComponent(new BS.Rigidbody({ mass: 1, useGravity: true }));

        // Handle clicks
        sphere.On("click", (e) => {
            console.log("Clicked at:", e.detail.point);
        });
    });
});
```

## Where it goes

Your world's JavaScript lives in its page, `Assets/WebRoot/index.html`, which the Setup panel creates. Put the code in a `<script>` in the page's `<body>`:

```html
<html world-asset>
<head>
  <meta charset="utf-8">
  <title>Space</title>
</head>
<body>
  <script>
    // The code above goes here
  </script>
</body>
</html>
```

Don't add a script tag for the SDK itself: the browser injects `window.BS` into every page.

## Run it

1. Give the ball something to land on: `GameObject > 3D Object > Plane` adds a floor at the origin.
2. Press **Play**. Play mode serves the page and runs it against the open scene, and the ball drops onto the floor in front of you.
3. Click the ball. To see what it logged, open the page's developer console: in the Setup panel (`Creator SDK > Setup`), press **Open** on **Page developer tools** while the space is playing.

?> The ball is on the **UI** layer because the SDK's desktop player in Play mode only clicks the UI and Menu layers. In the headset every layer is clickable. See [Layers](../building-in-unity/layers.md).

## What's happening

- **`bs-loaded`** fires once the SDK is ready in the page. It's latched: a listener added after it has fired runs straight away, so deferred scripts (`<script type="module">`) and other late-loading code never miss it.
- **`unity-loaded`** fires once the scene has loaded, so objects you create go into a world that exists.
- **`BS.GameObject`** is an empty object in the scene. Everything it does comes from components: `BS.Sphere` gives it a shape, `BS.Material` a colour, `BS.SphereCollider` something to click and collide with, and `BS.Rigidbody` gravity.
- **`On("click")`** listens for clicks on the object's collider; `e.detail.point` is where it was hit, in world space.

## Next steps

- [Core Concepts](core-concepts.md): scenes, GameObjects and components in a page.
- [Scene API](../javascript-api/scene-api.md) and [GameObject API](../javascript-api/gameobject-api.md): the full JavaScript reference.
- [Visual Scripting](../visual-scripting/overview.md): the same kind of thing without code.
- [The Builder Window](../building-in-unity/builder.md): publish your world.

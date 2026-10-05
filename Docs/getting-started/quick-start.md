# Quick Start

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

The `bs-loaded` event is latched: a listener added after the event has already fired is invoked immediately. Deferred scripts (`<script type="module">`) and other late-loading code can register the listener whenever they run — load order is never a race.

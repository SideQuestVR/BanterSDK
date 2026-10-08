# Components

Everything a GameObject does comes from its components: a shape, a material, physics, sound, a browser. You can add the same BS components in two ways: in the Unity Inspector while you build the scene, or from your page's JavaScript while the space runs.

## Adding components from JavaScript

Create the component with its settings and pass it to `AddComponent()`. `AddComponent` returns a Promise that resolves with the component once Unity has created it, so `await` it before you use the component.

```js
const obj = new BS.GameObject({ name: "MyObject" });
const rb = await obj.AddComponent(new BS.Rigidbody({ mass: 2 }));
```

**Constructor convention:** every component with configurable properties accepts either positional arguments or a single config object as its first argument (components with no properties, such as `BS.ColliderEvents` and `BS.WorldObject`, take no constructor arguments at all). A plain object in the first position is treated as the config bag: set any of the component's properties, omit the rest for their defaults, and include an `id` field to choose the component's JavaScript ID. `0`, `false` and `""` are real settings; only a missing property takes the default. A few components have long positional lists (`BS.ConfigurableJoint` and `BS.Geometry` run past thirty parameters), so the config-object form is preferred throughout these docs.

```js
// Equivalent constructions:
new BS.Rigidbody(2, 0, 0.05, true);                 // Positional: mass, drag, angularDrag, isKinematic
new BS.Rigidbody({ mass: 2, isKinematic: true });   // Config object, order-free
new BS.Rigidbody({ id: "ball-rb", mass: 2 });       // id is honoured
```

## Adding components in Unity

Select a GameObject, press **Add Component** in the Inspector and search for the component with its `BS` prefix: **BS Rigidbody**, **BS Text**, **BS Audio Source** and so on. Unity adds a **BS Object Id** alongside the first one; leave it there, it's what registers the object with your page (see [BSObjectId](../multiplayer/overview.md#bsobjectid)). Common objects come ready-made under **GameObject > BS** ([Easy Prefabs](../building-in-unity/easy-prefabs.md)).

The values you set in the Inspector are applied when the space starts. Where the object already had the Unity component a BS component drives (a Rigidbody, Audio Source, Skinned Mesh Renderer or Video Player you set up yourself), that component's own settings win, and the BS component reads them back so your script sees the values in effect.

Some BS components are a thin layer over a Unity component of the same name: the colliders, the joints and **BS Light**. For those, set the values on the Unity component (Box Collider, Hinge Joint, Light); the BS one makes it reachable from JavaScript.

## Changing and reading properties

Setting a property sends the new value to Unity:

```js
rb.mass = 5;
rb.useGravity = false;
```

Reading a property gives the value your script last set or last received, not a live one. To read what Unity has now, ask for it:

```js
const velocity = await rb.GetProperty(BS.PN.velocity);  // one fresh read

rb.WatchProperties([BS.PN.velocity]);                    // or keep it up to date as it changes
// ...later, rb.velocity is current
```

Only the properties that change by themselves can be watched: a Rigidbody's `velocity` and `angularVelocity`, and a video player's `time`. The switches under **SYNC RIGIDBODY TO JS** (or **SYNC VIDEOPLAYER TO JS**) in the Inspector do the same from the start.

## Calling methods

Component methods (`rb.AddForce(...)`, `audio.Play()`, …) also return Promises. They resolve once Unity has run the method, with its return value when it has one:

```js
await rb.AddForce(new BS.Vector3(0, 5, 0), BS.ForceMode.Impulse);  // resolves once applied

const sync = await obj.AddComponent(new BS.SyncedObject());
const mine = await sync.DoIOwn();                                   // true or false
```

## Using objects placed in the editor

Every object with a BS component shows up in your page's scene once the scene has loaded, so you can find it by name, or by its path through other objects that have BS components, and then get its components:

```js
const scene = BS.Scene.GetInstance();
scene.On("unity-loaded", async () => {
    const door = await scene.Find("Door");
    const hinge = door.GetComponent(BS.CT.HingeJoint);
    const knob = await scene.FindByPath("Door/Knob");
});
```

An object with no BS component isn't visible to the page. Give it one (any BS component, or just a **BS Object Id**) to reach it from JavaScript. See [Finding Objects](../javascript-api/scene-api.md#finding-objects).

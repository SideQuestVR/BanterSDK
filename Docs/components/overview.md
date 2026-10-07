# Components

All components use the constructor pattern with config objects. Add them to GameObjects with `AddComponent()`.

```js
const obj = new BS.GameObject({ name: "MyObject" });
obj.AddComponent(new BS.ComponentName({ property: value }));
```

**Constructor convention:** every component with configurable properties accepts either positional arguments or a single config object as its first argument (components with no properties, such as `BS.ColliderEvents` and `BS.WorldObject`, take no constructor arguments at all). A plain object in the first position is treated as the config bag — set any of the component's properties, omit the rest for their defaults, and include an `id` field to choose the component's JavaScript ID. A few components have long positional lists (`BS.ConfigurableJoint` and `BS.Geometry` run past thirty parameters), so the config-object form is preferred throughout these docs.

```js
// Equivalent constructions:
new BS.Rigidbody(2, 0, 0.05, true);                 // Positional: mass, drag, angularDrag, isKinematic
new BS.Rigidbody({ mass: 2, isKinematic: true });   // Config object — order-free
new BS.Rigidbody({ id: "ball-rb", mass: 2 });       // id is honored
```

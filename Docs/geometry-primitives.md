# Geometry Primitives

Simple shape components for quick prototyping.

## Box

```js
obj.AddComponent(new BS.Box({
    width: 1,
    height: 1,
    depth: 1,
    widthSegments: 1,
    heightSegments: 1,
    depthSegments: 1
}));
```

## Sphere

```js
obj.AddComponent(new BS.Sphere({
    radius: 1,
    widthSegments: 16,
    heightSegments: 16,
    phiStart: 0,
    phiLength: Math.PI * 2,
    thetaStart: 0,
    thetaLength: Math.PI
}));
```

## Plane

Plane faces -Z direction (forward).

```js
obj.AddComponent(new BS.Plane({
    width: 1,
    height: 1,
    widthSegments: 1,
    heightSegments: 1
}));
```

## Cylinder

Curved side faces -Z direction (forward).

```js
obj.AddComponent(new BS.Cylinder({
    radiusTop: 1,
    radiusBottom: 1,
    height: 1,
    radialSegments: 8,
    heightSegments: 1,
    openEnded: false,
    thetaStart: 0,
    thetaLength: Math.PI * 2
}));
```

## Cone

```js
obj.AddComponent(new BS.Cone({
    radius: 1,
    height: 1,
    radialSegments: 8,
    heightSegments: 1,
    openEnded: false,
    thetaStart: 0,
    thetaLength: Math.PI * 2
}));
```

## Circle

```js
obj.AddComponent(new BS.Circle({
    radius: 1,
    segments: 32,
    thetaStart: 0,
    thetaLength: Math.PI * 2
}));
```

## Torus

```js
obj.AddComponent(new BS.Torus({
    radius: 1,
    tube: 0.4,
    radialSegments: 8,
    tubularSegments: 16,
    arc: Math.PI * 2
}));
```

## TorusKnot

```js
obj.AddComponent(new BS.TorusKnot({
    radius: 1,
    tube: 0.4,
    tubularSegments: 64,
    radialSegments: 8,
    p: 2,      // Winds around axis
    q: 3       // Winds around interior
}));
```

## Capsule

```js
obj.AddComponent(new BS.Capsule({
    radius: 0.5,
    height: 1,
    radialSegments: 32,
    heightSegments: 1
}));
```

## Ring

Flat ring (annulus).

```js
obj.AddComponent(new BS.Ring({
    innerRadius: 1,
    outerRadius: 2,
    thetaSegments: 32,
    phiSegments: 1,
    thetaStart: 0,
    thetaLength: Math.PI * 2
}));
```

## Polyhedra

`Dodecahedron`, `Icosahedron`, `Octahedron`, and `Tetrahedron` share the same two parameters.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `radius` | number | 0.5 | Radius of the solid |
| `detail` | number | 0 | Subdivision detail (0 = the raw solid) |

```js
obj.AddComponent(new BS.Icosahedron({ radius: 0.5, detail: 0 }));
// Same constructor for BS.Dodecahedron, BS.Octahedron, BS.Tetrahedron
```

## Procedural Geometry

Shapes built from point data.

**Extrude** — a 2D outline given thickness, optionally along a curve.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `shapePoints` | string | "" | The 2D outline and holes, as JSON — without it no mesh is built |
| `curvePoints` | string | "" | Optional 3D path to extrude along, as JSON; empty extrudes straight along Z |
| `depth` | number | 1 | How far to extrude when there is no extrude path |
| `depthSegments` | number | 1 | Subdivisions along the extrusion axis |
| `segments` | number | 32 | How finely curves in the outline are sampled |

**Lathe** — revolves a 2D profile around the Y axis.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `shapePoints` | string | "" | The 2D half-profile to revolve, as JSON — without it no mesh is built |
| `segments` | number | 32 | How finely the profile itself is sampled |
| `radialSegments` | number | 32 | Segments around the axis of revolution |
| `phiStart` | number | 0 | Start angle of the revolution |
| `phiLength` | number | 6.283185 | Swept angle (2π = full revolution; less leaves the solid open) |

**Tube** — tube following a curve.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `curvePoints` | string | "" | The 3D curve to sweep along, as JSON — without it no mesh is built |
| `radius` | number | 0.5 | Radius of the tube cross-section |
| `tubularSegments` | number | 32 | Segments along the path |
| `radialSegments` | number | 32 | Segments around the cross-section |

**Shape** — flat filled shape from a 2D outline.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `shapePoints` | string | "" | The 2D outline and holes, as JSON — without it no mesh is built |
| `segments` | number | 32 | How finely curves in the outline are sampled |

**Pillow** and **Horn** — parametric surfaces with the standard tessellation pair.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `stacks` | number | 5 | Tessellation in one direction |
| `slices` | number | 5 | Tessellation in the other |

```js
obj.AddComponent(new BS.Tube({ curvePoints: points, radius: 0.2 }));
obj.AddComponent(new BS.Pillow({ stacks: 16, slices: 16 }));
```

## Parametric Shapes

Mathematical surfaces. Each is a standalone component taking `stacks` and `slices` tessellation (default 5 × 5), and each is also reachable through the `Geometry` component by enum value.

| Component | BS.ParametricGeometryType | Surface |
|-----------|---------------------------|---------|
| `BS.Klein` | `Klein` | Klein bottle |
| `BS.Mobius` | `Mobius` | Möbius strip |
| `BS.Mobius3d` | `Mobius3d` | Solid Möbius |
| `BS.Catenoid` | `Catenoid` | Catenoid |
| `BS.Helicoid` | `Helicoid` | Helicoid |
| `BS.Fermet` | `Fermet` | Fermat spiral surface |
| `BS.Natica` | `Natica` | Natica shell |
| `BS.Scherk` | `Scherk` | Scherk surface |
| `BS.Snail` | `Snail` | Snail shell |
| `BS.Spiral` | `Spiral` | Spiral surface |
| `BS.Spring` | `Spring` | Spring/coil |

`Pillow` and `Horn` (above) belong to the same family; the enum also carries `Apple` and `Custom`.

```js
// As a standalone component — stacks/slices control tessellation
obj.AddComponent(new BS.Klein({ stacks: 32, slices: 32 }));

// Or through the Geometry component by enum value
obj.AddComponent(new BS.Geometry({
    geometryType: BS.GeometryType.ParametricGeometry,
    parametricType: BS.ParametricGeometryType.Snail,
    stacks: 32,
    slices: 32
}));
```

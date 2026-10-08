# Rendering & Visual Components

## Light

Adds a real-time light. It wraps a Unity Light: in the Inspector, set the values on the Light component.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `type` | number | 0 | A `BS.LightType`: `Spot` (0), `Directional` (1) or `Point` (2) |
| `color` | Vector4 | 1, 1, 1, 1 | Colour, RGBA from 0 to 1 |
| `intensity` | number | 1 | Brightness |
| `range` | number | 10 | How far the light reaches, in metres (Point and Spot) |
| `spotAngle` | number | 30 | Cone angle in degrees (Spot) |
| `innerSpotAngle` | number | 21.8 | Inner cone angle in degrees (Spot) |
| `shadows` | number | 0 | A `BS.LightShadows`: `None` (0), `Hard` (1) or `Soft` (2) |

The default type is Spot, pointing along the object's +Z. `BS.LightType`'s other members are light types the app can't render in real time.

<div class="docs-tabs">

```js
await obj.AddComponent(new BS.Light({
    type: BS.LightType.Point,
    color: new BS.Vector4(1, 0.9, 0.8, 1),
    intensity: 2,
    range: 10,
    shadows: BS.LightShadows.None
}));
```

![BS Light in the Unity Inspector](../images/components/light.png)

</div>

## Material

Applies a material/shader to the object.

<div class="docs-tabs">

```js
await obj.AddComponent(new BS.Material({
    shaderName: "Unlit/Diffuse",        // Shader name
    texture: "https://example.com/texture.png",
    color: new BS.Vector4(1, 1, 1, 1),  // RGBA tint
    side: BS.MaterialSide.Front,        // Front, Back, Double
    generateMipMaps: false,
    normalMap: "",                      // Optional maps: empty = off (URL, asset id or scheme reference)
    roughnessMap: "",                   //   read from the GREEN channel (white = rough)
    aoMap: "",                          //   read from the RED channel
    textureScale: 1,                    // UV tiling, or tiles per metre on the triplanar shaders
    normalStrength: 1
}));
```

![BS Material in the Unity Inspector](../images/components/material.png)

</div>

The bundled diffuse shaders come in two families, each with an opaque and an alpha-blended twin, and
all four tint by `color` (the Transparent twins also honour its alpha):

| Shader | Mapping |
|---|---|
| `Unlit/Diffuse`, `Unlit/DiffuseTransparent` | Mesh UVs, tiled by `textureScale` |
| `Unlit/DiffuseTriplanar`, `Unlit/DiffuseTriplanarTransparent` | World-space triplanar (no UVs needed); `textureScale` = tiles per metre |

**Texture references.** `texture`, `normalMap`, `roughnessMap` and `aoMap` each take one of:

| Reference | What it loads |
|---|---|
| An absolute URL (`https://…/brick.png`) | An image downloaded from the web. `generateMipMaps` applies to it. |
| `cc0:{slug}/{map}/{size}` | A texture from the CC0 texture library. `map` is `basecolor`, `normal` or `mask`; the mask packs R = AO, G = roughness, B = metallic, so `roughnessMap` and `aoMap` can both point at it. A 256 px preview shows at once for `basecolor`, and the texture at the requested size replaces it once it has downloaded. Resolved by the app; in Play mode only when the `com.sidequest.textures-cc0` package is installed. |
| `asset_browser_world` or `asset_browser_<id>` | A live web page: your space's own page, or a BS Browser's. The material follows the page, so it stays current when the browser resizes. See [Browser Textures](../browser/textures.md). |

Anything else (a relative path, a typo) leaves the slot at the shader's default.

## Text

3D text, drawn with TextMesh Pro. It reads from the object's back (−Z), the opposite of its blue axis.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `text` | string | "" | The text |
| `color` | Vector4 | 1, 1, 1, 1 | Colour, RGBA from 0 to 1 |
| `fontSize` | number | 2 | Font size |
| `horizontalAlignment` | number | 1 | A `BS.HorizontalAlignment`: `Left` (0), `Center` (1) or `Right` (2) |
| `verticalAlignment` | number | 1 | A `BS.VerticalAlignment`: `Top` (0), `Center` (1) or `Bottom` (2) |
| `richText` | boolean | true | Allow TextMesh Pro rich-text tags such as `<b>` and `<color>` |
| `enableWordWrapping` | boolean | true | Wrap lines at the edge of the text box |
| `rectTransformSizeDelta` | Vector2 | 20, 5 | Size of the text box, in metres |

<div class="docs-tabs">

```js
await obj.AddComponent(new BS.Text({
    text: "Hello World",
    color: new BS.Vector4(1, 1, 1, 1),
    fontSize: 2,
    horizontalAlignment: BS.HorizontalAlignment.Center,
    verticalAlignment: BS.VerticalAlignment.Center,
    richText: true,
    enableWordWrapping: true,
    rectTransformSizeDelta: new BS.Vector2(10, 5)
}));
```

![BS Text in the Unity Inspector](../images/components/text.png)

</div>

## Billboard

Turns the object to face the player's view.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `smoothing` | number | 0 | 0 turns instantly; larger values turn more smoothly |
| `enableXAxis` | boolean | true | Rotate around X |
| `enableYAxis` | boolean | true | Rotate around Y |
| `enableZAxis` | boolean | true | Rotate around Z |

<div class="docs-tabs">

```js
await obj.AddComponent(new BS.Billboard({
    smoothing: 0,
    enableXAxis: false,  // stay upright: turn around Y only
    enableYAxis: true,
    enableZAxis: false
}));
```

![BS Billboard in the Unity Inspector](../images/components/billboard.png)

</div>

## Mirror

Creates a reflective mirror surface: 2.5 × 2.5 m, seen from the object's back (−Z). Scale the object to change its size.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `renderTextureSize` | number | 1024 | Resolution of the reflection; higher is sharper and costs more |
| `cameraClear` | number | 1 | What fills the reflection behind objects: 1 = Skybox, 2 = Solid Color, 3 = Depth Only, 4 = Nothing |
| `backgroundColor` | string | "#000000" | Colour used with Solid Color, as a hex string |

<div class="docs-tabs">

```js
const mirror = await obj.AddComponent(new BS.Mirror({
    renderTextureSize: 1024,
    cameraClear: 2,
    backgroundColor: "#202030"
}));
```

![BS Mirror in the Unity Inspector](../images/components/mirror.png)

</div>

**Methods:**

```js
mirror.SetCullingLayer(5);   // Show only this layer in the mirror
mirror.AddCullingLayer(6);   // Also show this layer
```

## InvertedMesh

Turns the object's mesh inside out, so you see it from inside: a sky sphere, or a room built from a box. It reverses the mesh's triangles once, when it starts. Normals aren't flipped, so it looks best with an unlit material.

<div class="docs-tabs">

```js
await obj.AddComponent(new BS.Sphere({ radius: 50 }));   // the shape first
await obj.AddComponent(new BS.InvertedMesh());
```

![BS Inverted Mesh in the Unity Inspector](../images/components/inverted-mesh.png)

</div>

The object needs its mesh by the time the component starts, so add the shape first. If the shape is rebuilt later (a property changes), the new mesh isn't turned inside out.

## SkinnedMeshRenderer

Exposes a skinned mesh, such as a character in your scene, to your script, most usefully its blend shapes. It works with the Skinned Mesh Renderer already on the object and doesn't add one, so add a **BS Skinned Mesh Renderer** in the Inspector next to it and [find it from your script](overview.md#using-objects-placed-in-the-editor). The Skinned Mesh Renderer's own settings win, and are read back into the properties.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `updateWhenOffscreen` | boolean | false | Keep skinning even when no camera sees the mesh |
| `skinnedMotionVectors` | boolean | true | Motion vectors for the skinned mesh |
| `quality` | number | 0 | Most bones per vertex: 0 = Auto, or 1, 2 or 4 |
| `blendShapes` | string | — | Read-only: the blend shapes as JSON, `{"blendShapes":[{"index":0,"name":"smile","weight":0}, …]}` |
| `bones` | string | — | Read-only: the bones as JSON, `{"bones":[{"name":…,"path":…,"instanceId":…}, …]}`, paths relative to the root bone |
| `rootBoneInstanceId` | number | — | Read-only: Unity instance ID of the root bone |

**Methods:**

<div class="docs-tabs">

```js
const scene = BS.Scene.GetInstance();
const face = await scene.Find("Face");
const smr = face.GetComponent(BS.CT.SkinnedMeshRenderer);

const smile = await smr.GetBlendShapeIndex("smile");   // -1 if there's no such shape
await smr.SetBlendShapeWeight(smile, 100);              // weights run 0 to 100
const weight = await smr.GetBlendShapeWeight(smile);   // 100
```

![BS Skinned Mesh Renderer in the Unity Inspector](../images/components/skinned-mesh-renderer.png)

</div>

`blendShapes` lists the weights from when the component started; it isn't updated by `SetBlendShapeWeight`.

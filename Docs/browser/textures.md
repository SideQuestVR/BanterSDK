# Textures

Every browser draws its page into a texture, and you can put that texture on any material. A page can fill a cinema screen, wrap a curved display or cover a sphere, and it stays live: video plays and the page updates as it changes.

## Texture references

Materials name a browser's texture by its reference:

| Browser | Reference | Where you can use it |
|---|---|---|
| A [BS Browser](bs-browser.md) | `asset_browser_` followed by the browser's `unityId`, e.g. `asset_browser_-41234` | Your space's script. The number is only known in Play mode, so it can't go in the Inspector or a graph. |
| [The space browser](space-browser.md) | `asset_browser_world` | Anywhere: your script, a BS Material's **Texture** field in the Inspector, Visual Scripting. |

## On a material

<div class="docs-tabs">

```js
// A browser's page on a 4 m plane, as well as on the browser itself
await browser.Async();   // until Unity has linked it, so unityId is set

const wall = new BS.GameObject({ name: "Wall screen", localPosition: new BS.Vector3(0, 2, 5) });
wall.AddComponent(new BS.Plane({ width: 4, height: 2.25 }));
wall.AddComponent(new BS.Material({
    shaderName: "Unlit/Diffuse",
    texture: "asset_browser_" + browser.unityId
}));
```

![BS Material in the Unity Inspector](../images/components/material.png)

</div>

- **In Unity**, type `asset_browser_world` into a BS Material's **Texture** field to show the space browser's page on that object.
- **In Visual Scripting**, set a BS Material's **Texture** (or call **Set Texture**) to `asset_browser_world`. Or take the `Texture` output of **On World Browser Texture** (`Events > BS > Utils`) and set it as a material's main texture.
- **Any texture slot works**: `texture`, `normalMap`, `roughnessMap` and `aoMap` all accept browser references.

## How it behaves

- **The texture is the page's size**, for example 1280 × 720 pixels, without mipmaps.
- **Resizing replaces it.** When a browser's page size changes, its texture is replaced, and every BS Material using its reference picks up the new one.
- **A material can come first.** A material that names a browser's texture before the page has drawn shows it as soon as it does.
- **The browser keeps drawing.** A BS Browser costs the same whether players look at the panel or only its texture, so see [Performance](bs-browser.md#performance) before adding many.

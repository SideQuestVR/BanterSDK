# Asset System

Assets are standalone resources (textures, audio clips, meshes) that live outside the GameObject/Component hierarchy and can be shared by multiple components. Every asset registers itself in the `AssetRegistry` when constructed.

## Asset

Base class for all assets.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `assetId` | string | auto | Unique ID, e.g. `asset_Texture2D_<uuid>` |
| `assetType` | AssetType | — | The asset's `BS.AssetType` value |
| `loaded` | boolean | false | True once the asset has loaded in Unity |
| `failed` | boolean | false | True if loading failed |
| `url` | string | — | Source URL, when loaded from one |
| `memorySize` | number | — | Memory used in bytes, when reported |
| `tag` | string | — | Free-form tag for grouping and lookup |

**Methods:**

```js
const ref = asset.createReference();   // type-safe AssetReference to this asset
const ok = await asset.waitForReady(); // true = loaded, false = failed
await asset.Async();                   // resolves with the asset once ready
asset.Destroy();                       // destroy in Unity and unregister
```

**Events:**

```js
asset.On("loaded", (e) => { /* e.detail = the asset */ });
asset.On("failed", (e) => { /* e.detail = { asset, error } */ });
asset.On("destroyed", (e) => { /* e.detail = the asset */ });
```

## AssetRegistry

Singleton tracking every asset in the scene.

```js
const registry = BS.AssetRegistry.GetInstance();
```

**Lookup:**

```js
registry.get(assetId);                 // asset or undefined (synchronous)
await registry.getOrWait(assetId);     // waits for load; rejects if loading fails

// Query fields: type, url, loaded, tag (all optional)
registry.find({ type: BS.AssetType.Texture2D, loaded: true });
registry.findByType(BS.AssetType.AudioClip);  // all assets of a type
registry.findByUrl("https://.../brick.png");  // asset with that URL
```

**Registration** — automatic for assets created through the SDK:

```js
registry.register(asset);       // add to the registry
registry.unregister(assetId);   // remove from the registry only
```

**Reference counting** — track which owners use an asset so it is not destroyed while in use. Owner IDs are any strings you choose:

```js
registry.addReference(assetId, ownerId);
registry.removeReference(assetId, ownerId);
registry.getReferenceCount(assetId);   // number of owners
registry.getDependents(assetId);       // owner IDs that reference this asset
registry.getDependencies(assetId);     // asset IDs this asset references

registry.destroyAsset(assetId);        // refuses (warns) while references remain
registry.destroyAsset(assetId, true);  // force: destroys regardless
```

**Diagnostics:**

```js
registry.getMemoryUsage(); // { totalAssets, assetsByType, totalMemoryBytes, memoryByType }
registry.inspect();        // print a registry summary to the console
```

The registry is a standard DOM `EventTarget` and fires `asset-registered`, `asset-loaded`, `asset-failed`, and `asset-destroyed` events via `registry.addEventListener(...)`.

## Asset References & Wrappers

`AssetReference` is a lightweight, serializable pointer to an asset by ID.

```js
const ref = asset.createReference();  // from an asset
const ref2 = BS.AssetReference.from(assetId, BS.AssetType.Texture2D); // from an ID

await ref.get();     // resolves with the asset (waits for load)
ref.getCached();     // asset if registered, null otherwise
ref.isLoaded();      // true when the asset has loaded
ref.isLoading();     // true while still loading
ref.hasFailed();     // true if loading failed
ref.equals(other);   // same id and type
```

Typed shorthands take just the ID: `BS.TextureReference`, `BS.AudioClipReference`, `BS.MaterialReference`, `BS.MeshReference`.

```js
const texRef = new BS.TextureReference(assetId); // AssetType.Texture2D implied
```

**Asset wrappers** — create an asset from a URL. Loading starts immediately when a URL is given; the optional second argument is a tag.

```js
const tex  = new BS.BanterTexture("https://.../brick.png", "level1");
const clip = new BS.BanterAudioClip("https://.../hit.mp3");
const mesh = new BS.BanterMesh("https://.../rock.mesh");
```

**`BS.AssetType` enum:**

| Member | Value | Member | Value |
|--------|-------|--------|-------|
| `Texture2D` | 0 | `AnimationClip` | 9 |
| `Texture3D` | 1 | `RenderTexture` | 10 |
| `AudioClip` | 2 | `Cubemap` | 11 |
| `Material` | 3 | `AssetBundle` | 100 |
| `Mesh` | 4 | `GLTF` | 101 |
| `Sprite` | 5 | `GameObject` | 200 |
| `Font` | 6 | `Component` | 201 |
| `Shader` | 7 | `Transform` | 202 |
| `PhysicsMaterial` | 8 | | |

## Worked Example

```js
// Create a texture asset from a URL — it registers itself and starts loading.
const tex = new BS.BanterTexture("https://cdn.example.com/brick.png", "level1");

// Wait for it to be ready (true = loaded, false = failed).
const ok = await tex.waitForReady();
if (!ok) throw new Error("texture failed to load");

// Hand out a reference instead of the asset itself.
const ref = tex.createReference();
const same = await ref.get();            // resolves to the loaded asset

// Track who is using it.
const registry = BS.AssetRegistry.GetInstance();
registry.addReference(tex.assetId, "wall-material");
registry.getReferenceCount(tex.assetId); // 1

// Look it up later without keeping a pointer.
registry.findByUrl("https://cdn.example.com/brick.png");
registry.find({ tag: "level1", loaded: true });

// Clean up — refuses while references remain unless forced.
registry.removeReference(tex.assetId, "wall-material");
registry.destroyAsset(tex.assetId);
```

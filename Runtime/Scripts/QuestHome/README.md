# Quest Home Loader

This folder contains all the scripts necessary for loading Quest Home environments from SideQuest APK files.

## Components

### Core Component
- **BanterQuestHome.cs** - Main component that orchestrates the Quest Home loading pipeline

### Helper Script
- **QuestHomeLoader.cs** - Simple MonoBehaviour helper for easy Unity Editor usage (drag-and-drop)

### Helper Classes
- **APKExtractor.cs** - Handles nested ZIP extraction (APK → scene.zip → .ovrscene → GLTF files)
- **KTXParser.cs** - Parses KTX texture files and extracts ASTC compressed texture data
- **GLTFMaterialMapper.cs** - Maps GLTF textures to Unity materials and handles material conversion

## Usage

### Unity Editor (Easiest!)
```
1. Create an empty GameObject in your scene
2. Add Component → Banter → Quest Home Loader
3. Set the APK URL in the Inspector
4. Press Play - loads automatically!
```

**Inspector Properties:**
- **APK Url** - URL of the Quest Home APK from SideQuest
- **Add Colliders** - Auto-generate colliders on opaque meshes
- **Legacy Shader Fix** - Convert to unlit shaders for Quest
- **Load On Start** - Auto-load when scene starts

**Public Methods:**
```csharp
loader.LoadQuestHome();      // Load manually
loader.UnloadQuestHome();    // Unload current home
loader.ReloadQuestHome();    // Reload (unload + load)
```

### A-Frame
```html
<a-entity sq-questhome="url: https://cdn.sidequestvr.com/file/167567/canyon_environment.apk"></a-entity>
```

### JavaScript SDK
```javascript
const go = new BS.GameObject("CustomHome");
const questHome = await go.AddComponent(
    new BS.BanterQuestHome(
        "https://cdn.sidequestvr.com/file/167567/canyon_environment.apk",
        true,  // addColliders
        true   // legacyShaderFix
    )
);
```

## Loading Pipeline

1. **Download APK** - Downloads APK from SideQuest CDN with progress tracking
2. **Extract Files** - Extracts nested ZIP archives to get GLTF, textures, and audio
3. **Load Textures** - Loads ASTC-compressed textures using Unity's native support
4. **Parse GLTF** - Parses GLTF JSON to extract material-texture mappings
5. **Load Model** - Loads GLTF model using GLTFUtility
6. **Apply Textures** - Applies loaded textures to Unity materials
7. **Convert Shaders** - Converts materials to unlit shaders (Quest optimization)
8. **Generate Colliders** - Auto-generates MeshColliders on opaque meshes
9. **Setup Audio** - Loads background audio if present in APK

## Technical Details

- Uses Unity's native ASTC texture decompression (no external decoder needed)
- Supports 6 ASTC formats that Unity supports: 4x4, 5x5, 6x6, 8x8, 10x10, 12x12
- Non-square formats (5x4, 6x5, 8x5, 8x6, 10x5, 10x6, 10x8, 12x10) are not supported by Unity
- Uses System.IO.Compression for ZIP extraction
- Integrates with existing GLTFUtility package
- Full async/await support for non-blocking loading

## APK Structure

```
CustomHome.apk/
├── assets/
│   └── scene.zip
│       ├── _WORLD_MODEL.gltf.ovrscene
│       │   ├── scene.gltf
│       │   ├── scene.bin
│       │   ├── texture0.ktx
│       │   ├── texture1.ktx
│       │   └── ...
│       └── _BACKGROUND_LOOP.ogg
```

## Dependencies

- **GLTFUtility** (Siccity.GLTFUtility) - Already included in Banter SDK
- **System.IO.Compression** - Built-in .NET library
- **Newtonsoft.Json** - Already included in Banter SDK

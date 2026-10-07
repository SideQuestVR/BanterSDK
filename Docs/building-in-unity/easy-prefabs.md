# Easy Prefabs

Ready-made objects for common space features, under `GameObject > BS` (also the Hierarchy's right-click menu). They ship with the Creator SDK package; there is nothing to import.

- **Player**: Spawn Point, Spawn Range, Seat, Teleporter, Scene Settings.
- **Grab**: one preset per grab type (Point, Point as a gun, Cylinder, Ball, Soft) and a climbable handhold.
- **Objects**: Mirror, Browser, Video Player, Text, Audio Source, Portal, GLTF Model, Synced Object, UI Panel, Billboard, Collider Events.

New objects land where Unity's own `GameObject` menu puts them: under the object you right-clicked, otherwise at the Scene view's centre (or the world origin, if **Preferences > Scene View > Create Objects at Origin** is on).

The mirror, browser, UI panel, portal, text and glTF model only build their visuals in Play mode, so the Scene view outlines them in blue instead, whether they came from this menu or **Add Component**: their size, and an arrow on the side they're seen from. The mirror, browser, UI panel and text all face the object's back (−Z), the opposite of the blue axis; a glTF model faces the blue axis (unless **Legacy Rotate** is on); the portal turns to face the player. Text also shows what it says. Select one to see its size in metres.

The player prefabs are built on SDK components (`BSSpawn`, `BSSeat`, `BSTeleporter`, `BSSettings`).

## Spawn Point and Spawn Range (`BSSpawn`)

When the space loads, one of the scene's active spawns is picked at random, and the local player is teleported there. The teleport uses the spawn's position and its Y rotation as the player's facing. `radius` spreads arrivals: players land at a random point within that many metres, on the horizontal plane. Spawn Point has radius 0 and Spawn Range has radius 5; otherwise they are the same component. For several spawn locations, add several spawns.

The teleport is a spawn teleport with velocity stopped, the same as the `TeleportTo` unit with `Is Spawn` on. A spawn added by a page script after the space has loaded doesn't move anyone. Scripts can call `Spawn()` on any spawn to send the local player there.

## Seat (`BSSeat`)

Clicking the seat sits the local player on it. Moving (the move stick or WASD) or jumping stands them up, unless `unseatOnMove` / `unseatOnJump` are off.

`BSSeat` sets up the `BSAttachedObject` beside it as a seat: Physics + AvatarAttachTo, jointed, `isSeat`, and never auto-attached. It also listens for clicks on its colliders and adds a kinematic Rigidbody if there is none. Without one, the client would add a dynamic Rigidbody and the chair would fall.

In the prefab, the seat components are on the `SitPoint` child at the top of the cushion. The player sits there, facing its forward. The child's trigger box is on the **UI layer**: the client clicks any layer, but the SDK's desktop player only clicks the UI and Menu layers. The visible parts use the `Seat` material (URP Lit).

In SDK Play Mode, clicking the seat sits the desktop player. **Space** stands them up (unless they're holding something, when Space is the held object's primary button), and so does moving while flying (right mouse + WASD).

## Teleporter (`BSTeleporter`)

When the local player walks into the trigger collider, they are teleported to `destination`: its position, facing its Y rotation, with velocity stopped. `cooldown` (0.5 s) stops the teleporter re-firing straight away. Only colliders tagged `BSLocalCharacter` count; don't give that tag to anything else. Move the prefab's `Destination` child to set the landing point, keeping it outside the trigger. The portal visual uses `FaceTarget` on the mesh, not the root, so the trigger and destination don't turn with the camera.

## Scene Settings (`BSSettings`)

Sets the space's settings from the scene:

- abilities: teleport, force grab, spider-man, hand holding, radar, name tags, portals, guests
- refresh rate and clipping planes
- player physics
- the two locks

They are applied when the space loads, again when the page reloads, and whenever a property changes. Spawn point, occupancy, avatars, friend position join, default textures and dev tools are not included. Use one per scene.

A page script that calls `scene.SetSettings(...)` sends every setting, so it replaces these if it runs afterwards. Either remove that call, or tick both locks so nothing can change the settings later. The build checklist warns when a page calls `SetSettings` and the settings aren't locked. In SDK Play Mode only the clipping planes have a visible effect; everything else takes effect in the client.

## Grab presets

Each grab preset is a Rigidbody root with `BSWorldObject` and `BSSyncedObject`, so every player sees it move, plus a collider on the **Grabbable layer (20)** with a `BSGrabHandle` saying how it's held:

| Preset | Grab type | How the hand holds it |
|---|---|---|
| Point (Handle) | Point | Snaps to one pose: the `GrabHandle` transform (a trigger capsule around the grip). |
| Point (Gun) | Point | The same grip, plus `BSHeldEvents` with the triggers blocked, so the trigger fires the gun's events. |
| Cylinder (Stick) | Cylinder | Anywhere along the handle's Y axis. |
| Ball | Ball | Anywhere on a sphere of the grab radius. |
| Soft (Any Shape) | Soft | Wherever the hand touches the collider. |
| Climbable Handhold | Cylinder | No Rigidbody, so grabbing it holds on to the world. |

Replace the placeholder meshes with your own and keep the handle collider on the Grabbable layer. Don't use `BSGrababble` for objects placed in the editor: it only sets itself up when a page script sets its properties.

## Objects

Each object is one SDK component, set up for the usual case:

| Object | What you get |
|---|---|
| Mirror | `BSMirror`: a 2.5 × 2.5 m mirror, seen from the object's back (−Z). |
| Browser | `BSBrowser` showing `https://sidequestvr.com`; see [Browser](#browser-bsbrowser). |
| Video Player | A 1.6 × 0.9 m quad with `BSVideoPlayer`; the video plays on it. |
| Text | `BSText` saying "Hello World" at font size 2, wrapping inside a 20 × 5 m area, read from −Z. |
| Audio Source | `BSAudioSource` with Spatial Blend 1, so it's heard from where it is. |
| Portal | `BSPortal`, a doorway to another space; see [Portal](#portal-bsportal). |
| GLTF Model | `BSGLTF` with Add Colliders on. Set its `url`; the model loads in Play mode, facing +Z. |
| Synced Object | A 0.3 m cube with a Rigidbody and `BSSyncedObject`, so every player sees it move. |
| UI Panel | `BSUIPanel`: 512 × 512 pixels at 100 pixels per metre, so 5.12 m square, read from −Z. |
| Billboard | A quad with `BSBillboard`, which turns it to face the player. |
| Collider Events | A 2 m trigger box standing on the object, with `BSColliderEvents` reporting enter and exit to the page and Visual Scripting. |

### Browser (`BSBrowser`)

Shows a web page in the space. Set `url` to your page. The page is `pageWidth` × `pageHeight` pixels (1280 × 720) and is drawn at 1300 pixels per metre, so about 0.98 × 0.55 m, centred on the object and read from its back (−Z). Change the page size to resize it, or scale the object; **Pixels Per Unit** doesn't change it. The page loads in Play mode; until then the blue outline shows where it will be. Clicks and scrolling reach the page. Scripts can resize it and show its page on other surfaces: see [Browser](../components/media.md#browser).

### Portal (`BSPortal`)

A doorway to another space: set `url` to that space's address, and `instance` to send players to one particular instance of it. In Play mode the portal looks the space up and shows its name and icon (a placeholder landscape if it has no icon, and "Unknown Space" if the address isn't a space). When the local player walks into it, the client takes them there. In SDK Play Mode you stay in your world: the Console says where the portal would go.

The ring is about 2.15 m across, 0.8 m above the object, and turns about its vertical axis to face the player, so the object's rotation doesn't matter; the blue outline in the Scene view turns towards the camera the same way.

## Build checklist

When the Builder opens on a scene, on **Re-check**, and before every build, the Builder checks the scene and lists what it finds:

- the project setup: one row for the Setup panel's required items (missing build support blocks the build)
- Visual Scripting nodes the client won't run
- convex colliders on large static geometry
- missing scripts and graphs, scene cameras and audio listeners, pink materials
- oversized or uncompressed Android textures, uncompressed long audio, overall scene size
- the prefabs above set up wrongly

Checks only report. A **Fix** button changes things only when clicked, with undo; **Fix All** runs every fix that doesn't ask first; **Select** shows what an issue is about.

- **Notes and warnings** are listed in the confirmation, whose button becomes **Build anyway**.
- **Errors marked "blocks the build"** stop it; other errors can be built past interactively.
- **Unattended (batch) builds** stop on any error and never show dialogs.

The checklist inspects the scene the builder is set to build, not whatever is open, and leaves the open scenes and their dirty state alone.

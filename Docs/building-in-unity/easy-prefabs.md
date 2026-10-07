# Easy Prefabs

Ready-made objects for common space features, under `GameObject > BS` (also the Hierarchy's right-click menu). They ship with the Creator SDK package; there is nothing to import.

- **Player**: Spawn Point, Spawn Range, Seat, Teleporter, Scene Settings.
- **Grab**: one preset per grab type (Point, Point as a gun, Cylinder, Ball, Soft) and a climbable handhold.
- **Objects**: Mirror, Browser, Video Player, Text, Audio Source, Portal, GLTF Model, Synced Object, UI Panel, Kit Item, Billboard, Collider Events.

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

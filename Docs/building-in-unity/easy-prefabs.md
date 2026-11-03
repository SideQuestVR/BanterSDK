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

The teleport stops the player's velocity. A spawn added by a page script after the space has loaded doesn't move anyone. Scripts can call `Spawn()` on any spawn to send the local player there.

## Seat (`BSSeat`)

Clicking the seat sits the local player on it. Moving (the move stick or WASD) or jumping stands them up, unless `unseatOnMove` / `unseatOnJump` are off.

Clicking another seat transfers the player directly to it. The previous seat's exit position is skipped when another seat has already been entered.

The shared **Pointer Range** component defaults to **2 m** on seats; edit its **Distance** to change reach. This governs clicking, the hover marker, sit hint and targeting ray. Distance is measured from the player's head to the seat origin in world space, so a scaled visual or larger trigger does not increase reach. Leaving range before releasing the click cancels seating. Editor placement previews and scripted `Sit()` remain available at any distance.

`BSSeat` sets up the `BSAttachedObject` beside it as a seat: Physics + AvatarAttachTo, jointed, `isSeat`, and never auto-attached. It also listens for clicks on the colliders it has when it starts (so add colliders before the seat when building one from a script) and adds a kinematic Rigidbody if there is none. Without one, the client would add a dynamic Rigidbody and the chair would fall.

Turn on **Use Exit Position**, select the seat, and drag the orange **Exit** point in the Scene view to place the player's feet when they get off (standing up, moving or jumping). This works in every seat mode without entering pose editing and supports Undo and prefab overrides. The exit stays relative to the seat, including its scale. Legacy faces the seat direction; upright modes preserve the player's viewing direction. With it off, the player is lifted 1 m above the seat with their speed cleared and drops back down.

The prefab is one GameObject: `BSSeat`, `BSAttachedObject`, `BSObjectId`, `BSPointerRange` and a trigger BoxCollider. Its origin is the sitting point, facing local **+Z**; place it at the top of your furniture's cushion. The preset starts 0.48 m above its placement point. Its trigger box is on the **UI layer**: only the UI and Menu layers can be clicked, in the app and in Play mode. It has no solid collider or furniture mesh; keep physical collision on your actual furniture.

**View Rotation** chooses how the seat carries the view and avatar:

| Mode | View | Pelvis and legs |
|---|---|---|
| **Legacy** | Carries the view with the seat's full rotation. | Fixed to the authored pose and follow the seat's complete rotation. |
| **Upright Follow Yaw** (default) | Follows seat position and left/right rotation, keeping seat pitch and roll out of the view. | Fixed to the authored pose and follow the seat's complete rotation. |
| **Upright Independent** | Follows seat position while seat rotation does not turn the view. | Fixed to the authored pose and follow the seat's complete rotation. |

Sitting aligns the player's viewing direction with the seat's forward direction. The upright modes use world **+Y** as up and align yaw while preserving physical head pitch and roll. Real head rotation and leaning still work normally; the camera is not forced level when the player physically tilts their head. Artificial VR snap/smooth turning is disabled while seated in these modes. Desktop mouse look and gamepad look remain available. The upper body follows tracked head and hands through IK; waist and foot tracking do not override the seat's pelvis and leg placement until the player stands.

Select the seat with Scene view Gizmos enabled to see the full **base Robot** reference avatar with Basic shoes for judging foot placement. Enable **Editor Robot Preview**, then choose **Edit Pose in Scene**. Drag the **Pelvis**, **Left sole**, **Right sole**, **Left knee**, or **Right knee** point directly in the Scene view. Movement follows the view plane; orbit the view to adjust depth. Rotate (**E**) remains available for the selected pelvis or foot. Knee points sit on the posed joints; dragging them changes bend direction while preserving leg lengths and foot targets. **Pose Values** exposes the same values numerically, with rotations shown in degrees. **Reset Seated Pose** restores the default pose. Open prefab assets in Prefab Mode to use scene handles; edits support Undo and prefab overrides.

The pose stores pelvis and left/right **sole** positions and rotations, plus two knee hints. The default pelvis joint is **0.04 m** above the seat origin, placing the reference Robot's underside just above the seat plane. The default soles sit **0.60 m below** and **0.475 m forward** of the seat origin, giving the reference Robot nearly level thighs and naturally bent knees. Adjust sole height to suit the furniture and avatar proportions. Existing custom poses are preserved; Reset Seated Pose replaces the entire pose with the defaults. These offsets are metres in the seat's rotated coordinate frame: a 0.2 m pelvis offset stays 0.2 m if the furniture or its parent is scaled. The preview is a reference Robot, not a replacement for the player's chosen avatar. At runtime the avatar keeps its own limb lengths; targets outside its reach are clamped. A very short avatar, a large lean, or an upside-down chair can therefore leave feet or the rendered head short of their targets. The camera remains tracked, and reaching a limit does not automatically eject the player.

View mode and pose are captured when the player sits. Changing them while occupied takes effect after standing and sitting again; moving or rotating the seat itself continues to carry its occupant. Configure the pose before using the seat. In SDK local multiplayer, late joiners or recreated preview avatars can use newly edited seat settings while existing observers retain the earlier pose; an unobserved stand-and-sit on the same seat cannot always be distinguished by that simulator. The production client streams the solved avatar bones instead.

The seat ring, backrest and facing arrow use the shared marker material in the Scene view (Gizmos enabled), respect hidden objects and Prefab Mode, and stay out of the Game view while editing. During play, **Show Seat Hologram** controls this marker and the sit hint; it keeps the existing `ShowRuntimeVisual` setting and defaults on. **Show Leg Hologram** independently shows the posed Robot pelvis, legs and Basic shoes and defaults off. Either, both, or neither can be enabled. Runtime holograms appear only while the local player targets an empty seat within interaction range, and hide when occupied. They are visual aids: hiding them does not disable the collider, sitting, or the editor preview. The client uses **Trigger to sit** (XR) or **Click to sit** (desktop); the SDK desktop simulator uses **Click to sit**.

The SDK ships the model at `Runtime/Resources/CreatorConveniences/Models/Seat.fbx` and its shared `Materials/SeatMarker` material. The SDK model is used by both editor previews and runtime. Vertex colors identify the ring (red), backrest (green) and arrow (blue). The texture-free, single-pass **Banter/Seat Marker** shader uses those masks for color and the authored UV spacing for a gentle traveling highlight. A double-sided additive glow fades vertically along the backrest and softly across the ring/arrow strips, with no bloom requirement. The shader bobs the ring 1–2.5 cm above its mesh; on hover the arrow eases from a 10 cm lift down to 1.5 cm over 0.25 seconds. Keep the full UV values, including V coordinates outside 0–1. One cached mesh and material serve every seat; a cached per-seat property block supplies the hover press without modifying that shared material. The glow uses ordinary depth testing, no depth writes and no shadows; the marker sits 0.015 m above the seat origin. The SDK and client must both contain this seat version; no extra visual component or custom world script is required.

When updating older prefab instances, the sitting origin has moved from the `SitPoint` child onto the root. Check existing placement and transform overrides so the root remains at cushion height. Preserve the existing `BSObjectId`; do not regenerate interaction identities to migrate a seat.

In SDK Play Mode, clicking the seat sits the desktop player. **Space** stands them up (unless they're holding something, when Space is the held object's primary button), and so does moving while flying (right mouse + WASD).

For scripts and graphs, see [Seat API](../components/player-setup.md#seat). Upright-view comfort, moving-seat behavior on real PCVR/Quest hardware, and real two-client behavior have **not yet been validated**. Editor checks and the Robot preview do not establish headset comfort; translation and yaw-following can still produce uncomfortable motion.

## Teleporter (`BSTeleporter`)

When the local player walks into the trigger collider, they land at the configured destination and face its Y rotation. **Stop Velocity** stops their movement; **Cooldown** (0.5 s) prevents an immediate repeat. Only colliders tagged `BSLocalCharacter` count; don't give that tag to anything else.

The prefab has its trigger, portal mesh, renderer and `BSTeleporter` on the root, with one `Destination` child retained for compatibility. Set the landing position and facing directly in the BS Teleporter Inspector, or move and rotate that child. Select the teleporter to see the curved route and exit-facing arrow. Keep the landing point outside the trigger. The portal stays aligned with its root; no Animator or camera-facing helper turns it.

An assigned **Override** transform takes precedence over the direct local position and yaw. At runtime, `Destination` can point to another transform; `DestinationPosition` and `DestinationYaw` configure the local fallback when no transform is assigned. **Use Local Destination** (`UseDestinationPosition`) enables that fallback; an older component with no destination stays invalid until configured.

The portal defaults to a cached image looking out from the destination at player eye height, in the editor and during play. It refreshes when the destination changes; use **Refresh Destination Image** after changing scenery at an unchanged destination. In VR, it caches two views in one stereo atlas using the headset eye spacing; each eye samples its own view. Desktop and editor use one image. The default is 256 pixels per eye. Cached images have a fixed viewpoint and do not update moving scenery or head-motion parallax.

Enable **Realtime When Nearby** for a live perspective view while the player is close and the portal is in the camera frustum. It maps the actual viewer and stereo eye cameras through the entrance to the destination, clips geometry in front of the exit plane, and keeps the existing glow. This follows the coordinate mapping used by the 3D mirror, with separate portal images and properties so multiple portals cannot overwrite each other. Leaving range returns to the cached view; approaching again refreshes immediately. Live capture runs just before the main camera renders, after URP refreshes its stereo eye matrices. Portal-in-portal recursion is suppressed.

Expand **Advanced** to adjust per-eye resolution (64–1024), live distance (default 8 m), and live refresh rate (0 means every frame). Lower resolution reduces rendering cost; a refresh cap reduces work but makes head-motion updates less smooth in VR. Live rendering is opt-in and renders the destination again for each eye. Single-pass VR captures both eyes once per frame; multipass updates the active eye, with a both-eyes-per-pass fallback if Unity cannot identify the active eye. Device performance and headset comfort need checking in the target world.

**Show Runtime Visual** defaults to on. Turning it off hides the full portal mesh and releases its captured image without disabling the teleport trigger. Editor destination images and the selected route remain available. Temporary capture data and the live capture camera are released when no longer needed or when the component is disabled.

## Scene Settings (`BSSettings`)

Sets the space's settings from the scene:

- abilities: teleport, force grab, spider-man, hand holding, radar, name tags, portals, guests
- refresh rate and clipping planes
- player physics
- the two locks

They are applied when the space loads, again when the page reloads, and whenever a property changes. Spawn point, occupancy, avatars and dev tools are not included. Use one per scene.

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

Replace the placeholder meshes with your own and keep the handle collider on the Grabbable layer. `BSGrababble` (see [VR Interaction](../components/vr-interaction.md#grababble)) is the all-in-one alternative: it adds the grab handle, `BSWorldObject` and held events itself and moves the object to the Grabbable layer, whether you add it in the Inspector or from a script.

Select a grab preset, `BSGrabHandle` or `BSGrababble` with **Gizmos** enabled to see a glowing hand in the Scene view. The two grab inspectors have an **Editor hand preview** toggle and an **Editor preview hand** selector; a handle restricted to one hand previews that hand. Selecting a parent prop also previews its child handles. The manual hand selector is for editing only and is hidden in Play Mode; runtime holograms automatically use the nearby tracked left or right hand. The SDK desktop simulator uses its simulated right hand.

- **Point** shows the fixed grip, including its facing and the left/right palm offset.
- **Cylinder** shows a grip around the handle's local Y axis. Drag the approach dot to try another side or height.
- **Ball** places the palm on the grab-radius sphere. Drag the approach dot to try another side.
- **Soft** uses the collider's surface instead of the grab radius. Drag the approach dot to choose where the hand reaches it.

The preview calls the runtime grab solver on isolated editor copies; moving the approach dot does not move your handle or change its serialized data. To change the actual grip, edit the handle's transform, grab type or radius. Finger curl is representative: avatar proportions, trigger input and custom hand poses can differ. Existing runtime limitations, including non-convex Soft sampling and Cylinder orientation under nested rotated parents, are also reflected in the preview.

Keep the Rigidbody root at unit scale for surface grips, and scale the collider/visual children instead. The current runtime surface solver can offset joint anchors when the Rigidbody itself is scaled; the preview preserves that behaviour and the inspector warns about it.

The editor and client share the stylized rig at `Runtime/Resources/CreatorConveniences/Models/GripHand.fbx`, copied from this project's authored hand. Position and rotation always come from the actual grab handle solver. Fingers curl against that handle's collider; a hand whose trigger is reserved by held events keeps its index finger extended. The thumb preserves its authored opposition and uses smaller bend limits than the other fingers. The left hand mirrors the right-hand rig. Each local preview owns one hidden rig with its renderer disabled and one reusable fitted mesh. New targets fit immediately; approach/shape changes refit at most 20 times per second. No continuous GPU skinning or preset pose assets are required. Finger fitting is a visual approximation: actual Flexabody fingers follow controller grip/trigger/touch poses or hand tracking, with optional custom handle poses; the ghost does not change those animations.

The complete hand is transparent, using the shared `Materials/GripHologram` material with the seat's cyan HDR color, a restrained rim glow and gentle traveling highlight. The authored second UV channel drives the wrist-to-fingertip gradient; UV0 is preserved. A shared `GripHologramDepth` material first records the nearest hand surface after ordinary world transparency renders; the color draw blends only where depth matches. This keeps the background visible without brightening through overlapping fingers/back faces. Both draws reuse the same fitted mesh and shader, with no texture, bloom feature or renderer feature required. Fade changes alpha smoothly. Custom transparent effects queued after 3100 can be masked by the hand's depth. Finger fitting uses native surface queries for primitive/convex colliders and conservative bounds for concave colliders.

Both materials share URP's instance/eye transfer path for single-pass instancing and multiview. Automated shader compilation covers PC D3D and Android Vulkan/OpenGL ES stereo variants for both the hand and seat. These compiler checks do not replace a two-eye headset test or device profiling.

**Show Runtime Visual** on `BSGrabHandle` or `BSGrababble` controls the in-game hand hologram for that object and defaults to on. If both components are present, either switch can hide the hint. Editor hand previews and actual grabbing are independent of this setting.

In the client, each local hand shows the pose of its actual next grab candidate inside the configured grab range. The indicator fades on leaving range and hides while holding, when grabbing is blocked, or when the local rig is disabled. It uses the same candidate picker as grabbing, including its existing first-overlap priority. The SDK desktop simulator shows the same hologram at the winning grabbable under the cursor; nearer UI/click targets, flying and holding suppress it. Hover and grabbing share the same mouse-hand approach calculation.

Holograms are local visuals with one state per hand, cached meshes/material and per-hand opacity; no network messages, per-grip renderers or authored scene objects are added. The hidden fitting rigs belong to the local previews and are disposed with them. A bare Grabbable-layer collider without a configured runtime grab handle keeps the existing Soft-grab fallback but has no hint until a handle exists. Add `BSGrabHandle` or `BSGrababble` for the complete authoring/preview flow.

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
| Spawner Object | A 0.4 m stick on the Grabbable layer with a kinematic Rigidbody, `BSWorldObject`, `BSSyncedObject`, `BSGrababble` (a Cylinder grip along its length) and `BSSpawnerObject`. Grabbing it puts a synced copy of the stick in your hand while the stick stays put; see [SpawnerObject](../components/special.md#spawnerobject). |
| UI Panel | `BSUIPanel`: 512 × 512 pixels at 100 pixels per metre, so 5.12 m square, read from −Z. |
| Billboard | A quad with `BSBillboard`, which turns it to face the player. |
| Collider Events | A 2 m trigger box standing on the object, with `BSColliderEvents` reporting enter and exit to the page and Visual Scripting. |

### Browser (`BSBrowser`)

Shows a web page in the space. Set `url` to your page. The page is `pageWidth` × `pageHeight` pixels (1280 × 720) and is drawn at 1300 pixels per metre, so about 0.98 × 0.55 m, centred on the object and read from its back (−Z). Change the page size to resize it, or scale the object; **Pixels Per Unit** doesn't change it. The page loads in Play mode; until then the blue outline shows where it will be. Clicks and scrolling reach the page. For messages, textures and everything else browsers can do, see the [Browser](../browser/overview.md) section.

### Portal (`BSPortal`)

A doorway to another space: set `url` to that space's address. In Play mode the portal looks the space up and shows its name and icon (a placeholder landscape if it has no icon, and "Unknown Space" if the address isn't a space). When the local player walks into it, the client takes them there. In SDK Play Mode you stay in your world: the Console says where the portal would go.

The ring is about 2.15 m across, 0.8 m above the object, and turns about its vertical axis to face the player, so the object's rotation doesn't matter; the blue outline in the Scene view turns towards the camera the same way.

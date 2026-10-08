# Visual Scripting

## Overview

The SDK ships Unity Visual Scripting plus its own library of BS nodes: node graphs that run on `Script Machine` components. Graphs execute inside the space at runtime — you can build interactive behaviour (buttons, levers, doors, vehicles, scoreboards) without writing any JavaScript.

An object can carry several Script Machines, each running its own graph. A graph can live in two places:

| Source | Where it lives | Use when |
|--------|----------------|----------|
| Embed | Stored on the Script Machine component itself | One-off behaviour tied to a single object |
| Graph | A `ScriptGraphAsset` file in your project | Reusable behaviour shared across objects and scenes |

## Setup

One-time setup: press **Generate nodes** on the Visual Scripting nodes item in the Setup panel (`Creator SDK > Setup`). This configures the project's Visual Scripting settings and rebuilds the node library. To rebuild it again later, press **Run** on **Configure Visual Scripting** in the panel's Tools.

Run it again if the BS nodes described below do not appear in the fuzzy finder, or after updating the SDK.

## Your First Graph

A cube that rotates when clicked:

1. Create a cube: `GameObject > 3D Object > Cube`. It comes with a Box Collider — clickable objects need a collider.
2. In the Inspector, set the cube's **Layer** to **UI**. Only the UI and Menu layers can be clicked, in the headset and in Play mode (see [Layers](../building-in-unity/layers.md)).
3. Select the cube and click `Add Component > Visual Scripting > Script Machine`.
4. Leave Source set to `Embed` (or choose `Graph` and create a new graph asset), then click `Edit Graph`.
5. In the graph window, right-click an empty spot to open the fuzzy finder. BS nodes appear under `BS` and `Events > BS`.
6. Add `Events > BS > PlayerEvents > On Click`. Leave its object input empty — an empty input means "this object". The node also outputs the click `Point` and `Normal` (world-space position and surface normal of the click).
7. Add a member node: search for `Transform Rotate` and pick `Transform: Rotate (X Angle, Y Angle, Z Angle)`.
8. Wire the control (arrow) output of `On Click` into the control input of `Rotate`, and set `Y Angle` to `15`.
9. Enter Play mode (or upload the space) and click the cube — it rotates 15° per click. In Play mode, left-click it ([Testing in Play Mode](../getting-started/testing-in-play-mode.md#clicking-grabbing-and-typing)).

## Event Nodes

Event nodes start a graph's control flow when something happens in the space. They all live under `Events > BS` in the fuzzy finder.

**Player events** (`Events > BS > PlayerEvents`)

| Node | Purpose |
|------|---------|
| On Click | A user clicked the object's collider, which must be on the UI or Menu layer. The object input defaults to this object; outputs the click `Point` and `Normal` |

**Held events** (`Events > BS > HeldEvents`) — fire while a player holds the object. They need a **BS Held Events** component on the object, which makes it grabbable and reports the input (see [HeldEvents](../components/vr-interaction.md#heldevents)); like anything grabbable, its collider goes on the Grabbable layer. The object input defaults to this object; every held event outputs `Is Left`, which reports which hand.

| Node | Purpose |
|------|---------|
| On Grab | The object was grabbed |
| On Release | The object was released |
| On Trigger | Continuously while held: the trigger's `Input` (float, 0 to 1) |
| On GunTrigger | The trigger was pulled past the BS Held Events **Sensitivity**. With **Auto** on, it repeats every **Fire Rate** seconds while the trigger stays pulled |
| On PrimaryDown | Primary button pressed while holding |
| On PrimaryUp | Primary button released while holding |
| On SecondaryDown | Secondary button pressed while holding |
| On SecondaryUp | Secondary button released while holding |
| On ThumbClickDown | Thumbstick clicked in while holding |
| On ThumbClickUp | Thumbstick click released while holding |
| On Thumbstick | Continuously while held: the thumbstick's `Input` (Vector2) |

**Controller events** (`Events > BS > Controller`) — global controller input, not tied to a held object. These only fire in the app, not in Play mode.

| Node | Purpose |
|------|---------|
| On Controller Button Pressed | Any controller button pressed. Outputs `Button Type` and `Hand Side` as numbers (see [ButtonType](../reference/enums.md#buttontype) and [HandSide](../reference/enums.md#handside)) |
| On Controller Button Released | Any controller button released. Outputs `Button Type` and `Hand Side` |
| On Controller Axis Update | Thumbstick axis changed. Outputs `Hand Side`, `X Axis`, `Y Axis` |
| On Trigger Axis Update | Trigger axis changed. Outputs `Hand Side` and `Trigger Value` |

**Trigger events** (`Events > BS > Trigger`)

| Node | Purpose |
|------|---------|
| On BS Trigger Enter Event Received | A collider entered this object's trigger. Outputs the `Collider`, and the `user` (BSUser) when the collider belongs to a player |

**Browser events** (`Events > BS > Browser`)

| Node | Purpose |
|------|---------|
| On BullSchript Callback Received | Injected JavaScript returned a value. Filter by `Return ID`; outputs `Data`. See [The Space Browser](../browser/space-browser.md#run-javascript-in-it-from-visual-scripting) |
| On Receive Browser Message | A page in a BS Browser sent a message. Fires for every browser in the space. Outputs `Message`; see [Messages](../browser/messages.md) |

**Space events** (`Events > BS > Space`)

| Node | Purpose |
|------|---------|
| On Quest Home Loaded | A Quest home finished loading. Takes the `Quest Home Object`; outputs `Success` and `Error Message` |

**Networking events** (`Events > BS > Networking`) — see [Multiplayer](../multiplayer/overview.md).

| Node | Purpose |
|------|---------|
| On One Shot | A one-shot network message arrived. Outputs `Data` |
| On Space State Properties Changed | A space state property changed. Filter by `Property Name` (leave empty for all); outputs `New Value` and whether it is a public property |
| On Space State Value Changed | As above, plus the JSON value and a `Deleted` flag. Filter by `Path` (leave empty for all); outputs `Value`, `JSON`, `Is Public Property?`, `Deleted` |
| On Space State Result | The outcome of a write you tagged with a `Request Id`. Outputs `Ok`, `Error`, `Path`, `JSON` |
| On Space State Error | **Any** failed space-state write, including ones with no `Request Id` — the only way a `Set Space Prop` failure can reach a graph. Filter by `Property Name`; outputs `Code`, `Message` |

**User events** (`Events > BS > User`)

| Node | Purpose |
|------|---------|
| On User Joined | A user joined the space. Outputs `User Info` (BSUser) |
| On User Left | A user left the space. Outputs `User Info` (BSUser) |
| On User State Value Changed | A participant's prop changed. Filter by `Key` (leave empty for all); outputs `Value`, `JSON`, `UserId`, `Is Local`, `Deleted` |
| On User State Result | The outcome of a write you tagged with a `Request Id`. Outputs `Ok`, `Error`, `Key`, `JSON` |
| On User State Error | **Any** failed user-state write, including uncorrelated ones. Filter by `Key`; outputs `Code`, `Message` |

**Utility events** (`Events > BS > Utils`)

| Node | Purpose |
|------|---------|
| On Global Event | A named global event fired (see `Trigger Global Event`). Configurable argument count |
| On World Browser Texture | The space's own page has a new texture: when it first paints, and again after a resize. Outputs the `Texture` (materials can use it as `asset_browser_world`) |

**World file events** (`Events > BS > World`) — the answers to the Persist File nodes below. See [Saving Data](../javascript-api/saving-data.md#visual-scripting).

| Node | Purpose |
|------|---------|
| On Persist File Saved | A save finished. Outputs `Saved Name`, `Success` and `Error` |
| On Persist File Loaded | A load finished. Outputs `Loaded Name`, `Success`, `Data` and `Error` |

**Recording events** (`Events > BS > Recording`) — app only. See [Recording](../javascript-api/recording.md#visual-scripting).

| Node | Purpose |
|------|---------|
| On Recording State Changed | This player's recorder changed state. Outputs `State`, `Take Id`, `Error` and `JSON` |
| On Program Changed | The program changed. Outputs `Program Feed Id`, `Preview Feed Id`, `Take Id` and `JSON` |

**UI events** (`Events > BS > UI`) — fired by UI elements created with the UI nodes below.

| Node | Purpose |
|------|---------|
| On UI Click | A UI element was clicked |
| On UI Change | A UI element's value changed |
| On UI Mouse Event | A mouse event occurred on a UI element |
| On Toggle Changed | A toggle was switched |
| On Slider Changed | A slider value changed |
| On Slider Int Changed | An integer slider value changed |
| On MinMax Slider Changed | A min-max slider range changed |
| On Dropdown Changed | A dropdown selection changed |
| On Text Field Changed | A text field's text changed |
| On Int Field Changed | An integer field's value changed |
| On Radio Button Changed | A radio button was selected |
| On Radio Button Group Changed | A radio button group's selection changed |
| On Color Picker Changed | A colour picker's colour changed: live while dragging, and once on release. Outputs `Element ID`, `Hex` and `Color` |

## BS Node Categories

The action-side BS nodes, grouped as they appear in the fuzzy finder.

**Player** (`BS > Player`) — control what the local player can do. Movement toggles live under `BS > Player > Actions`, input blocking under `BS > Player > Input`.

| Node | Purpose |
|------|---------|
| Set Can Move | Enable/disable locomotion |
| Set Can Rotate | Enable/disable snap turning |
| Set Can Jump | Enable/disable jumping |
| Set Can Crouch | Enable/disable crouching |
| Set Can Grab | Enable/disable grabbing |
| Set Can Grapple | Enable/disable grappling |
| Set Can Teleport | Enable/disable teleporting |
| Set Block Left Trigger | Block the left trigger from the platform |
| Set Block Left Primary | Block the left primary button |
| Set Block Left Secondary | Block the left secondary button |
| Set Block Left Thumbstick | Block the left thumbstick |
| Set Block Right Trigger | Block the right trigger |
| Set Block Right Primary | Block the right primary button |
| Set Block Right Secondary | Block the right secondary button |
| Set Block Right Thumbstick | Block the right thumbstick |

**Space** (`BS > Space`, `BS > Networking`) — space-level actions.

| Node | Purpose |
|------|---------|
| Get Space URL | Get the current space URL |
| Get Users | Get the users in the space, as a list of BSUser |
| Load Quest Home | Load a Quest home environment |
| Send a One Shot Message | Broadcast a one-shot network message |
| Set World Browser Size | Size the space's own page in pixels (default 1024 × 768), for example 1920 × 1080 to fill a 16:9 screen through `asset_browser_world`. Clamped to 320–3840 × 180–2160; 0 × 0 restores the default, as does loading the next space |
| Set Space State Property | Set a public/protected space state property (string value) |
| Set Space State Value | Set a value at a dotted `Path`. `Value Is JSON?` switches the text between a plain string and parsed JSON, so numbers, booleans, arrays and objects all work. Optional `Request Id` correlates the result |
| Delete Space State Value | Remove a `Path` **and everything under it** |
| Get Space State Value | Read the local mirror. Outputs `Value`, `JSON`, `Exists`, `Is Public Property?` (no trigger — it is a pure value node) |

**User** (`BS > User`) — act on users.

| Node | Purpose |
|------|---------|
| Get User Info | The BSUser whose id, uid or name matches the input (null when nobody does). Its output port is labelled `Name`, but it holds the whole BSUser: name, id, uid, color, isLocal, isSpaceAdmin |
| Get Local User Info | The local user's BSUser |
| Get User State | A user's head position/rotation (despite the name — it is not a prop reader) |
| Get Local User State | The local user's head position/rotation |
| Set My User State Value | Set one of **your own** synced props. `Value Is JSON?` parses the text; `Moderators Can Write?` opens it to space moderators (default: only you). No user id — user state is owner-writes-only |
| Delete My User State Value | Remove one of your own props |
| Get User State Value | Read any participant's prop from the local mirror. `UserId Or Me` defaults to `"me"`; outputs `Value`, `JSON`, `Exists` |
| Get Local User Language | The local user's language, such as `English` (empty in Play mode) |
| Add Force To Player | Apply a physics force to the player, with a `Mode` (ForceMode). App only |
| Teleport To Location | Teleport the player to `Position`, turned to face `Rotation` (degrees around Y) |
| Lock Player Position | Stop the player moving and grabbing. App only |
| Unlock Player Position | Release the lock |
| Add Toast Message | Show a notification with your `Message`, after `Delay` seconds, for `Timeout` seconds (default 5). App only |

**Utils** (`BS > Utils`, `BS > Browser`, `BS > Networking`) — helpers and content loading.

| Node | Purpose |
|------|---------|
| Load Texture from URL | Download an image into a `Texture`; carries on from `Loaded` or `Failed` |
| Load Text from URL | Download text with `GET`, or send `Body` with `POST`; carries on from `Loaded` or `Failed` |
| Load Audio from URL | Download an audio clip; carries on from `Loaded` or `Failed` |
| GameObject texture to Base64 | Read an object's texture as base64 |
| Trigger Global Event | Fire a named global event (pairs with `On Global Event`) |
| Trigger Visual Scripting Relay | Fire a **Visual Scripting Relay** component's event (`Add Component > BS > Visual Scripting Relay`), passing it a `Value` of the relay's type, so a graph can drive UnityEvents set up in the Inspector |
| Get Platform | Which platform the user is on, as comma-separated text that starts with `VR` or `2D` and ends with the operating system (empty in Play mode) |
| Copy Text To Clipboard | Copy a string to the user's clipboard |
| Color: TryParseHtmlString | Parse an HTML color string into a Color |
| String To Float Invariant Culture | Locale-safe string → float |
| Float To String Invariant Culture | Locale-safe float → string |
| UnEscape Url | Decode URL escapes in a string |
| Audio: Get AudioListener Spectrum Data | Sample spectrum data from the listener |
| Audio: Get AudioSource Spectrum Data | Sample spectrum data from an AudioSource |
| Inject BullSchript | Run JavaScript in the space's page (pairs with `On BullSchript Callback Received`) |
| Read BullSchript from File | Load JavaScript source from a file |

**World files** (`BS > World`) — save and load text files with your published world. See [Saving Data](../javascript-api/saving-data.md#visual-scripting).

| Node | Purpose |
|------|---------|
| Persist File Set | Save `Data` as the file `Name`; the result arrives on `On Persist File Saved` |
| Persist File Get | Load the file `Name`; the contents arrive on `On Persist File Loaded` |

**Recording** (`BS > Recording`) — app only. See [Recording](../javascript-api/recording.md#visual-scripting).

| Node | Purpose |
|------|---------|
| Start Recording | Start recording on this player's device. Outputs `Take Id` and `Error` |
| Stop Recording | Stop recording. Outputs `Error` |
| Cut To Feed | Cut a camera feed to the program. Outputs `Error` |

**UI** (`BS > UI`) — build UI Toolkit panels from graphs. Elements raise the `Events > BS > UI` events above.

| Node | Purpose |
|------|---------|
| Create UI Panel | Create a UI panel to hold elements |
| Destroy UI Panel | Remove a panel |
| Get UI Panel | Look up an existing panel |
| Create UI Element | Create a generic element |
| Create UI Box | Container: box |
| Create UI Foldout | Container: collapsible foldout |
| Create UI ScrollView | Container: scrollable view |
| Create UI Button | Control: button |
| Create UI Label | Control: text label |
| Create UI Toggle | Control: checkbox toggle |
| Create UI Slider | Control: slider |
| Create UI Dropdown | Control: dropdown |
| Create UI Text Field | Control: text input |
| Create UI Int Field | Control: integer input |
| Create UI Float Field | Control: float input |
| Create UI Color Picker | Control: colour picker (see [UIColorPicker](../components/ui-system.md#uicolorpicker)). The picker is drawn in the app; in Play mode it is an empty element |
| Create UI Image | Display: image |
| Create UI Progress Bar | Display: progress bar |
| Register UI Click | Subscribe an element to click events |
| Register UI Event | Subscribe an element to a named event |
| Attach UI Child | Add an element to a parent |
| Detach UI Child | Remove an element from its parent |
| Set UI Parent | Reparent an element |
| Destroy UI Element | Delete an element |
| Get UI Text / Set UI Text | Read/write an element's text |
| Get UI Value / Set UI Value | Read/write an element's value |
| Get UI Property / Set UI Property | Read/write a named property |
| Set UI Enabled | Enable/disable an element |
| Set UI Visible | Show/hide an element |
| Get UI Style / Set UI Style | Read/write a named style |
| Get UI Size / Set UI Size | Read/write width/height |
| Get UI Position / Set UI Position | Read/write position |
| Get UI Flexbox / Set UI Flexbox | Read/write flexbox layout |
| Get UI Spacing / Set UI Spacing | Read/write margin/padding |
| Get UI Border / Set UI Border | Read/write border styling |
| Get UI Background / Set UI Background | Read/write background styling |
| Get UI Appearance / Set UI Appearance | Read/write appearance styling |
| Get UI Typography / Set UI Typography | Read/write font styling |
| Load UXML Asset | Load a UXML layout asset |
| Process UXML Tree | Instantiate a loaded UXML tree |

### Deprecated Nodes

Six older nodes are hidden from the fuzzy finder. Graphs that already use them still run, and Unity marks them **Deprecated** with what to use instead:

| Node | Use instead |
|------|-------------|
| Load glTF/glb from URL | Set the BS GLTF's `Url`. The old node does the same: it sets the URL of a BS GLTF component that's already on an object, and doesn't create one |
| BS glTF is Loaded | The BS GLTF's `IsLoaded` |
| World Browser Open URL | Set the BS Browser's `Url` |
| BS Synced Object Take Ownership | The BS Synced Object's own **Take Ownership** member node |
| BS Synced Object Is Owner | The BS Synced Object's own **Do I Own** member node |
| Trigger VisualScriptingEvent | Trigger Visual Scripting Relay |

## Standard Unity Nodes

The stock Unity Visual Scripting node set is also available:

| Category | What's in it |
|----------|--------------|
| Control | If, Sequence, For/While loops, For Each, Switch, Select |
| Logic | Comparisons, And/Or/Negate |
| Math | Scalar and Vector arithmetic, Lerp, Min/Max, trigonometry |
| Time | Timer, Cooldown, Wait For Seconds, Wait Until |
| Collections | Lists and Dictionaries: create, add, remove, get item |
| Variables | Graph, Object, Scene and Application scoped variables |
| Events | Custom Event / Trigger Custom Event, lifecycle events (Start, Update) |
| Nesting | Subgraphs — reuse a graph inside another graph |

## Codebase Member Nodes

Beyond the dedicated nodes above, graphs can call approved Unity and SDK members directly. Type a class and member name in the fuzzy finder to get get/set/invoke nodes:

```
Transform: Rotate        // invoke a method
Transform: Set Position  // set a property
Debug: Log               // static call
```

BS components have member nodes too, for their properties and methods: a BS GLTF's `Url`, a BS Browser's `_RunActions`, a BS Synced Object's **Take Ownership**.

Only approved members run in the app. The fuzzy finder isn't limited to them: the node library takes in whole assemblies (Unity's and .NET's), so it offers many members the app won't run. Stick to the BS nodes and common `UnityEngine` members (Transform, GameObject, Rigidbody, Debug, ...), and let the Builder's check below tell you about the rest.

## Build Validation

The app only runs graphs whose nodes are on its allow list: the BS nodes, Unity's own Visual Scripting nodes, and the approved members. The Builder's checklist checks for this with its **Visual Scripting node support** check (see [The Build Checklist](../building-in-unity/builder.md#the-build-checklist)). It looks at:

- every script graph and state graph asset in the project, whether a scene uses it or not;
- every Script Machine and State Machine in every prefab, on any object in it;
- every Script Machine and State Machine in the scene being built, on inactive objects too;
- and inside all of them, the subgraphs, states and transitions they nest.

If any node isn't allowed, the checklist shows an error that blocks the build and can't be overridden:

```
3 Visual Scripting node type(s) aren't allowed in spaces
```

Under it, the first 15 offenders are listed by identifier, such as `UnityEngine.Application.OpenURL`. Remove or replace those nodes and build again. Graph assets you don't use count too, such as ones from imported samples: delete them. See also [Troubleshooting](../reference/troubleshooting.md#the-build-stops-visual-scripting-node-types-arent-allowed-in-spaces).

The Console lists them as well each time Unity reloads scripts (when the project opens, and after code changes): `[VisualScripting] Found elements that are not allowed for Visual Scripting`, then one error per node that starts `[VisualScripting] Element not allowed` and ends with the node's identifier.

!> Play mode doesn't enforce the allow list, so a graph can work there and not in the app. In the app, a Script Machine whose own graph has a node that isn't allowed doesn't run at all. The app doesn't check again inside the subgraphs and states a graph nests, so don't rely on it: fix everything the Builder lists.

## Sample Graphs

Both SDK samples ship ready-made graphs — import them via `Window > Package Manager > SideQuest Creator SDK > Samples`. Unity copies them into `Assets/Samples/`.

**Basics** imports four worlds, each in its own folder with its own scene, plus a shared folder:

| Folder | Scene | What it is |
|--------|-------|------------|
| `Basics` | BasicScene | Buttons, levers, held objects and player info |
| `Gadgets` | GadgetsExample | A draw tool, a flight thruster, a gravity gun and a simple gun |
| `GravityMazeExampleGame` | RealmsOfGravity | A small game that changes the direction of gravity |
| `Networking` | Networking | One-shot messages and space state between players |
| `-SharedAssets-` | | Materials, models, shaders, textures and a library of subgraphs the worlds share |

The graphs, and the BS nodes they show:

| Graph | Folder | Shows |
|-------|--------|-------|
| PhysicButton | `Basics/ScriptGraphs` | A physical push button (On Click) |
| GrabReleaseEvent | `Basics/ScriptGraphs` | On Grab and On Release |
| HeldEvent, HeldEventOneSided | `Basics/ScriptGraphs` | Every held event, for either hand or one |
| AngularLever, SlidingLever | `Basics/ScriptGraphs` | Levers that report their angle or position, with standard nodes only |
| ArmatureAttatchment | `Basics/ScriptGraphs` | Attaching an object to an avatar's armature |
| `PlayerInfo` | `Basics/ScriptGraphs` | Get Local User Info, Get User State, On BS Trigger Enter Event Received |
| DrawGadget, DrawGadgetUI | `Gadgets/DrawTool/ScriptGraphs` | A drawing tool: held events, space state properties, Get Local User Info |
| HandThruster | `Gadgets/FlightThruster/ScriptGraphs` | A hand-held thruster: held events and Rigidbody forces |
| GravityGun | `Gadgets/GravityTilt/ScripGraphs` | On GunTrigger, and a raycast that changes the direction of gravity |
| SimpleGun, Projectile, Pooling | `Gadgets/SimpleGun/ScriptGraphs` | A gun: On GunTrigger, Send a One Shot Message and On One Shot, and an object pool for its projectiles |
| GravitySystem, MovingPlatform, PlatformButton, SetGravityTrigger, SetPlatformState | `GravityMazeExampleGame/ScriptGraphs` | The gravity maze game: On Click and standard nodes |
| OneShotSender, OneShotReciver | `Networking/ScriptGraphs` | Send a One Shot Message and On One Shot |
| SetSpaceStatePropertie, ReceiveSpaceStatePropertie | `Networking/ScriptGraphs` | Set Space State Property and On Space State Properties Changed |

The subgraph library in `-SharedAssets-/Subgraphs`, for your own graphs:

| Folder | Subgraphs |
|--------|-----------|
| `common` | DirectionalTrigger, HDRcolor, Remap, SimpleSpaceStateBoole (a shared on/off value in space state), SmoothFloat, Timestep, isColliderLocalPlayer |
| `SavedVariables` | GetSavedVariable, SetSavedVariable, RemoveSavedVariable: values kept in the page's `localStorage` through Inject BullSchript, so on that one device |
| `SpaceSettings` | SpaceSettings, plus one subgraph per setting (allow guests, portals, spider-man, teleport, clipping plane, nametags, radar, max occupancy, refresh rate), each applied through JavaScript; see [Scene Settings](../javascript-api/scene-settings.md) |
| `Tweening` | TweenColor, TweenFloat, TweenQuaternion, TweenVector2, TweenVector3 |

**FlexaWorld** (scene `FlexaWorld.unity`; graphs in `Assets/Prefabs/ScriptGraphs/`):

| Graph | Purpose |
|-------|---------|
| Gun | A grabbable, firing gun (On GunTrigger) |
| Kart | A drivable kart: held events, with a Set Angular X Drive subgraph |
| SpaceSettings/* | The same space-settings subgraphs as the Basics sample's |

## Controlling Graphs from JavaScript

The `BS.ScriptGraph` component mirrors an object's Script Machines into JavaScript.

```js
const obj = await scene.Find("MyButton");
const graphs = obj.GetComponent(BS.CT.ScriptGraph);

console.log(graphs.machineCount);   // number of Script Machines on the object
console.log(graphs.graphTitles);    // comma-separated titles of their graphs (in the app; empty in Play mode)

graphs.CreateMachine();             // add a new Script Machine (fresh graph with Start/Update events)
graphs.RemoveMachine(0);            // remove the machine at index 0
graphs.RefreshMachines();           // re-sync machineCount / graphTitles
```

For inspecting and editing the graphs themselves from JavaScript (in the app only), see [Advanced: ScriptGraphBridge](script-graph-bridge.md).

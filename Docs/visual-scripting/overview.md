# Visual Scripting

## Overview

The SDK ships Unity Visual Scripting plus its own library of BS nodes: node graphs that run on `Script Machine` components. Graphs execute inside the space at runtime — you can build interactive behaviour (buttons, levers, doors, vehicles, leaderboards) without writing any JavaScript.

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
2. Select the cube and click `Add Component > Visual Scripting > Script Machine`.
3. Leave Source set to `Embed` (or choose `Graph` and create a new graph asset), then click `Edit Graph`.
4. In the graph window, right-click an empty spot to open the fuzzy finder. BS nodes appear under `BS` and `Events > BS`.
5. Add `Events > BS > PlayerEvents > On Click`. Leave its object input empty — an empty input means "this object". The node also outputs the click `Point` and `Normal` (world-space position and surface normal of the click).
6. Add a member node: search for `Transform Rotate` and pick `Transform: Rotate (X Angle, Y Angle, Z Angle)`.
7. Wire the control (arrow) output of `On Click` into the control input of `Rotate`, and set `Y Angle` to `15`.
8. Enter Play mode (or upload the space) and click the cube — it rotates 15° per click.

## Event Nodes

Event nodes start a graph's control flow when something happens in the space. All 49 of them live under `Events > BS` in the fuzzy finder.

**Player events** (`Events > BS > PlayerEvents`)

| Node | Purpose |
|------|---------|
| On Click | A user clicked the object. The object input defaults to this object; outputs the click `Point` and `Normal` |

**Held events** (`Events > BS > HeldEvents`) — fire while the object is being held. The object input defaults to this object; every held event outputs `Is Left`, which reports which hand.

| Node | Purpose |
|------|---------|
| On Grab | The object was grabbed. Outputs `Is Left` |
| On Release | The object was released |
| On Trigger | Trigger moved while holding. Outputs `Is Left` and the trigger `Input` (float) |
| On GunTrigger | Trigger fired like a gun trigger while holding. Outputs `Is Left` |
| On PrimaryDown | Primary button pressed while holding |
| On PrimaryUp | Primary button released while holding |
| On SecondaryDown | Secondary button pressed while holding |
| On SecondaryUp | Secondary button released while holding |
| On ThumbClickDown | Thumbstick clicked in while holding |
| On ThumbClickUp | Thumbstick click released while holding |
| On Thumbstick | Thumbstick moved while holding. Outputs `Is Left` and `Input` (Vector2) |

**Controller events** (`Events > BS > Controller`) — global controller input, not tied to a held object.

| Node | Purpose |
|------|---------|
| On Controller Button Pressed | Any controller button pressed. Outputs `Button Type` and `Hand Side` |
| On Controller Button Released | Any controller button released. Outputs `Button Type` and `Hand Side` |
| On Controller Axis Update | Thumbstick axis changed. Outputs `Hand Side`, `X Axis`, `Y Axis` |
| On Trigger Axis Update | Trigger axis changed. Outputs `Hand Side` and `Trigger Value` |

**Trigger events** (`Events > BS > Trigger`)

| Node | Purpose |
|------|---------|
| On BS Trigger Enter Event Received | A collider entered this object's trigger. Outputs the `user` (BSUser) when the collider belongs to a player |

**AI events** (`Events > BS > AI`)

| Node | Purpose |
|------|---------|
| On Ai Image | An AI image generation finished. Outputs `Data` |
| On Ai Model | An AI model generation finished. Outputs `Data` |
| On Ai SpeechToText | A speech-to-text result arrived. Filter by `Return ID`; outputs `Data` |
| On Base64 CDN Link | A Base64 To CDN upload finished. Outputs `Data` |
| On Camera Snap | A camera snapshot is ready. Outputs `Data` |

**Browser events** (`Events > BS > Browser`)

| Node | Purpose |
|------|---------|
| On BullSchript Callback Received | Injected JavaScript returned a value. Filter by `Return ID`; outputs `Data` |
| On Receive Browser Message | A message arrived from a space browser page. Outputs `Message` |
| On Receive Menu Browser Message | A message arrived from the user's menu browser. Outputs `Message` |

**Space events** (`Events > BS > Space`)

| Node | Purpose |
|------|---------|
| On Get User State | A Get User State request completed. Outputs `Data` |
| On Quest Home Loaded | A Quest home finished loading. Takes the `Quest Home Object`; outputs `Success` and `Error Message` |

**Networking events** (`Events > BS > Networking`)

| Node | Purpose |
|------|---------|
| On One Shot | A one-shot network message arrived. Outputs `Data` |
| On Space State Properties Changed | A space state property changed. Filter by `Property Name`; outputs `New Value` and whether it is a public property |
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

**Leaderboard events** (`Events > BS > Leaderboard`)

| Node | Purpose |
|------|---------|
| On Leaderboard Update Received | Leaderboard data arrived. Outputs `Board` and `Scores` |
| On Leaderboard Error Received | A leaderboard request failed. Outputs `Error Data` |

**File events** (`Events > BS > Files`)

| Node | Purpose |
|------|---------|
| On Select File | The user picked a file. Outputs `Data` and `Type` |

**UI events** (`Events > BS > UI`) — fired by UI elements created with the UI nodes below.

| Node | Purpose |
|------|---------|
| On UI Click | A UI element was clicked |
| On UI Change | A UI element's value changed |
| On UI Keyboard Event | A keyboard event occurred on a UI element |
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
| Set Block Left Thumbstick Click | Block the left thumbstick click |
| Set Block Right Trigger | Block the right trigger |
| Set Block Right Primary | Block the right primary button |
| Set Block Right Secondary | Block the right secondary button |
| Set Block Right Thumbstick | Block the right thumbstick |
| Set Block Right Thumbstick Click | Block the right thumbstick click |

**Space** (`BS > Space`, `BS > Networking`, `BS > Leaderboard`, `BS > AI`, `BS > Files`) — space-level actions.

| Node | Purpose |
|------|---------|
| Get Space URL | Get the current space URL |
| Get Users | Get the users in the space |
| Is Space Favourited | Whether the local user favourited the space |
| Load Quest Home | Load a Quest home environment |
| Send a One Shot Message | Broadcast a one-shot network message |
| Set World Browser Size | Size the space's own page in pixels (default 1024 × 768), for example 1920 × 1080 to fill a 16:9 screen through `asset_browser_world`. Clamped to 320–3840 × 180–2160; 0 × 0 restores the default, as does loading the next space |
| Set Space State Property | Set a public/protected space state property (string value) |
| Set Space State Value | Set a value at a dotted `Path`. `Value Is JSON?` switches the text between a plain string and parsed JSON, so numbers, booleans, arrays and objects all work. Optional `Request Id` correlates the result |
| Delete Space State Value | Remove a `Path` **and everything under it** |
| Get Space State Value | Read the local mirror. Outputs `Value`, `JSON`, `Exists`, `Is Public Property?` (no trigger — it is a pure value node) |
| Set a Score on a Leaderboard | Write a score to a leaderboard |
| Get the Current Leaderboard | Fetch leaderboard data |
| Clear Scores on a Leaderboard | Clear a leaderboard |
| Generate Ai Image | Request an AI-generated image |
| Generate Ai Model | Request an AI-generated 3D model |
| Start Speech To Text | Start speech-to-text capture |
| Stop Speech To Text | Stop capture and request the transcription |
| Base64 To CDN | Upload base64 data to the CDN |
| GameObject texture to Base64 | Read an object's texture as base64 |
| Select file (GLB/JPG/PNG) | Ask the user to pick a file |

**User** (`BS > User`) — act on users.

| Node | Purpose |
|------|---------|
| Get User Info | Get info for a given user |
| Get Local User Info | Get info for the local user |
| Get User State | A user's head position/rotation (despite the name — it is not a prop reader) |
| Get Local User State | The local user's head position/rotation |
| Get User Saved Value | Read a saved per-user value |
| Set User Saved Value | Write a saved per-user value |
| Remove User Saved Value | Delete a saved per-user value |
| Set My User State Value | Set one of **your own** synced props. `Value Is JSON?` parses the text; `Moderators Can Write?` opens it to space moderators (default: only you). No user id — user state is owner-writes-only |
| Delete My User State Value | Remove one of your own props |
| Get User State Value | Read any participant's prop from the local mirror. `UserId Or Me` defaults to `"me"`; outputs `Value`, `JSON`, `Exists` |
| Get Local User Language | Get the local user's language |
| Get the voice volume of the Local User | Current microphone volume of the local user |
| Add Force To Player | Apply a physics force to the player |
| Teleport To Location | Teleport the player |
| Lock Player Position | Lock the player in place |
| Unlock Player Position | Release the lock |
| Set User Avatar | Set the user's avatar |
| Add Toast Message | Show a toast notification |

**Utils** (`BS > Utils`, `BS > Browser`, `BS > Networking`, `BS > Components`; `Load glTF/glb from URL` and `World Browser Open URL` sit at the `BS` root) — helpers and content loading.

| Node | Purpose |
|------|---------|
| Load Texture from URL | Download an image into a Texture |
| Load Text from URL | Download text |
| Load Audio from URL | Download an audio clip |
| Load glTF/glb from URL | Download and spawn a glTF/glb model |
| Trigger Global Event | Fire a named global event (pairs with `On Global Event`) |
| Trigger VisualScriptingEvent | Invoke a VisualScriptingEvent component's UnityEvent (deprecated — use Trigger Visual Scripting Relay) |
| Trigger Visual Scripting Relay | Invoke a typed visual scripting relay |
| Get Platform | Which platform the user is on |
| Copy Text To Clipboard | Copy a string to the user's clipboard |
| Color: TryParseHtmlString | Parse an HTML color string into a Color |
| String To Float Invariant Culture | Locale-safe string → float |
| Float To String Invariant Culture | Locale-safe float → string |
| UnEscape Url | Decode URL escapes in a string |
| Audio: Get AudioListener Spectrum Data | Sample spectrum data from the listener |
| Audio: Get AudioSource Spectrum Data | Sample spectrum data from an AudioSource |
| Menu Browser Open URL | Open a URL in the user's menu browser |
| World Browser Open URL | Open a URL in an in-space browser |
| Get Menu Browser URL | Current URL of the menu browser |
| Inject BullSchript | Run JavaScript in the space's script context |
| Read BullSchript from File | Load JavaScript source from a file |
| BS glTF is Loaded | Whether a BS glTF component finished loading |
| BS Synced Object Take Ownership | Take network ownership of a synced object |
| BS Synced Object Is Owner | Whether the local user owns a synced object |

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

Only approved types and members are usable — the fuzzy finder is limited to an approved set of types and assemblies, and builds are validated member-by-member against the platform's allow list (see Build Validation below).

## Build Validation

Building through the `Creator SDK/Builder` window validates the graphs in your project — script and state graph assets, plus graphs embedded on prefabs and scene objects. If any graph uses a node or member outside the approved set, the build stops with:

```
Found disallowed visual scripting nodes, please check the logs for more information.
```

The Unity Console then names each offender in an error that starts with `[VisualScripting] Element not allowed` and ends with the node's identifier.

Remove or replace the listed nodes and build again. Sticking to the BS nodes and common `UnityEngine` members (Transform, GameObject, Rigidbody, Debug, ...) keeps graphs valid.

## Sample Graphs

Both SDK samples ship ready-made graphs — import them via `Window > Package Manager > SideQuest Creator SDK > Samples`.

**Basics** (`Basics/ScriptGraphs/`):

| Graph | Purpose |
|-------|---------|
| PhysicButton | A physical push button |
| GrabReleaseEvent | React to an object being grabbed and released |
| HeldEvent | React to controller input while an object is held |
| HeldEventOneSided | One-sided variant of HeldEvent |
| AngularLever | A rotating lever that reports its angle |
| SlidingLever | A sliding lever that reports its position |
| ArmatureAttatchment | Attach an object to an avatar armature |
| BanterPlayerInfo | Read player info into a graph |

**FlexaWorld** (`Assets/Prefabs/ScriptGraphs/`):

| Graph | Purpose |
|-------|---------|
| Gun | A grabbable, firing gun |
| Kart | A drivable kart (with a Set Angular X Drive subgraph) |
| SpaceSettings/* | Small graphs that each apply one space setting (allow guests, portals, teleport, spider-man, radar, max occupancy, refresh rate, clipping plane) |

## Controlling Graphs from JavaScript

The `BS.ScriptGraph` component mirrors an object's Script Machines into JavaScript.

```js
const obj = await scene.Find("MyButton");
const graphs = obj.GetComponent(BS.CT.ScriptGraph);

console.log(graphs.machineCount);   // number of Script Machines on the object
console.log(graphs.graphTitles);    // comma-separated titles of their graphs

graphs.CreateMachine();             // add a new Script Machine (fresh graph with Start/Update events)
graphs.RemoveMachine(0);            // remove the machine at index 0
graphs.RefreshMachines();           // re-sync machineCount / graphTitles
```

For inspecting and editing the graphs themselves from JavaScript, see [Advanced: ScriptGraphBridge](script-graph-bridge.md).

# Recording

Your world can start and stop a video recording on a player's own device, and run a multi-camera show: cameras placed in the world, screens that show them, and a "program" that cuts between them for everyone at once.

!> **Only in the app, on Windows and Meta Quest.** None of this works in SDK Play Mode: every call there answers `app_unavailable`, camera feeds don't render and feed monitors keep their own material. Publish your world to try it.

How it behaves:

- **The recording is the player's, on their device.** It's saved as an MP4 in the player's Videos folder (PC) or Movies folder (Quest), and nothing is uploaded. Your world can start and stop it and follow its progress, but never gets the file.
- **No confirmation.** A world script can start a recording on any player's device without asking them. Other players see a recording badge on whoever is recording.
- **A recording stops when the player leaves the space.**
- **Sound** is what the player hears plus their own voice, as their recording settings say.

## Check first

```js
const info = await BS.Recording.info();
if (info.canRecord) {
    showRecordButton();
} else {
    console.log("Can't record:", info.reason);
}
```

`BS.Recording.info()` never throws. It resolves with:

| Field | What it is |
|---|---|
| `canRecord` | Whether a recording can start now |
| `reason` | Why not: `already_recording`, `app_unavailable` (SDK Play Mode), `unavailable`, or the app's own message, such as no usable video encoder; `null` when it can |
| `recording` | Whether this player is recording now, whoever started it |
| `platform` | `"windows"` or `"android"` |

## Start and stop

```js
const { takeId } = await BS.Recording.start({ title: "Opening night" });

// ... later
await BS.Recording.stop();
```

`start(options)` resolves with the new recording's `takeId` as soon as the recorder accepts it. `stop()` resolves as soon as the stop is accepted; the file is finished afterwards, and the `Saved` state below says when.

With no options it records what the player sees. Name one source to record something else instead; if you name more than one, the first in this table wins:

| Option | What it records |
|---|---|
| `feedId` | A [camera feed](#camera-feeds-and-monitors) by its id, or `"program"` to follow whatever is [on program](#the-program) through every cut |
| `texture` | A texture: a `BS.Browser` (what that browser shows, following it when it resizes) or a texture reference string such as `"asset_browser_world"` for your space's own page. See [Textures](../browser/textures.md#texture-references). |
| `cameraObject` | A camera in your scene: its `BS.GameObject` or that object's `unityId`. A camera with a camera feed records as that feed. |

And these settings:

| Option | What it does |
|---|---|
| `preset` | Quality: `"Low"`, `"High"` or `"Ultra"`. Leave it out for the player's own setting (`"Default"` means the same). |
| `title` | The recording's title, used in its file name. Defaults to the world's name. |
| `srgbEncoded` | With `texture`: set it when the texture's colours come out too bright. |

| Quality | Windows | Quest |
|---|---|---|
| Low | 1280 × 720, 30 fps | 1280 × 720, 30 fps |
| Standard (the app's default) | 1920 × 1080, 30 fps | 1280 × 720, 30 fps |
| High | 1920 × 1080, 60 fps | 1920 × 1080, 30 fps |
| Ultra | 3840 × 2160, 30 fps | as High |

Recording a browser, for example a slideshow page:

```js
const screen = new BS.GameObject({ name: "Slides", localPosition: new BS.Vector3(0, 1.5, 3) });
const browser = await screen.AddComponent(new BS.Browser({ url: "https://example.com/slides" }));

await BS.Recording.start({ texture: browser, title: "Slides" });
```

A browser can only be recorded once its page has drawn its first frame; before that, `start` rejects with `texture_not_found`.

### Following a recording

```js
const off = BS.Recording.on("state", (s) => {
    console.log(s.state, s.takeId, s.error);
});

off(); // stop listening
```

`on("state", callback)` reports every change of this player's recorder, including recordings the player started themselves from the menu. It returns a function that removes the listener.

| `state` | Meaning |
|---|---|
| `Starting` | A recording is starting |
| `Recording` | It's recording |
| `Finalizing` | It has stopped and the file is being finished |
| `Idle` | Nothing is recording |
| `Faulted` | The recorder failed; `error` says why |
| `Saved` | Sent once when a recording's file is finished. Also carries `durationSeconds`. |
| `Failed` | Sent once when a recording couldn't be saved; `error` says why |

### Errors

Every call except `info()` rejects with a `BS.HostExtensionError` when it fails. Its `code` is one of:

| `code` | Meaning |
|---|---|
| `app_unavailable` | Recording isn't available here (SDK Play Mode) |
| `already_recording` | This player is already recording |
| `feed_not_found` | No camera feed has that id |
| `texture_unknown` | The texture reference isn't one the app understands (it starts with `asset_`) |
| `texture_not_found` | Nothing has been drawn under that reference yet |
| `camera_not_found` | The object wasn't found, or has no Camera |
| `bad_request` | The options are wrong, such as an unknown `preset` |
| `start_failed` | The recorder couldn't start; `message` says why |
| `timeout` | The app didn't answer within 10 seconds |
| `internal`, `internal_error` | Something went wrong in the app or on the way there |

```js
try {
    await BS.Recording.start({ feedId: "stage-cam" });
} catch (e) {
    if (e instanceof BS.HostExtensionError) console.warn(e.code, e.message);
}
```

## Camera feeds and monitors

A camera feed is a camera in your world whose picture the app renders into a texture. Feeds can be shown on screens, recorded, and cut to program. Both parts are components you add in Unity (**Add Component**, then search for their names); scripts refer to feeds by their id.

### Camera Feed (`BSCameraFeed`)

Put it on a Camera that doesn't draw to the screen. Leave the Camera component switched off: the app takes it over and turns it on only when the feed has to render.

| Field | What it does |
|---|---|
| Feed Id | The id screens, cuts and recordings use. Unique in the space; defaults to the object's name. |
| Display Name | The name players see when they choose a feed to record. Falls back to the id. |
| Feed Camera | The camera to render. Defaults to the Camera on this object. |
| Priority | When the device can't afford every feed, higher priorities keep rendering first. |
| Max Width, Max Height | The largest picture, 1280 × 720 by default. The app may render smaller. |
| Max Fps | The most renders per second, 30 by default. The app may render less often. |
| Pc Only | Offer the feed on Windows only, for camera rigs too expensive for Quest. |

A feed only renders while something needs it: a screen showing it that someone can see, or a recording. The app shares a render budget between feeds:

| | Windows | Quest |
|---|---|---|
| Feed cameras per frame | 4 | 1 |
| Renders per second, all feeds | 240 | 36 |
| Largest picture | 1280 × 720 | 640 × 360 |

Keep the number of feeds and screens small on Quest.

### Feed Monitor (`BSFeedMonitor`)

Put it on a screen's renderer to show a feed on it.

| Field | What it does |
|---|---|
| Feed Id | A camera feed's id, or `program` (what's on air) or `preview` (what's lined up next). Defaults to `program`. |
| Target Renderer | The renderer to draw on. Defaults to the Renderer on this object. |
| Material Index | Which of the renderer's materials gets the picture. |
| Texture Property | The material's texture property: `_BaseMap` for URP Lit and Unlit (the default), `_MainTex` for most others. |

The material asset itself is never changed. A monitor whose feed doesn't exist keeps its material's own texture.

## The program

The program is the feed that's "on air" in the space, and preview is the one lined up next. Every player's `program` and `preview` monitors follow it, and recordings of `"program"` switch camera with every cut. A cut reaches every player in the space at the same moment, about a quarter of a second after it's made, and players who join later see the current program.

```js
await BS.Newsroom.cut("wide", "close-up");     // "wide" on air, "close-up" on preview
await BS.Newsroom.cut("preview");              // take whatever is on preview to air

const { program, preview } = await BS.Newsroom.getProgram();

const off = BS.Newsroom.on("program", (p) => {
    highlightTally(p.program, p.preview);
});
```

- `cut(programFeedId, previewFeedId)` puts a feed on program for everyone, and optionally another on preview (leave it out to keep the current preview). `"program"` and `"preview"` stand for whatever is on them now. It doesn't check that the feed exists. Anyone can cut.
- `getProgram()` resolves with `program`, `preview` and `takeId` (the program recording started from the app's director tools, or `null`).
- `on("program", callback)` reports every change, from any player's cut, with the same three fields. It returns a function that removes the listener.

Your script runs for every player, so cut from one place, such as the player who pressed a button, rather than from every player at once. The program is kept in the public space-state key `newsroom.program`; use `cut` rather than writing it yourself. Outside a multiplayer room a cut only changes this player's program.

## Under the hood

`BS.Recording` and `BS.Newsroom` are wrappers over a general way of asking the app for a feature by name. You can use it directly:

```js
const scene = BS.Scene.GetInstance();
const reply = await scene.HostExtension("recording", "info", {});
// { ok: true, canRecord: true, reason: null, recording: false, platform: "windows" }

scene.On("host-ext", (e) => {
    console.log(e.detail.command, e.detail.evt, e.detail.data); // "recording", "state", {...}
});
```

`scene.HostExtension(command, op, body, timeoutMs)` never rejects: it resolves `{ ok: true, ... }` or `{ ok: false, error }`, with `error` set to `app_unavailable` when the app doesn't provide that feature and `timeout` after `timeoutMs` (10000 by default). The `host-ext` event carries everything the app's features report, as `command`, `evt` and the parsed `data`.

## Visual Scripting

Actions are under `BS > Recording`, events under `Events > BS > Recording`. Each action's `Error` output is empty when it worked and holds one of the [error codes](#errors) when it didn't.

| Node | What it does |
|---|---|
| Start Recording | Starts recording. Inputs `Feed Id`, `Texture Ref`, `Camera` (a GameObject), `Preset` and `Title`, as the [options](#start-and-stop) above; outputs `Take Id` and `Error`. |
| Stop Recording | Stops recording. Outputs `Error`. |
| Cut To Feed | Cuts `Program Feed Id` to program, and `Preview Feed Id` to preview if it isn't empty. Outputs `Error`. |
| On Recording State Changed | This player's recorder changed state. Outputs `State`, `Take Id`, `Error` and the whole event as `JSON`. |
| On Program Changed | The program changed. Outputs `Program Feed Id`, `Preview Feed Id`, `Take Id` and `JSON`. |

For `Texture Ref` in a graph, `asset_browser_world` records your space's own page.

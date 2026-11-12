# Photos

Your world can take a photo for a player from a camera you placed: a photo booth, a group-shot spot, a "say cheese" button by the stage. The player presses something in your world, and your script takes the picture from that camera.

!> **Only in the app.** In SDK Play Mode every call answers `app_unavailable`. Publish your world to try it.

How it behaves:

- **The photo is the player's.** It goes into their photos the same way a hand camera photo does: saved on their device, tagged with the people in the shot (players who opted out of photo tags are in the picture but not named), and uploaded to their account if they have photo uploads on. They don't need their hand camera out. Your world gets the photo's details and, once it's uploaded, its link, but never the image itself.
- **The player has to press something first.** A photo is only taken right after the player clicks the object you name, such as the booth's button (see [The click rule](#the-click-rule)).
- **Only from your own cameras.** The photo is taken from a [Camera Feed](recording.md#camera-feed-bscamerafeed) in your world, never from the player's hand camera or the program.
- **The player always knows.** Every photo shows the usual "Photo saved" toast.

## Set up the camera

1. Add a Camera where the photo should be taken from, pointing at where people will stand. Switch the Camera component off.
2. Add a **Camera Feed** (`BSCameraFeed`) to it and give it a **Feed Id**, such as `PhotoBooth`.
3. Add the button players press: an object with a collider on the **UI** layer (see [Layers](../building-in-unity/layers.md)), close enough to reach (see [Pointer Range](../components/ui-system.md#pointer-range)).

The [Camera Feed](recording.md#camera-feed-bscamerafeed) fields work as they do for recording. Add a [Feed Monitor](recording.md#feed-monitor-bsfeedmonitor) with the same id if you want a screen that shows players what the camera sees.

## Take a photo

Take the photo from the button's `click` event, and name the button as `clicked`:

```js
const scene = BS.Scene.GetInstance();
const button = await scene.Find("BoothButton"); // needs a BS Object Id

button.On("click", async () => {
    const reply = await scene.HostExtension("photo", "take", {
        feedId: "PhotoBooth",
        clicked: button.unityId
    });

    if (reply.ok) {
        console.log("Took photo", reply.id, "with", reply.people.length, "people in it");
    } else {
        console.warn("No photo:", reply.error, reply.message);
    }
});
```

`take` needs:

| Field | What it is |
|---|---|
| `feedId` | The Feed Id of the Camera Feed to take the photo from |
| `clicked` | The object the player pressed to take it: its `unityId`, or its BS Object Id |

It answers once the photo is saved on the player's device, usually a few frames later:

| Field | What it is |
|---|---|
| `ok` | `true` |
| `id` | The photo's id. The [upload events](#uploads) use it. |
| `width`, `height` | The photo's size in pixels |
| `people` | The players in the photo (see below) |

Each entry in `people`:

| Field | What it is |
|---|---|
| `userId` | Their SideQuest user id; empty for guests and for players who opted out of photo tags |
| `name` | Their display name; empty for players who opted out |
| `isGuest` | Whether they're a guest |
| `private` | Whether they opted out of photo tags. Only their position is given. |
| `x`, `y` | Where their head is in the photo, from 0 to 1, from the top-left corner |

### A countdown

The player's click allows a photo for 10 seconds, so there's time for a "3, 2, 1":

```js
button.On("click", async () => {
    for (const n of [3, 2, 1]) {
        showCountdown(n);
        await new Promise((r) => setTimeout(r, 1000));
    }
    showCountdown(null);
    await scene.HostExtension("photo", "take", { feedId: "PhotoBooth", clicked: button.unityId });
});
```

## The click rule

A photo is only taken when:

- **The player clicked the object named in `clicked`**, or one of its children, in the last 10 seconds. A click somewhere else in your world (a door, a seat) doesn't count. Clicks are the player's real presses with their pointer or mouse; clicks raised from scripts or graphs don't count.
- **That click hasn't already taken a photo.** Each click allows one photo.
- **The last photo was at least 2 seconds ago.**

Your script runs for every player, but only the player who pressed the button can take a photo with that click, and the photo is always theirs. To take a photo of everyone in the booth, take it from the one camera: everyone in the shot is in the picture and tagged, unless they opted out.

## Check first

```js
const info = await scene.HostExtension("photo", "info", {});
// { ok: true, canTake: true, reason: null, uploads: true }
```

| Field | What it is |
|---|---|
| `canTake` | Whether a photo could be taken now. The click is only checked by `take`. |
| `reason` | Why not: `unavailable`, `busy` or `too_soon`; `null` when it can. `info` itself answers `app_unavailable` in SDK Play Mode. |
| `uploads` | Whether the photo will be uploaded to the player's account: they're signed in and have photo uploads on |

## Uploads

When the player has uploads on, the photo uploads after `take` has answered. Your world hears how it went through the `host-ext` event:

```js
scene.On("host-ext", (e) => {
    const { command, evt, data } = e.detail;
    if (command !== "photo") return;

    if (evt === "uploaded") showLink(data.id, data.url);
    if (evt === "upload_failed") console.warn("Upload failed", data.id, data.error);
});
```

| `evt` | `data` |
|---|---|
| `uploaded` | `id` and `url`, the uploaded photo's link |
| `upload_failed` | `id` and `error` |

Only the world that took the photo hears about its upload. If the player has left by the time it finishes, nobody is told.

## Errors

`scene.HostExtension` never rejects: when a photo isn't taken it resolves with `ok: false` and an `error` code, and usually a `message` with more detail.

| `error` | Meaning |
|---|---|
| `app_unavailable` | Photos aren't available here (SDK Play Mode) |
| `bad_request` | `feedId` is missing, or the body isn't valid |
| `unavailable` | The app can't take photos right now |
| `busy` | A photo is already being taken |
| `too_soon` | The last photo was under 2 seconds ago |
| `no_click` | The player didn't press the object named in `clicked` in the last 10 seconds, or that click already took a photo, or the object wasn't found |
| `feed_not_found` | Your world has no Camera Feed with that id |
| `capture_failed` | The photo couldn't be taken; `message` says why |
| `timeout` | The photo took too long |
| `unknown_op`, `internal_error` | Something went wrong in the app or on the way there |

For the `scene.HostExtension` call itself, see [Under the hood](recording.md#under-the-hood) in Recording.

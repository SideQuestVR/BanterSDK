# Scene Settings

A space's settings decide what players can do in it (teleport, the grapple, grabbing from a distance), how they move (walking speed, jumping, the grapple's rope), and a few things about the space itself: where players arrive, the refresh rate and the clipping planes. Set them from your page with `scene.SetSettings`, or place a **Scene Settings** object in Unity.

```js
window.addEventListener("bs-loaded", async () => {
    const scene = BS.Scene.GetInstance();

    const settings = new BS.SceneSettings();
    settings.EnableTeleport = false;
    settings.EnableSpiderMan = true;                      // the grapple
    settings.SpawnPoint = new BS.Vector4(0, 10, 0, 90);   // x, y, z, and the Y rotation in degrees
    settings.PhysicsJumpStrength = 1.5;
    await scene.SetSettings(settings);
});
```

## How settings are applied

- **`SetSettings` sends every setting.** Anything you didn't set goes out at its default from the tables below, so a second call that only changes `EnableTeleport` also puts everything else back to the defaults. Keep one `BS.SceneSettings` object, change it, and send it again.
- **Call it early**, as above, before the space finishes loading. `EnableGuests` and `SpawnPoint` are acted on while the space loads.
- **Every space starts from the defaults.** The settings are reset when the player leaves, so nothing carries over to the next space.
- **A [Scene Settings](../components/player-setup.md#settings) object in the scene** (see [Easy Prefabs](../building-in-unity/easy-prefabs.md#scene-settings-bssettings)) sets the same values when it starts, again whenever the page reloads, and whenever one of its properties changes. Whichever runs last wins: a `SetSettings` call after it replaces all of its values, and it replaces yours. Use one or the other, or lock them.
- **Locks.** Once `SettingsLocked` is `true`, nothing can change the general settings (the first table) until the player leaves: later `SetSettings` calls and Scene Settings objects are ignored for them. `PhysicsSettingsLocked` does the same for the physics settings. The call that sets a lock still applies its own values first, and a lock can't be undone. A Scene Settings object that set a lock can still change its own values.
- **In SDK Play Mode** only `ClippingPlane` has an effect. Everything else needs the app.

## General Settings

| Setting | Type | Default | What it does |
|---------|------|---------|--------------|
| `EnableTeleport` | boolean | true | Players can teleport themselves. Teleports you make (`TeleportTo`, spawns, BS Teleporters) work either way. |
| `EnableSpiderMan` | boolean | false | Players can use the grapple: fire a rope with the trigger (Q or E on desktop) and reel in with grip. |
| `EnableForceGrab` | boolean | false | Players can grab objects from a distance. |
| `EnableNametags` | boolean | true | The name tags over other players. `false` hides them, whatever each player's own Nametags option says. |
| `EnableAvatars` | boolean | true | Other players' avatars. `false` shows everyone else as a plain placeholder body, and their avatars aren't downloaded. Your own avatar isn't affected. |
| `EnableGuests` | boolean | true | `false` turns away players who aren't signed in: their load is stopped with a message asking them to sign in. Signed-in players are never turned away. See the note below. |
| `RefreshRate` | number | 72 | The display refresh rate, on headsets that let apps change it (Quest). The headset's highest supported rate at or below this is used; elsewhere it's ignored. |
| `ClippingPlane` | Vector2 | (0.02, 1500) | The player's camera near (`x`) and far (`y`) clipping planes, in metres. |
| `SpawnPoint` | Vector4 | (0, 0, 0, 1) | Where players arrive: `x`, `y`, `z`, and in `w` the Y rotation in degrees. They're placed here as the loading screen opens, and the menu's **Respawn** brings them back. The default (0, 0, 0, 1) means "none": then a [Spawn](../components/player-setup.md#spawn) in the scene picks the spot, or players start at the origin. A spawn point you set wins over Spawn objects. |
| `SettingsLocked` | boolean | false | Locks the settings in this table. See [Locks](#how-settings-are-applied). |

`EnableGuests` is a check in the app, made as your space loads. To really keep guests out, use your world's settings on altvr.app: **Edit world > Who is turned away > Guests**. That stops guests at the door, before they get in.

## Physics Settings

How the local player moves. Each player's app applies them to that player.

| Setting | Type | Default | What it does |
|---------|------|---------|--------------|
| `PhysicsMoveSpeed` | number | 3.25 | Walking speed, in metres per second. |
| `PhysicsMoveAcceleration` | number | 4.6 | How hard players push off to reach walking speed. It's also what holds them on slopes and carries them up steps, so values below 2.2 are raised to 2.2. |
| `PhysicsAirControlSpeed` | number | 2.4 | How fast players can steer themselves in the air, in metres per second. Moving faster than this, steering can still turn or slow them, but not speed them up. |
| `PhysicsAirControlAcceleration` | number | 6 | How quickly they steer in the air, in metres per second per second. |
| `PhysicsDrag` | number | 0 | Air resistance on the player's body. It slows walking a little too. |
| `PhysicsJumpStrength` | number | 1 | Multiplies the jump: 2 jumps harder, 0.5 jumps lower. How firmly players stand doesn't change. To turn jumping off, use `SetCanJump(false)`. |
| `PhysicsHandPositionStrength` | number | 1 | Multiplies how strongly the hands are pulled to the controllers: lower is floppier, higher is stiffer. |
| `PhysicsHandRotationStrength` | number | 1 | The same for the hands' rotation. |
| `PhysicsHandSpringiness` | number | 10 | Higher makes the hands bouncier, lower makes them settle sooner. |
| `PhysicsGrappleRange` | number | 512 | How far the grapple reaches, in metres. |
| `PhysicsGrappleReelSpeed` | number | 1 | Multiplies how fast holding grip reels the grapple in. |
| `PhysicsGrappleSpringiness` | number | 10 | Higher makes the grapple's rope bouncier. It applies from the next rope fired. |
| `PhysicsSettingsLocked` | boolean | false | Locks the settings in this table. See [Locks](#how-settings-are-applied). |

## Player count

How many players share an instance of your world isn't a page setting: `MaxOccupancy` does nothing. The server decides it when someone joins, from:

1. Your world's **Capacity** on altvr.app (**Edit world**). This wins when it's set.
2. Otherwise, a tag in your page's `<head>`: `<meta name="sq-maxoccupancy" content="8">`, or `<meta name="sq-singleuser">` for one player per instance. The server reads it from your published page and remembers it for about an hour, so a change can take that long to apply.
3. Otherwise, 20.

## Not available yet

`BS.SceneSettings` still has these, and `SetSettings` sends them, but they don't do anything in the app at the moment:

- `EnableHandHold`, `EnableRadar`, `EnableFriendPositionJoin`, `EnableDefaultTextures`, `EnableDevTools`, `EnableQuaternionPose`, `EnableControllerExtras`.
- `EnablePortals`. Portals you place always work.
- `PhysicsGorillaMode`.
- `PhysicsFreeFallAngularDrag`. It's applied to the player's body, but the body never tumbles in the app, so there's nothing to see.
- `MaxOccupancy`: see [Player count](#player-count).

## Scene Physics Methods

```js
await scene.Gravity(new BS.Vector3(0, -9.8, 0));   // the default
await scene.TimeScale(0.5);                         // half speed; 1 is normal

// Cast a ray: origin, direction, distance, and an optional layer mask (all layers by default)
const hit = await scene.Raycast(new BS.Vector3(0, 1, 0), new BS.Vector3(0, -1, 0), 100);
if (hit) {
    const [, id, px, py, pz, nx, ny, nz] = hit.split("¶");
    const point = new BS.Vector3(Number(px), Number(py), Number(pz));
    const normal = new BS.Vector3(Number(nx), Number(ny), Number(nz));
    const object = scene.objects[id];   // the GameObject that was hit, if your page knows it
}
```

Gravity and the time scale go back to normal, (0, -9.8, 0) and 1, when the player leaves the space.

`Raycast` resolves with `null` when the ray hits nothing. On a hit it resolves with a string of `¶`-separated values: a command code, the id of the object hit, then the hit point's x, y, z and the surface normal's x, y, z.

## Player Control Methods

Turn the local player's abilities off and on:

```js
await scene.SetCanMove(false);   // pass true to allow it again
```

| Method | With `false`, players can't | In SDK Play Mode |
|---|---|---|
| `SetCanMove` | Move with the stick (WASD on desktop) | Yes |
| `SetCanRotate` | Turn with the stick, or with the mouse on desktop | Yes |
| `SetCanCrouch` | Crouch with the stick (Ctrl or Shift on desktop) | No |
| `SetCanTeleport` | Teleport | No |
| `SetCanGrapple` | Use the grapple | No |
| `SetCanJump` | Jump | No |
| `SetCanGrab` | Grab things | Yes |

They stay as you set them, so switch them back on when your space no longer needs them.

```js
// Teleport the local player: position, Y rotation in degrees, stop their movement
await scene.TeleportTo(new BS.Vector3(0, 5, 0), 90, true);

// Launch them upwards at 5 metres per second
await scene.AddPlayerForce(new BS.Vector3(0, 5, 0), BS.ForceMode.VelocityChange);

// Buzz the left controller: strength 0 to 1, for 0.1 seconds
await scene.SendHapticImpulse(0.5, 0.1, BS.HandSide.LEFT);
```

- **`TeleportTo`** works in the app and in SDK Play Mode. It takes a fourth argument, `isSpawn`, which has no effect. In the app, if your space has no `SpawnPoint`, the first `TeleportTo` while it's still loading becomes its spawn point.
- **`AddPlayerForce`** only moves players in the app. With `VelocityChange` or `Acceleration` every part of the player gets the same push. `Impulse` and `Force` are shared out over the whole player by weight, 76 kg in all, so they need much bigger values.
- **`SendHapticImpulse`** buzzes a VR controller. Nothing happens on desktop or in SDK Play Mode.

## Input Blocking & Controller Events

Stop the app using a control, so you can use it yourself:

```js
await scene.SetBlockLeftThumbstick(true);   // false to unblock
```

| Method | What it stops in the app, on VR controllers |
|---|---|
| `SetBlockLeftThumbstick` | Moving (on desktop too, and in SDK Play Mode) |
| `SetBlockRightThumbstick` | Turning and crouching with the right stick |
| `SetBlockRightPrimary` | Jumping (the A button) |
| `SetBlockLeftSecondary` | Teleporting |
| `SetBlockLeftTrigger`, `SetBlockRightTrigger` | That hand's trigger, for held objects, and its grapple (on desktop too) |
| `SetBlockLeftPrimary` | Nothing (on a gamepad, teleporting) |
| `SetBlockRightSecondary`, `SetBlockLeftThumbstickClick`, `SetBlockRightThumbstickClick` | Nothing yet |

Like the abilities above, blocks stay until you lift them. Blocking a control only stops the app using it: your page still gets the events below.

### Controller Input Events

The app sends your page the local player's VR controller input. They don't fire on desktop or in SDK Play Mode.

```js
// A button went down or up
scene.On("button-pressed", (e) => {
    console.log(e.detail.button, e.detail.side);
});
scene.On("button-released", (e) => {
    console.log(e.detail.button, e.detail.side);
});

// A thumbstick moved
scene.On("controller-axis-update", (e) => {
    console.log(e.detail.hand, e.detail.x, e.detail.y);
});

// A trigger moved
scene.On("trigger-axis-update", (e) => {
    console.log(e.detail.hand, e.detail.value);
});
```

| Event | `detail` |
|---|---|
| `button-pressed`, `button-released` | `button`: a `BS.ButtonType` (`TRIGGER`, `GRIP`, `PRIMARY` (A or X), `SECONDARY` (B or Y), `THUMBSTICKCLICK`). `side`: a `BS.HandSide` (`LEFT`, `RIGHT`). |
| `controller-axis-update` | `hand`: a `BS.HandSide`. `x` and `y`: the thumbstick, -1 to 1 (left/right, down/up). Sent when the stick moves by more than 0.01. |
| `trigger-axis-update` | `hand`: a `BS.HandSide`. `value`: how far the trigger is pressed, 0 to 1. Sent when it changes by more than 0.01. |

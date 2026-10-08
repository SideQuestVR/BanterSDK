# User & Multiplayer

## UserData

Information about connected users:

```js
const user = scene.localUser;

user.uid;       // The player's id: the same on every visit, and the key of scene.users
user.id;        // The player's session id for this visit (one-shots name their sender by it)
user.name;      // Display name
user.color;     // A colour as hex text: "4488FF" for you, "AAAAAA" for everyone else
user.isLocal;   // Is this the local player
user.props;     // Their user state, as strings
user.state;     // The same user state as real JSON values
```

Use `uid` to tell players apart. On your own page, your `id` can read `"local"` until you've joined the
room. `color` isn't the avatar's colour. [Multiplayer](../multiplayer/overview.md#players) has more on
players.

## Attaching Objects to Users

Attach objects to the player with the `AttachedObject` component (see [AttachedObject](../components/vr-interaction.md#attachedobject)).
An attachment always goes on the **local player**, whatever `uid` you give it: `Attach()` on a player's
own page attaches to that player. Pass `"me"` (or `scene.localUser.uid`).

```js
// A hat in your scene that whoever clicks it puts on
// (it needs a BS Object Id to be found, and a collider on the UI layer to be clicked)
const hat = await scene.Find("Hat");
const attached = await hat.AddComponent(new BS.AttachedObject({
    uid: "me",
    avatarAttachmentPoint: BS.AvatarBoneName.HEAD,
    attachmentPosition: new BS.Vector3(0, 0.15, 0), // offset from the head
    autoSync: true                                  // show it on your avatar for everyone else too
}));

let wearing = false;
hat.On("click", async () => {
    if (wearing) return;
    wearing = true;
    await attached.Attach("me");
});

// Take it off again
async function takeOffHat() {
    wearing = false;
    await attached.Detach("me");
}
```

| Setting | What it does |
|---|---|
| `avatarAttachmentPoint` | Where the object goes: a `BS.AvatarBoneName`. `HEAD` and `NECK` follow the head, the hand, forearm and finger bones follow that hand, and every other bone follows the body. The object follows it exactly. |
| `attachmentPosition`, `attachmentRotation` | The object's offset from that point. |
| `avatarAttachmentType` | `BS.AvatarAttachmentType.AttachToAvatar` (the default) puts the object on the player; `AvatarAttachTo` puts the player on the object, like a seat (leave `attachmentType` at its default for that). |
| `autoSync` | Others see the object on your avatar too. Their copy of the object is moved, so it has to exist for every player with the same [BS Object Id](../multiplayer/overview.md#bsobjectid). |
| `autoAttach` | Attach as soon as the component starts. |

Attaching an object that is already attached moves it to the new settings.

In Play mode without local multiplayer, only head attachments and seats are shown; other attachment points
leave the object where it is.

## State Synchronization

```js
// Set shared public state (strings)
scene.SetPublicSpaceProps({
    "gameScore": "100",
    "currentRound": "3"
});

// ...or as real JSON, with nesting and awaited errors
await scene.SpaceStateSet("game", { score: 100, round: 3 });

// Listen for changes
scene.On("space-state-changed", (e) => {
    e.detail.changes.forEach(change => {
        console.log(change.property, "changed to", change.newValue);
    });
});

// Same updates, real values, with the full path and a delete flag
scene.On("space-state", (e) => {
    e.detail.changes.forEach(c => console.log(c.path, "=", c.value));
});

// Send a message to everyone else in the space (an object is sent as JSON)
await scene.OneShot({
    type: "player-action",
    data: { x: 1, y: 2 }
});

// Receive messages
scene.On("one-shot", (e) => {
    const sender = Object.values(scene.users).find(u => u.id === e.detail.fromId);
    const message = JSON.parse(e.detail.data); // always a string
    console.log("From:", sender ? sender.name : e.detail.fromId);
    console.log("Data:", message.data);
});
```

A one-shot never comes back to the player who sent it, and `fromId` is the sender's `id`, not their
`uid`. See [One-Shot Events](scene-events.md#one-shot-events) and [Scene API: State Management](scene-api.md#state-management).

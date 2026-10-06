# User & Multiplayer

Who's in the space, attaching objects to users, and keeping state in sync for everyone.

## UserData

Information about connected users:

```js
const user = scene.localUser;

user.id;        // User ID
user.uid;       // Session UID
user.name;      // Display name
user.color;     // Avatar color
user.isLocal;   // Is this the local player
user.props;     // Custom properties
```

## Attaching Objects to Users

Attach objects to a user's body with the `AttachedObject` component (see [AttachedObject](../components/vr-interaction.md#attachedobject)):

```js
// Attach an object to a user's right hand
const attached = await obj.AddComponent(new BS.AttachedObject({
    uid: scene.localUser.uid,
    attachmentType: BS.AttachmentType.RightHand,
    autoAttach: true
}));

// Attach/detach for a specific user at runtime
attached.Attach(scene.localUser.uid);
attached.Detach(scene.localUser.uid);

// Attachment types:
BS.AttachmentType.Head
BS.AttachmentType.LeftHand
BS.AttachmentType.RightHand
BS.AttachmentType.LeftFoot
BS.AttachmentType.RightFoot
BS.AttachmentType.Chest
BS.AttachmentType.Back
```

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

// Send message to all users
scene.OneShot({
    type: "player-action",
    data: { x: 1, y: 2 }
}, true);

// Receive messages
scene.On("one-shot", (e) => {
    console.log("From:", e.detail.fromId);
    console.log("Data:", e.detail.data);
});
```

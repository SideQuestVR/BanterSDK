# Saving Data

What your world can keep after everyone has left, and for how long.

| | Who can write | Who can read | How long it lasts | Use it for |
|---|---|---|---|---|
| [World files](#world-files) | The world's owner, signed in | Everyone | Until you replace or remove them | Things the owner sets up in the world: layouts, settings, a high-score board they curate |
| [Space state](#space-state) | Anyone in the space (protected keys: the owner and moderators) | Everyone in the same instance | 24 hours after the last change | A game in progress that should survive people leaving for a while |
| [User state](#user-state) | Each player, their own | Everyone in the space | Until the player leaves | Live per-player values; not for saving |
| [`localStorage`](#localstorage) | Your page | Your page, on that one device | On that device, until it's cleared | Small per-device conveniences |

There is no built-in way to save a player's own progress between visits. Your page is a web page, so it can keep that on your own server, like any website.

## World files

Named files stored with your world. Anyone in the space can read them, but only the world's owner, signed in to SideQuest, can write them. That makes them the place for things the owner edits in the world and everyone else then sees: arrange the furniture, save it, and every later visitor loads that arrangement.

They live at your world's address. A file you save as `layout` is served at `https://my-world.worldspace.host/__persisted_layout.json`, and stays there through later uploads from the Builder.

### Check first

`BS.PersistFileBridge.info(scene)` says whether this player can save here, without trying. It never throws.

```js
const scene = BS.Scene.GetInstance();
const info = await BS.PersistFileBridge.info(scene);

if (info.canWrite) {
    showSaveButton();
} else {
    console.log("Can't save here:", info.reason);
}
```

| Field | What it is |
|---|---|
| `canWrite` | `true` when this player is signed in and owns the world |
| `signedIn` | Whether the player is signed in to SideQuest |
| `reason` | Why `canWrite` is false, in words you can show the player; `null` when they can write |
| `url` | Your world's address, where the files are served from |
| `worldId`, `slug` | The world's id and its address name |

### Save

```js
const layout = { chairs: 12, stage: "round" };
const result = await BS.PersistFileBridge.set(scene, "layout", JSON.stringify(layout));
console.log("Saved", result.bytes, "bytes at", result.savedAt);
```

`set(scene, name, data)` replaces the whole file with `data`, which must be a string: `JSON.stringify` anything else. It resolves once the file is stored, with `name` (the stored file name), `url`, `bytes` and `savedAt`. It rejects with an `Error` saying why when the save is refused: the player isn't the owner, isn't signed in, or the file is too big.

Saves to the same name run one after another, in the order you made them. A save that fails leaves the previous version in place. Each save is an upload, so save when something changes, not every frame.

### Load

```js
const text = await BS.PersistFileBridge.get(scene, "layout");
const layout = text ? JSON.parse(text) : { chairs: 8, stage: "square" }; // null: never saved
```

`get(scene, name)` resolves with the file's text, or `null` when it has never been saved. Anyone can load.

The files are also ordinary files on your world's own address, so a published page can fetch one directly. That's the better way for anything large:

```js
const response = await fetch("/" + BS.PersistFileBridge.fileNameFor("layout"));
const layout = response.ok ? await response.json() : null; // 404: never saved
```

### List and remove

```js
const files = await BS.PersistFileBridge.list(scene);
for (const file of files) {
    console.log(file.name, file.updated, file.url);
}

await BS.PersistFileBridge.remove(scene, "layout"); // owner only; removing a missing file isn't an error
```

`list` returns every saved file of the world, each with its stored `name` (`__persisted_layout.json`), `updated` time and `url`.

### Names

You pass a short name, and the stored file is always `__persisted_<name>.json`.

- Only letters, digits, `_` and `-` are kept; anything else is dropped, so `"my layout!"` becomes `mylayout`. A name with none of those characters is refused.
- Names are cut to 80 characters.
- `BS.PersistFileBridge.fileNameFor(name)` gives the stored file name for a name.
- The app's own world tools keep their files here too. Don't use the names `scenes`, `script_graphs` or `shanes_editor`, or names starting with `scene_`.

The `.json` ending is fixed, but the contents are whatever string you save.

### Rules and limits

- **Only the owner writes.** Other players, guests and moderators can't, whatever your script does. `info()` tells you before you try.
- **Everyone can read.** The files are public at your world's address. Never save anything secret in one.
- **50 MB** per file.
- **Published worlds only.** The files belong to a world published with the Builder; in a space that isn't one, every call reports that.
- **Not in SDK Play Mode.** In the Editor every call reports "persisted files are not available in this build": `info()` gives it as the `reason`, and the other calls reject with it. Publish the world to try it.

### Visual Scripting

The same files, from a graph. The actions are under `BS > World` and the events under `Events > BS > World`.

| Node | What it does |
|---|---|
| Persist File Set | Saves `Data` (a string) as the file `Name`. Carries straight on; the result arrives on On Persist File Saved. |
| Persist File Get | Loads the file `Name`. Carries straight on; the contents arrive on On Persist File Loaded. |
| On Persist File Saved | A save finished. Outputs `Saved Name`, `Success` and `Error`. Set `Name` to catch only that file, or leave it empty for all. |
| On Persist File Loaded | A load finished. Outputs `Loaded Name`, `Success`, `Data` and `Error`. A file that was never saved gives `Success` true and empty `Data`. Set `Name` to filter, or leave it empty. |

Names follow the same rules as above, and the event's name is the one the graph passed (`layout`, not `__persisted_layout.json`). A save by anyone but the owner gives `Success` false and the reason in `Error`.

## Space state

Space state is the shared state of your space while people are in it: values everyone can read and write, synced to every player. See [Multiplayer](../multiplayer/overview.md) and [State Management](scene-api.md#state-management) for how to use it.

It also outlasts the visit, for a while. The multiplayer server keeps an instance's space state for **24 hours after the last change**, even after everyone has left, so a player who joins that instance the next morning finds the game where it was. After 24 hours without a change it's gone. Each instance of your space has its own state, so players placed in a second instance don't see the first one's.

Treat it as a game in progress, not a save: anything that must last belongs in a [world file](#world-files).

In the Editor, the Local Multiplayer window's **Keep space state** option keeps it between Play sessions the same way.

## User state

Each player's own user state is cleared when they leave the space, so it never carries over to their next visit. Use it for live per-player values (team, score this round) and [world files](#world-files) or your own server for anything that should last.

## localStorage

The space's page is a web page, and `localStorage` works in it. The app keeps it on the device between visits, on Windows and on Quest. But it belongs to that device and app install, not to the player: it doesn't follow them to another headset or PC, everyone who uses that device shares it, and it can be cleared at any time. Use it for small conveniences, such as a remembered volume, and nothing that matters.

In Play mode the page is served from a local address, so its storage is separate from the published world's.

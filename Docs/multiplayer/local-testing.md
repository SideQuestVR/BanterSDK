# Testing Multiplayer Locally

Local multiplayer lets you test your world with up to four players on one computer, before you publish it. It uses Unity's **Multiplayer Play Mode**: each extra player is a second copy of the editor, running the same project. When you press **Play** in your main editor, every player enters Play mode, and they all meet in one room on a relay that runs inside the main editor and follows the live servers' rules.

## What it covers

Each player sees the others and can test, between them:

- **Players:** each one appears to the others as an orb avatar with eyes, a body and hands, named after their player (Main Editor, Player 2…). `scene.users`, `user-joined` and `user-left` behave as they do in the app.
- **[Synced objects](overview.md#synced-objects):** ownership, taking it with `TakeOwnership()`, by grabbing and by walking into objects, handing it over when a player stops.
- **[One-shots](overview.md#one-shots)**, with `fromAdmin` set for the player you make the world's owner.
- **[Space state](overview.md#space-state)**, public and protected, and **[user state](overview.md#user-state)**, with the JSON calls and their errors.
- **[Attachments and seats](overview.md#attachments-and-seats):** hand attachments go on the player's hands, head and neck attachments on the camera, everything else on the torso.
- **Arriving late:** a player can leave and join again to see what a newcomer gets.

The page and Visual Scripting work exactly as they do on their own; there is nothing to change in your world.

## Set it up

1. **Open the Setup panel** (`Creator SDK > Setup`). With local multiplayer available, its checklist has two extra items:

   | Item | | What it does |
   |---|---|---|
   | Multiplayer Play Mode | Optional | **Install** adds Unity's `com.unity.multiplayer.playmode` 2.0.2 package. Only needed to test with more than one player. |
   | Run In Background | Recommended | **Turn on** sets Player Settings > Resolution and Presentation > Run In Background. Each player is a separate editor and only one has focus; without this the others stop updating and their avatars freeze. It changes nothing in the worlds you build. |

2. **Add players.** Open `Window > Multiplayer > Multiplayer Play Mode` and tick the players you want: Player 2, 3 and 4. Each one opens its own editor window, which takes a few seconds.
3. **Save the scene.** The other players load the scene from disk, so they only see saved changes. By default local multiplayer saves your open scenes for you when you press Play.
4. **Press Play in the main editor** once every player's editor shows your scene. A player whose editor is still starting plays an empty scene, in a room of its own.

Control a player by clicking into its Game view; each uses the desktop controls you know from Play mode.

## The Local Multiplayer window

Open it from the Setup panel: under **Tools**, press **Open** on **Local Multiplayer**. In a player's editor it only shows that player's room; the settings live in the main editor.

**Environment** shows the Multiplayer Play Mode package (with **Install** or **Open Multiplayer Play Mode**), whether Ora, the SDK's browser package, is new enough, and a warning with a fix button when Run In Background is off.

**Settings** are saved in the project's `UserSettings/SideQuestLocalMultiplayer.json`, which isn't version-controlled, and every player reads them when Play starts:

| Setting | Default | What it does |
|---|---|---|
| Mode | Auto | **Auto** is on when Multiplayer Play Mode is installed. **On** and **Off** force it. |
| World owner | Main Editor | This player's one-shots arrive with `fromAdmin` set, and they may write protected space state and clear the room. |
| Moderators | none | These players may write protected space state too. |
| Keep space state | On | Keeps the room's space state between Play sessions, as a published space keeps it. |
| Save scenes on Play | On | Saves scenes with unsaved changes when you press Play. Off: you're asked to **Save and Play**, **Play Without Saving** or **Cancel**. |
| Overlay key | F8 | The key that shows and hides the in-game overlay. Any Input System key name, such as F9 or Backquote. |
| Show overlay at start | On | Whether the main editor starts with the overlay open. The other players always start with it closed, because their Game views are small. |

Below them, **Saved space state** says how many rooms have saved state. **Clear Room State** deletes it (during Play, only the world owner can clear it, for everyone in the room), and **Show Folder** opens `Library/SideQuestLocalMultiplayer/space-state`.

**Scenes** warns about scenes that were never saved or have unsaved changes (**Save All** fixes both), and counts the networked objects: synced objects, attached objects and seats. Any whose [BSObjectId](overview.md#bsobjectid) is empty, duplicated or an instance number is listed, because the other players might not find the same object; **Assign Stable Ids** gives each a new, saved ID.

**Live** shows the room during Play: you, the connection, the other players, the diagnostics, and the same buttons as the overlay.

## The overlay

Every player shows an overlay over its Game view. The overlay key (F8) switches it between a panel and a one-line strip with the connection state and the number of players. Clicking the overlay never reaches the world.

| Part | What it shows |
|---|---|
| You | Your player, your role (owner or moderator), your `uid` and session id, and what you're sitting on or wearing. |
| Relay | The connection state and round-trip time, the room, and whether its space state is being kept. |
| Players | Each other player with their role, session id and how often their avatar updates, and what they're sitting on or wearing. |
| Diagnostics | Warnings, such as Run In Background being off or players whose scenes disagree about object IDs, plus a note the first time your world meets a behaviour of the app that may surprise you (for example, `syncPosition` being ignored). |

Its buttons:

- **Rejoin** reloads this player's page, so it leaves and joins again exactly like someone arriving late.
- **Leave** takes this player out of the room without stopping Play; **Join** brings it back.
- **Clear room state** (world owner only) empties the room's space state for everyone. Click it twice within three seconds.

## Rooms and what's kept

- **One room per scene.** The room is named after the scene's file path, so every player has to be playing the same saved scene. A scene that was never saved plays in a separate room, away from the players in yours.
- **Space state is kept between Play sessions** while **Keep space state** is on, and deleted 24 hours after its last change, like a published space's.
- **User state and synced objects start fresh** every Play, as they do when everyone leaves a published space.

## How it differs from the app

- **Avatars are orbs**, tinted per player. `user.color` still has the app's values: `"4488FF"` for you and `"AAAAAA"` for the others.
- **Names are the player slots**, and each slot's `uid` stays the same from one Play to the next.
- **Desktop only:** every player uses the mouse and keyboard, and grabs with the mouse. Only the player's body counts for walking into synced objects; in the app their head, hands and feet do too.
- **No voice**, and no network delay: everything runs on one computer.
- **Roles come from the settings**, not from SideQuest: the world owner and moderators are the slots you pick.
- **The app's own behaviours are kept on purpose**, so what works here works there: one-shots never come back to their sender, `syncPosition` and `kinematicIfNotOwned` are ignored, and so on. The overlay notes each one the first time it happens.
- **Changing a script during Play stops local multiplayer.** Stop Play and press it again.
- **Projects with the FlexaBody package** aren't supported.
- **Each player is a whole editor plus its own browser**, so four players need a capable computer.

If a player can't connect, the overlay and the window say why:

| Message | What to do |
|---|---|
| nothing answered at localhost:42068 … is the main editor in Play? | Press Play in the main editor. A player waits 30 seconds, then loads the page on its own. |
| localhost:42068 is the hub of another project | Another Unity project is in Play. Stop Play there. |
| localhost:42068 has no local multiplayer relay | Local multiplayer is off in the main editor, or Ora there is older than 0.1.0. |

## Playing without local multiplayer

With local multiplayer off, or Multiplayer Play Mode not installed and **Mode** on **Auto**, Play mode has just you:

| Feature | In Play mode on your own |
|---|---|
| Players | Only you, with a random name, `id`, `uid` and colour. |
| One-shots | Go nowhere. |
| `SetPublicSpaceProps`, `SetProtectedSpaceProps` | The page gets no `space-state-changed`. Visual Scripting's On Space State Properties Changed still fires. |
| JSON space and user state (`SpaceStateSet`, `UserStateSet`…) | Reject with the code `app_unavailable`. |
| `SetUserProps` | Updates you, and `user-state-changed` fires. |
| Synced objects | Physics runs as authored; nothing is shared. `TakeOwnership()` does nothing, and Do I Own is `false`. |
| Attachments | Only attachments to the head, and seats. |

To get working space state and user state while playing on your own, set **Mode** to **On**: the main editor then joins a room by itself.

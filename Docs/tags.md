# Tags

Tags let your scripts and triggers tell objects apart. A built space stores an object's tag as its
position in the tag list, and the client reads that position against its own list, so your project
has to have exactly the client's tags, in the client's order. **Creator SDK > Setup** sets them up
(the "SDK layers and tags" item) and removes any other tags; the Builder's checklist flags objects
that still use one, with a button to fix them.

## The Tag List

| Tag | What it's for |
|---|---|
| Untagged, Respawn, Finish, EditorOnly, MainCamera, Player, GameController | Unity's built-in tags. Objects tagged EditorOnly are left out of the build. |
| UserTag1 – UserTag32 | Yours. Tag your own objects with these and compare against them in your scripts. |
| BSLocalCharacter | The local player's body. Portals, teleporters and trigger volumes look for it. |
| BSLocalCharacterLeftHand, BSLocalCharacterRightHand | The local player's physics hands. |
| BSLocalCharacterHead | The local player's head (`BS.PlayerTag.HEAD` in JavaScript). |
| BSLocalCharacterFeet | The local player's feet: the rolling ball they move on. |

Only the local player carries the BSLocalCharacter tags; don't put them on your own objects.

## Detecting the Player

- A trigger collider that checks for `BSLocalCharacter` fires when the local player walks in. In
  Visual Scripting, the GettingStarted sample's `isColliderLocalPlayer` subgraph does the check.
- Touching a synced object with the body, either hand, the head or the feet takes ownership of it.
- In JavaScript, `collision-enter` and `trigger-enter` events carry the other object's tag in
  `e.detail.tag`, and `BS.IsPlayerTag(tag)` is true for the player's head.

## Old Tags

Older spaces used `__BA_` tags. `__BA_UserTag0`–`__BA_UserTag14` are now `UserTag1`–`UserTag15`;
`__BA_LocalPlayer`, `__BA_PlayerLeftHand`, `__BA_PlayerRightHand`, `__BA_PlayerHead` and
`__BA_LocalPlayerFeet` are now the matching BSLocalCharacter tags; the rest are gone. The Builder's
checklist renames them on your objects when you click its fix.

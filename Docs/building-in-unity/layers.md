# Layers

Layers decide what the player can stand on, teleport to, grapple, grab and bump into, and what the
player's camera draws. Unity stores an object's layer as a number, so your world has to use the
same slots as the client: **Creator SDK > Setup** names slots 3 and 6–24 for you and removes any
other layer names (the "SDK layers and tags" item). Slots 25 and above belong to the client; the
Builder's checklist flags objects on them, with a button to move them to Default.

## The Layer List

| Slot | Layer | What it's for |
|---|---|---|
| 0 | Default | Ordinary world geometry. |
| 1 | TransparentFX | Unity built-in. For movement it counts like Default. |
| 2 | Ignore Raycast | Unity built-in. Ground and grapple use it; teleport doesn't. |
| 3 | UserLayer1 | Yours. Solid and walkable, and you can teleport onto it, but the grapple ignores it. |
| 4 | Water | Unity built-in. For movement it counts like Default. |
| 5 | UI | World-space UI that the pointer clicks. The player's body and hands pass through it. |
| 6–12 | UserLayer2–8 | Yours. Ground, teleport and grapple all use them, but the player's body and hands pass through. |
| 13–19 | UserLayer9–15 | Yours. Teleport, grapple and ground ignore them; the player's body and hands still bump into them. |
| 20 | Grabbable | Things the player can pick up. `BSGrabbable` puts its object here for you. |
| 21 | Invisible | Hidden from the player's own view (mirrors still show it). Still solid. |
| 22 | Menu | The client's menus. Recording cameras leave it out; the player's body and hands pass through it. |
| 23 | CharacterColliders | The local player's body: the rolling ball, torso and head. Don't put your own objects here. |
| 24 | CharacterHandColliders | The local player's physics hands. Don't put your own objects here. |

From JavaScript, use the `BS.L` names, e.g. `new BS.GameObject({ layer: BS.L.UserLayer9 })` (see
[BSLayers (BS.L)](../reference/enums.md#bslayers-bsl)).

## Movement and Collisions

What works on each layer (✓ = yes):

| | Default, TransparentFX, Water | Ignore Raycast | UserLayer1 | UserLayer2–8 | UserLayer9–15 | Grabbable | Invisible | UI, Menu |
|---|---|---|---|---|---|---|---|---|
| Stand on it (ground) | ✓ | ✓ | ✓ | ✓ | | ✓ | | |
| Teleport onto it | ✓ | | ✓ | ✓ | | | | |
| Grapple to it | ✓ | ✓ | | ✓ | | | | |
| Grab it | | | | | | ✓ | | |
| Body bumps into it | ✓ | ✓ | ✓ | | ✓ | ✓ | ✓ | |
| Hands bump into it | | | | | ✓ | ✓ | ✓ | |

- **Grab** only ever looks at Grabbable, so a grabbable object needs a collider on that layer.
- **Body** is the CharacterColliders layer and **hands** are CharacterHandColliders. The hands
  only touch UserLayer9–15, Grabbable and Invisible; they pass through everything else.
- **Invisible** objects are hidden from the player's camera, and from the hand camera and
  recordings, which copy it. They still collide, so an Invisible collider makes an invisible wall.

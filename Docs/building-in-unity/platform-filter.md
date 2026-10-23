# Platform Filter (BSPlatformFilter)

Shows a GameObject, and everything under it, only to the players you choose: on PC or on Android, in VR or on a flat screen, or any combination of the two. Add it with **Add Component > BS > Platform Filter**, or search for *Platform Filter* (one per GameObject). Scripts can add it too, as `BS.PlatformFilter`.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `includeOnMobile` | bool | `true` | Show on Android (Quest) |
| `includeOnDesktop` | bool | `true` | Show on PC (Windows) |
| `includeInVR` | bool | `true` | Show to players in a headset |
| `includeInNonVR` | bool | `true` | Show to players on a flat screen |

## Combining the Two Halves

The filter has two halves: the platform (`includeOnMobile`, `includeOnDesktop`) and the mode (`includeInVR`, `includeInNonVR`). An object shows only when **both** halves let it through, and within a half any ticked box is enough.

| Wanted | Mobile | Desktop | VR | Non-VR |
|--------|:------:|:-------:|:--:|:------:|
| Everyone (the default) | ✓ | ✓ | ✓ | ✓ |
| PC only | | ✓ | ✓ | ✓ |
| Android (Quest) only | ✓ | | ✓ | ✓ |
| VR only, any platform | ✓ | ✓ | ✓ | |
| Flat screens only | ✓ | ✓ | | ✓ |
| Android + VR only | ✓ | | ✓ | |
| PC + flat screen only | | ✓ | | ✓ |

Unticking both boxes in one half hides the object from everyone.

## When It Is Applied

- **Building a space bundle:** each platform is built separately, so the platform half is settled there. An object excluded from a platform is left out of that platform's bundle with its whole subtree, and a nested filter cannot bring a child of an excluded ancestor back.
- **On the player's device:** whether a player is in VR is only known on their device, so a filter that restricts VR or Non-VR ships with the object and hides it when it starts. A filter that only restricts the platform has done its job in the build and is stripped from it.
- **Objects made by scripts:** the filter is checked when it is added, and again whenever one of its properties changes.

Hiding switches the GameObject off (`SetActive(false)`). The filter only switches back on what it switched off itself; an object you deactivated stays deactivated.

In the Unity editor's Play mode the filter judges you as a PC player (the editor never counts as Android, whatever the build target), and as a VR player only while a headset is active. Outside Play mode you see everything, so filtered objects can still be edited.

## Example: Platform-Specific Detail

Keep an expensive decoration on PC and a cheap stand-in on Quest with two sibling GameObjects at the same position:

1. `FountainHighPoly`: the full model. Add a Platform Filter, untick **includeOnMobile**.
2. `FountainMobile`: a low-poly mesh with a baked texture. Add a Platform Filter, untick **includeOnDesktop**.

Windows builds ship only `FountainHighPoly`; Android (Quest) builds ship only `FountainMobile`.

## Example: VR-Only Hint From a Script

```js
// A "grab the lever" hint, only for Quest players in a headset.
const hint = new BS.GameObject("LeverHint");
await hint.AddComponent(new BS.PlatformFilter({ includeOnDesktop: false, includeInNonVR: false }));

// Later, show it to everyone in VR on any platform:
const filter = hint.GetComponent(BS.CT.PlatformFilter);
filter.includeOnDesktop = true;
```

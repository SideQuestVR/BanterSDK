# Platform Filter (BSPlatformFilter)

A Unity Editor component that includes or excludes a GameObject per platform at build time. Add it with **Add Component > BS > Platform Filter**, or search for *Platform Filter* (one per GameObject). It is not part of the JS API — the component never ships and is invisible to scripts by design.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `includeOnMobile` | bool | `true` | Ship this GameObject and all its children in mobile (Quest/Android) builds |
| `includeOnDesktop` | bool | `true` | Ship this GameObject and all its children in desktop (Windows) builds |

## Build-Time Semantics

- Platform unchecked: the GameObject **and its entire subtree** are stripped from that platform's build.
- Platform checked: only the filter component itself is stripped — the marker never ships.
- A filter nested under an excluded ancestor cannot re-include anything; the whole ancestor subtree is already gone.
- Both boxes unchecked: the object ships on no platform.
- Filtering happens only at build time. Play mode in the editor always shows everything.

## Example: Platform-Specific Detail

Keep an expensive decoration on desktop and a cheap stand-in on mobile with two sibling GameObjects at the same position:

1. `FountainHighPoly` — the full model. Add a Platform Filter, untick **includeOnMobile**.
2. `FountainMobile` — a low-poly mesh with a baked texture. Add a Platform Filter, untick **includeOnDesktop**.

Windows builds ship only `FountainHighPoly`; Android (Quest) builds ship only `FountainMobile`. In the editor you see both — deactivate one while working if you like, since filters apply even to inactive objects.

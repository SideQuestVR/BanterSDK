# Audio Components

## AudioSource

Plays sound from the object. It adds a Unity Audio Source if the object doesn't have one.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `volume` | number | 1 | Volume, 0 to 1 |
| `pitch` | number | 1 | Playback speed; above 1 is higher and faster |
| `mute` | boolean | false | Silence the source |
| `loop` | boolean | false | Loop the clip |
| `playOnAwake` | boolean | true | Play the clip when the object is enabled |
| `spatialBlend` | number | 0 | 0 = 2D (the same everywhere), 1 = 3D (from the object's position) |
| `bypassEffects` | boolean | false | Skip effects on this source |
| `bypassListenerEffects` | boolean | false | Skip the listener's global effects |
| `bypassReverbZones` | boolean | false | Skip reverb zones |

The default `spatialBlend` is 0, so a sound plays at full volume wherever the player stands. Set it to 1 for a sound that comes from the object.

<div class="docs-tabs">

```js
const audio = await obj.AddComponent(new BS.AudioSource({
    volume: 0.8,
    loop: false,
    spatialBlend: 1        // 3D: heard from the object's position
}));
audio.PlayOneShotFromUrl("https://example.com/sounds/chime.mp3");
```

![BS Audio Source in the Unity Inspector](../images/components/audio-source.png)

</div>

**Methods:**

```js
audio.PlayOneShotFromUrl("https://example.com/sound.mp3"); // Download a sound and play it once
audio.Play();                                               // Play the Audio Source's own clip
audio.PlayOneShot(0);                                       // Play clip 0 from the component's Clips list
```

A script can only supply sound by URL, through `PlayOneShotFromUrl`. The URL has to end in `.mp3`, `.wav` or `.ogg` (a query string after it is fine); each sound is downloaded once and kept, so playing it again is instant. The Promise resolves once the sound has been downloaded and started, and rejects if the download fails.

`Play()` and `PlayOneShot(index)` play clips set in the Inspector: `Play()` the **AudioClip** of the object's Audio Source, `PlayOneShot(index)` an entry in the BS Audio Source's **Clips** list. On a component created from a script they have nothing to play. To use them, set up the object in the editor and [find it from your script](overview.md#using-objects-placed-in-the-editor):

```js
const scene = BS.Scene.GetInstance();
const bell = await scene.Find("Bell");
const bellAudio = bell.GetComponent(BS.CT.AudioSource);
bellAudio.Play();
```

When the object already has an Audio Source (it holds the clip), a BS Audio Source added in the Inspector keeps that Audio Source's settings and reads them back into its properties. `playOnAwake` takes effect the next time the object is enabled.

## Volume and mixers

When a world loads in the app, every Audio Source in its scene, on inactive objects too, is routed through the app's own mixer, so each player's volume settings apply to it:

| Audio Source | Follows |
|--------------|---------|
| On an object with `music` in its name, in any capitals (`LobbyMusic`), or directly under one | **Master Volume** and **Music Volume** |
| Any other | **Master Volume** and **Gameplay Volume** |

A mixer of your own keeps working: its groups, effects and snapshots still apply, and its output goes on into the app's mixer. A mixer has one output, so it follows one of the rows above, chosen by the first Audio Source that uses it. An Audio Source on it that belongs to the other row plays straight through the app's mixer instead: it follows its own slider, but your mixer's groups and effects don't apply to it. To keep your effects on both, put music and sound effects on separate mixers. An Audio Source on a mixer group with `music` in its name counts as music too. If a mixer can't be joined to the app's, its Audio Sources play straight through the app's mixer instead.

A BS Video Player with **Route Audio Through Audio Source** on follows the same rule. An Audio Source that a script adds after the world has loaded isn't routed, so players' volume settings don't change it.

Play mode doesn't show any of this: it plays your scene as you built it, mixer included. To hear what players hear, [visit your world](../building-in-unity/publishing.md#visiting-your-world) in the app.

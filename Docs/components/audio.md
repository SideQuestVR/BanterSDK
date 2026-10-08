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

A script can only supply sound by URL, through `PlayOneShotFromUrl`. The URL has to end in `.mp3`, `.wav` or `.ogg` (a query string after it is fine); each sound is downloaded once and kept, so playing it again is instant. The Promise resolves when the download starts, not when the sound has played.

`Play()` and `PlayOneShot(index)` play clips set in the Inspector: `Play()` the **AudioClip** of the object's Audio Source, `PlayOneShot(index)` an entry in the BS Audio Source's **Clips** list. On a component created from a script they have nothing to play. To use them, set up the object in the editor and [find it from your script](overview.md#using-objects-placed-in-the-editor):

```js
const scene = BS.Scene.GetInstance();
const bell = await scene.Find("Bell");
const bellAudio = bell.GetComponent(BS.CT.AudioSource);
bellAudio.Play();
```

When the object already has an Audio Source (it holds the clip), a BS Audio Source added in the Inspector keeps that Audio Source's settings and reads them back into its properties. `playOnAwake` takes effect the next time the object is enabled.

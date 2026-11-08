# Legacy APIs

These scene methods predate the current component APIs and still work, so older spaces keep running. Prefer the modern equivalent named in each row.

| Method | Description |
|--------|-------------|
| `LegacyAttachObject(object, whoToShow, part)` | Attaches an object to the local player's head, body or a hand (`BS.LegacyAttachmentPosition.HEAD`, `BODY`, `LEFT_HAND`, `RIGHT_HAND`), keeping its current local offset. Pass `"me"` as `whoToShow`: attachments only ever go to the local player. Prefer the `AttachedObject` component. |
| `LegacyLockPlayer()` | Freezes player movement. Prefer `SetCanMove(false)`. |
| `LegacyUnlockPlayer()` | Restores player movement. Prefer `SetCanMove(true)`. |
| `LegacySetRefreshRate(rate)` | Sets the headset refresh rate (Quest only). Prefer `SceneSettings.RefreshRate`. |
| `LegacySetVideoUrl(object, url)` | Plays a URL from the start on the first Unity `VideoPlayer` on the object or its children. The object must already be linked (`await object.Async()`). Prefer the `VideoPlayer` component. |

## Unsupported calls

These scene methods are left over from older spaces. They have nothing behind them, in the app or in Play mode, so they never do anything. The Unity log warns the first time a world calls each one.

| Method | What your script sees |
|--------|-----------------------|
| `AiImage`, `AiModel`, `Base64ToCDN`, `SelectFile` | The Promise rejects with a message containing `unsupported`. |
| `StartTTS`, `StopTTS`, `SendBrowserMessage` | The Promise resolves, so a page that doesn't wait for an answer carries on, and the page's console warns `… is not supported in this client.` |

The Visual Scripting nodes **Select file (GLB/JPG/PNG)** and **Base64 To CDN** do nothing either.

`OpenPage` (and the **Menu Browser Open URL** node) works in the app, where it asks the player before opening a site in their menu browser: see [Opening a Website](../javascript-api/scene-api.md#opening-a-website). In Play mode it behaves like `StartTTS` above.

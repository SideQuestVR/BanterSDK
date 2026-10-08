# Legacy APIs

These scene methods predate the current component APIs and still work, so older spaces keep running. Prefer the modern equivalent named in each row.

| Method | Description |
|--------|-------------|
| `LegacyAttachObject(object, whoToShow, part)` | Attaches an object to the local player's head, body or a hand (`BS.LegacyAttachmentPosition.HEAD`, `BODY`, `LEFT_HAND`, `RIGHT_HAND`), keeping its current local offset. Pass `"me"` as `whoToShow`: attachments only ever go to the local player. Prefer the `AttachedObject` component. |
| `LegacyLockPlayer()` | Freezes player movement. Prefer `SetCanMove(false)`. |
| `LegacyUnlockPlayer()` | Restores player movement. Prefer `SetCanMove(true)`. |
| `LegacySetRefreshRate(rate)` | Sets the headset refresh rate (Quest only). Prefer `SceneSettings.RefreshRate`. |
| `LegacySetVideoUrl(object, url)` | Plays a URL from the start on the first Unity `VideoPlayer` on the object or its children. The object must already be linked (`await object.Async()`). Prefer the `VideoPlayer` component. |

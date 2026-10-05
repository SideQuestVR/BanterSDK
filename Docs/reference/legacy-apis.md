# Legacy APIs

These scene methods predate the current component APIs and are kept so older spaces keep working. Prefer the modern equivalent named in each row.

| Method | Description |
|--------|-------------|
| `LegacyAttachObject(object, whoToShow, part)` | Attaches an object to a player body position (`BS.LegacyAttachmentPosition`); `whoToShow` selects the user. Prefer the `AttachedObject` component. |
| `LegacySetChildColor(object, color, path)` | Tints a child renderer found by path. Prefer the `Material` component. |
| `LegacyLockPlayer()` | Freezes player movement. Prefer `SetCanMove(false)`. |
| `LegacyUnlockPlayer()` | Restores player movement. Prefer `SetCanMove(true)`. |
| `LegacySetRefreshRate(rate)` | Sets the headset refresh rate. Prefer `SceneSettings.RefreshRate`. |
| `LegacySitPlayer(object)` | Seats the player on an object. |
| `LegacyUnsitPlayer()` | Stands the player back up. |
| `LegacyGorillaPlayer()` | Enables gorilla-style arm locomotion. Prefer `SceneSettings.PhysicsGorillaMode`. |
| `LegacyUngorillaPlayer()` | Disables gorilla-style arm locomotion. |
| `LegacyEnableControllerExtras()` | Enables the extra controller event stream. Prefer `SceneSettings.EnableControllerExtras`. |
| `LegacyEnableQuaternionPose()` | Enables quaternion pose updates. Prefer `SceneSettings.EnableQuaternionPose`. |
| `LegacySetVideoUrl(object, url)` | Points an object's video playback at a URL. Prefer the `VideoPlayer` component. |
| `LegacySendAframeEvent(id, isOn, path)` | Sends an A-Frame style event into the app. |
| `PlayAvatar(object, session, audio, avatar)` | Plays a recorded avatar performance on an object. |
| `LegacyPlayAvatar(object, session, audio, avatar)` | Older name for `PlayAvatar`. |
| `LegacyRequestOwnership(id)` | Requests network ownership of a synced object. See the `SyncedObject` component. |
| `LegacyDoIOwn(id)` | Asks whether the local user owns a synced object. |
| `LegacyResetNetworkObject(id)` | Resets a networked object to its original state. |

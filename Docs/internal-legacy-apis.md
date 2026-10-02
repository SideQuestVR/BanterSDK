# Internal & Legacy APIs

## Internal Scene Methods

These keep the SDK running and are documented for completeness; spaces should not call them.

| Method | Description |
|--------|-------------|
| `SetLoadPromise(promise)` | Registers the promise that gates the scene `loaded` event; warns and ignores a second call. |
| `FireUnityLoaded()` | Waits for Unity load plus one frame, then dispatches `unity-loaded` on the scene. Called once from the constructor. |
| `SetJsObjectID(obj)` | Reports a GameObject's JS-side id to Unity so the two sides stay linked. |
| `SetJsComponentID(comp)` | Reports a Component's JS-side id to Unity so the two sides stay linked. |
| `UpdateObject(unityId)` | Asks Unity to re-send an object's full state. |
| `InlineCrawl(gameObject)` | Asks Unity to enumerate an existing native object's children into the JS scene graph. |
| `InlineObject(gameObject, path)` | Links one existing native child at `path` under the object; resolves with its GameObject. |
| `Emit(message)` | Writes a raw message to the Unity message bus, bypassing request tracking. |
| `EnableLegacy()` | Switches on the legacy message pipeline for old spaces. |
| `SetProp(propType, props, id?)` | Shared implementation behind `SetPublicSpaceProps`, `SetProtectedSpaceProps` and `SetUserProps`. |
| `SpaceStateGet(path)` | Read one dotted path; resolves `undefined` when absent. |
| `SpaceStateGetAll()` | `{revision, public, protected}` — real JSON, keyed by full path. |
| `SpaceStateSet(path, value, opts?)` | Replace the value at `path`. `opts.protected` writes the protected scope. |
| `SpaceStateMerge(path, obj, opts?)` | Merge into `path`, leaving unnamed leaves alone. |
| `SpaceStateDelete(path, opts?)` | Remove `path` and every descendant. |
| `GetSpaceStateTree(scope?)` | Nested view of the local mirror. |
| `UserStateGet(key, userId?)` | Read your own prop, or another participant's. |
| `UserStateGetAll(userId?)` | Every prop of a participant. |
| `UserStateSet(key, value, opts?)` | Set one of your own props. `opts.moderatorsCanWrite` opens it to moderators. |
| `UserStateDelete(key, opts?)` | Remove one of your own props. |
| `_t(eventName, props)` | Sends a telemetry event. |
| `getUIMessageHandler()` | Returns the internal UI-system message handler. |
| `getInstance()` | Deprecated alias of `GetInstance()`; logs a warning and forwards. |

## Legacy Scene Methods

These predate the current component APIs and are kept so old spaces keep working; prefer the modern equivalent named per row.

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
| `PlayAvatar(object, session, audio, avatar)` | Plays a recorded avatar performance on an object (current command name, legacy pipeline). |
| `LegacyPlayAvatar(object, session, audio, avatar)` | Same as `PlayAvatar` via the older command name. |
| `LegacyRequestOwnership(id)` | Requests network ownership of a synced object. See the `SyncedObject` component. |
| `LegacyDoIOwn(id)` | Asks whether the local user owns a synced object. |
| `LegacyResetNetworkObject(id)` | Resets a networked object to its original state. |

## Internal GameObject & Component Methods

| Method | Description |
|--------|-------------|
| `GameObject.AddInlineObject(path)` | Instance wrapper for `scene.InlineObject(this, path)`. |
| `GameObject.CrawlInlineObjects()` | Instance wrapper for `scene.InlineCrawl(this)`. |
| `Component.Clone(toClone?)` | Copies another component's serialised state into this one. |
| `Component.Serialise(properties, all)` | Writes the named properties (or all of them) to the wire format; each component type provides its own implementation. |
| `Component.Deserialise(data, shouldUpdate?)` | Applies wire-format data to the component; each component type provides its own implementation. |
| `createDelegate()` | Lazily creates the listener map behind `On`/`Off`/`dispatchEvent`; present on every event target (Scene, GameObject, Component, UserData). |

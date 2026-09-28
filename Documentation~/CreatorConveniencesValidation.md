# Creator Conveniences validation — 2026-09-28

Status: review/test branch, not client-accepted or release-ready.

## Baseline and scope

`feature/creator-sdk-phase1-v2` starts from `6f921bb2` (SDK 4.0.14), the SDK revision pinned by Greenfield Unity `b27fe7a`. The older `feature/creator-sdk-phase1` checkpoint is retained. The standalone SDK tip `6d3246d4` failed a fresh import with CS1061: `ScriptMachine.RestartAfterSwap` is missing from the integration's Visual Scripting revision `86baf42`.

Only the two upstream **editor** guard removals from `6d3246d4` are carried forward: `PersistedWorldFiles.cs` and `RuntimeOverridePrune.cs` must compile in consumer projects because the builder references them outside `GREENFIELD_PROJECT`. The unrelated runtime graph-editor changes are not included. No Creator Conveniences runtime C# API or component was added.

## Findings and contract corrections

- Teleporter user-parent lookup: confirmed incompatible with Greenfield's source hierarchy. `LocalUserService` mounts user data under its service; `BanterSceneEventHandler` separately tags the local torso `__BA_LocalPlayer`. The graph now uses Unity `On Trigger Enter` and that same reserved tag as the SDK Portal, then the existing `TeleportTo` unit.
- Seat user lookup: likely failure contributor, not proven to be the only cause. The tested client's logs report zero re-injected users; its physical click delivery was not captured. The graph now passes the existing local token `me` to `BSAttachedObject._Attach`. Greenfield's `AttachmentsSystem` supports this token with or without a registered network user. Real click delivery and avatar seating remain unverified.
- Seat attachment mode: confirmed broken against the traced Greenfield consumer. `NonPhysics + AvatarAttachTo` does not activate its constraint or seated state. Only `Physics + AvatarAttachTo` calls the existing FlexaMover seat-joint and seated-state path. The prefab now selects Physics in both serialized component/payload fields and includes a kinematic/no-gravity Rigidbody anchor. Move/jump unseat flags are explicit. No native controller changes or new joint logic were introduced.
- Competing startup spawns: the primary test scene already had fixed spawn disabled and radius enabled. Client logs showed radius spawn, not two competing spawn events. The new diagnostic warns if creators actually enable multiple packaged startup spawners, without disabling any.
- Camera ownership: the isolated project's existing Basis tag-only patch is outside this Creator SDK repository. It must not be described as shipped by this SDK commit or verified in the installed client.

## Evidence

Unity 6000.3.21f1, resolved URP 17.3.0, input Both. The private harness builds real encrypted `asset.world` files with Android and Windows64 sections through the existing SDK builder, then decrypts/loads them through Basis in Play Mode. It adds SDK types to a private in-memory content-policy fixture; the source policy asset is not changed. Host camera, click input, trigger messages, and movement/attachment consumers are fixtures. Thus these checks prove serialized graphs and SDK event paths, not physical avatar movement or production policy acceptance.

Passed on the corrected Radius package:

- Exactly one startup spawn event, inside the configured 2m horizontal disk; spawn/stop flags true.
- Seat ignores other-object clicks and emits local `me` attachment, including with zero registered users.
- The reauthored seat selects Physics/AvatarAttachTo, jointAvatar and move/jump unseat flags in its emitted payload. Its packaged Rigidbody is kinematic/no-gravity; measured anchor drift stays below 1mm and 0.1 degrees over at least 50 physics ticks. This is stationary-anchor evidence, not a test of the native torso/seat joint or seated avatar pose.
- Local trigger has no parent `UserData`; the reserved tag produces a destination/facing event with velocity stop true and spawn false.
- Non-user and remote collider messages produce no teleport or graph errors.
- Six immediate local trigger messages produce one event; a later message after the gate reset produces a second.
- Destination remains stable after FaceTarget runs; FaceTarget is on the visual, not the root.

FixedSpawn and CandidateSpawn packages also passed their configured target assertions and the same interaction/event-path checks. CameraConflict passed packaged loading: the imported MainCamera-tagged camera remains enabled/offscreen, its conflicting tag is removed, and the fixture host remains MainCamera. Graphics-enabled overview/portal captures passed the error-color check and were visually inspected; the white seat and portal are visible. This tests the private project's separate Basis patch, not the installed client.

An ordinary consumer-mode import with `GREENFIELD_PROJECT` removed from both platform define lists exited successfully with no compiler errors. Existing upstream compiler warnings remain; this is not a claim of a warning-free Console.

Preflight/menu checks passed:

- All four `GameObject/BS` menu callbacks create proper prefab instances with resolved graphs; Radius defaults to 5 and Destination is preassigned.
- Used unsupported tags warn; unused tags do not. Future entries in the SDK tag dictionary are accepted. Inactive objects are checked; EditorOnly subtrees are skipped.
- Multiple enabled startup spawn graphs warn; disabling one clears the warning.
- Preview inspection preserves the active scene, dirty state, tags, and component-enabled flags.
- Both input is detected. Old-only and New-only warn without rewriting settings.
- Incorrect Windows/Android rendering modes warn in unattended builds without changing renderer assets.
- Interactive tag/input warnings passed both Cancel and Build anyway. Cancel blocks continuation; Build anyway permits it. Neither choice changes tags or input settings, and the private diagnostics restored input Both afterward.
- Earlier interactive renderer tests exercised Change and Build, Build Anyway, Inspect Renderer, and shared-renderer cancellation; 32 discovery/edge assertions passed. These are not device rendering acceptance.

## Remaining acceptance

Rebuild/upload the Radius test package and test a real seat click, sitting/unseating, and physical entry through the teleporter. Confirm local-only behavior with a second client. Fixed/candidate spawn variants must be tested separately. A conflicting-camera client test requires a client built with the separate Basis patch. XR/controller interaction, visible Hierarchy menu/Inspector presentation, all eight original sample scenes' URP conversion, and hosted/multiplayer acceptance are not completed by these tests. Portal image selection is not yet a root-level configuration field.

No Creator Hub/MCP scaffolding, credentials, test-project code, or private dependency binaries belong in this SDK diff.

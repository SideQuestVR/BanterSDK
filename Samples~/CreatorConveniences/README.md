# Creator Conveniences

Import this sample from Package Manager to add the prefabs, Visual Scripting graphs, and editor menu entries to your project. The included graphs call existing Creator SDK units; no custom runtime component is required. The entries are available under `GameObject > BS` (also the Hierarchy's Create GameObject context menu) after importing this sample.

## Spawn Point

`Prefabs/SpawnPoint.prefab` teleports the local player to the object's position and facing when the graph starts, through the SDK's `TeleportTo` unit with `Is Spawn` enabled and velocity stopping enabled. Place and rotate it at the intended spawn location. It requires the normal Creator SDK world/player runtime. This sample does not configure the world's stored default spawn setting.

The graph's Boolean and float defaults use Visual Scripting's typed serialization. A packaged Play Mode regression check verified one event at the configured position with both spawn and velocity-stop flags enabled; this is event-path evidence, not client movement acceptance.

## Spawn Range

`Prefabs/SpawnRange.prefab` has two explicit behaviors. If its `Spawn Candidates` child has one or more children, it selects one uniformly by index and spawns at that marker's world position and facing. Add empty child GameObjects under `Spawn Candidates` and move/rotate them to define choices; zero candidates falls back to radius mode, and one candidate always selects that marker. With no candidate children, it samples a uniformly distributed point in a horizontal XZ disk around the root transform. Set Object Variables `Radius` in the Inspector (default 5 world units; use a nonnegative value). Zero radius selects the root center. Radius mode keeps the root Y position and facing. Both modes call the SDK `TeleportTo` unit with `Is Spawn` and velocity stopping enabled. The graph addresses the candidate container as child index 0; keep the included `Spawn Candidates` container as the root's first child.

It requires the normal Creator SDK world/player runtime. Candidate markers are children of the built-in `Spawn Candidates` container; no external references or tags are needed.

## Seat

`Prefabs/Seat.prefab` contains a visible, editable seat, a BoxCollider interaction area, the SDK `BSAttachedObject` with `isSeat` enabled, and an `On Click` graph that attaches the local user to this object. Move, rotate, or scale the root to place it. Adjust the collider in the Inspector if the seat shape changes.

Its two visible parts use the included `Materials/Seat.mat` (URP Lit). The default visual requires URP, as used by Greenfield. For a Built-in project, assign a compatible material to the two visible children. This changes visuals only, not the attachment graph.

The graph sends the existing local attachment token `me` to `BSAttachedObject._Attach`. Greenfield's attachment consumer resolves that token to the local user when available and supports it without a registered network user. It does not look up or move a remote user. The attachment direction uses `AvatarAttachTo` and `NonPhysics`; confirm sitting and unseating behavior in the client before shipping. The collider only receives clicks when the runtime routes it through the SDK click event. No avatar, controller, or camera implementation is bundled here.

## Local Space Teleporter

`Prefabs/LocalSpaceTeleporter.prefab` teleports the local player when they enter its trigger volume. The root Inspector's Object Variables exposes `Destination`; select that Transform to edit the landing position and facing. The graph checks the existing reserved `__BA_LocalPlayer` collider tag used by Greenfield and the SDK Portal, calls the SDK `TeleportTo` unit, stops velocity, and uses a `Once` gate with a 0.5-second reset. It does not require `UserData` on the collider's parents. It does not open another space or select a Portal URL. Requires the normal Creator SDK world/player runtime and its local collider tagging.

## Build warnings

The scene builder warns about unsupported custom tags **used by scene objects**, comparing against Unity's built-in tags and the SDK's current supported tag list. Future SDK list additions are accepted automatically. It also warns if more than one enabled, active Spawn Point/Spawn Range graph can run at startup. These warnings never rewrite tags or disable objects. Keep only the intended startup spawner enabled; separate spawn configurations are separate test builds.

The renderer check reviews Windows Forward+ and Android Forward. `Change and Build` changes only the listed editable renderer assets with consent; shared conflicting assets must be reviewed manually. Unattended builds log warnings and leave assets unchanged. The Active Input Handling warning is a local Editor Play Mode check, not a fix for a hosted client's input; the separate fix command offers saving scenes and restarting Unity after consent.

## Known limitations

Only colliders tagged `__BA_LocalPlayer` are accepted; unrelated/remote colliders are ignored. Do not assign that reserved tag to arbitrary world objects. Earlier user-parent lookup fixtures passed, but the uploaded client test reported no teleport and no seating. Those fixtures did not match Greenfield's collider hierarchy or its zero-user case. The current graphs address those source mismatches; actual movement, physical clicks, sitting pose and unseating still require a new client test. Neither an event capture nor successful bundle loading is client acceptance.

The corrected graphs passed encrypted-package Play Mode checks on 2026-09-28: a seat click emits the local `me` attachment with zero users; a tagged local collider with no parent user data emits the configured teleport; unrelated/remote colliders are ignored; and the repeat-entry gate resets correctly. Fixed, radius and candidate-spawn variants each emit one configured startup event. See `Documentation~/CreatorConveniencesValidation.md` for proof boundaries and remaining acceptance.

The existing SDK FaceTarget component is on the portal mesh, not the teleporter root. This keeps the visual facing the player without rotating the trigger or the Destination child. Do not move it onto the root: a packaged Play Mode regression test demonstrated that doing so moved the configured landing point as the camera moved.

Validated in Unity 6.3.21f1 / Greenfield Editor: all four included Script Graph assets import with no missing units or unbound value inputs. The menu callbacks were invoked through the editor menu paths, though the menu was not visually inspected. In Play Mode, the local test player selected each of two marker children in separate runs (at 100 and 110 world units); the remote test player remained unchanged. With no markers and radius 5, a final-graph sample landed 4.98 units from the root in XZ. With radius 0, the event target was exactly the root center. With one marker, the target was exactly that marker. The graph is restricted to AOT-allowed calls; an initial `Transform.Find` version was rejected by Unity and replaced with `Transform.GetChild`.

Earlier local-player movement checks used a fixture, not a real hosted world. Some early fixtures also produced teardown errors and extra AudioListener warnings; those are not clean-client acceptance evidence.

Earlier Seat checks captured `BSScene.Click` attachment events, but did not test the uploaded client's zero-user case. A graphics-enabled URP 17.3 capture confirmed that the original Standard seat material rendered pink; the included URP Lit material then rendered the seat without error-colored pixels. Inspector presentation and the visible GameObject menu itself were not visually reviewed. This sample is not release-accepted until the current client interaction matrix passes.

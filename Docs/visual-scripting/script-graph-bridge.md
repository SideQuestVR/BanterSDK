# Advanced: ScriptGraphBridge

`BS.ScriptGraphBridge` is the low-level session API for listing, editing and live-controlling script graphs from JavaScript. It is an advanced API — most creators never need it; use the nodes and `BS.ScriptGraph` instead.

!> **Only in the app.** In SDK Play Mode every call rejects, with a reason that ends `script graph editing is not available in this build`.

All bridge calls are async and return Promises (the `toBase64` / `fromBase64` helpers are synchronous). In the app, a call that fails resolves with an object whose `error` holds the reason (for example `No session 's3'` or `Machine not found`) rather than rejecting, so check for it.

Machines are addressed by a target: `{ bid, machineIndex }` — the object's BS id plus the index of the Script Machine on that object. Each row returned by `list()` describes one machine: `bid`, `machineIndex`, `objectName`, `path`, `active`, `source` (`"embed"` or `"macro"`), `graphTitle`, `unitCount`, `blocked` (the graph's top level contains nodes outside the app's allow list), `paused` and `overridden` (a graph has been applied over the one the world was built with). A target can also carry the row's `path`, which is used when the `bid` doesn't find the object.

| Function | Returns | Description |
|----------|---------|-------------|
| `list(withRefs?)` | `{ machines }` | Enumerate every Script Machine in the scene (including on inactive objects). `list(true)` adds each machine's `graphRef` and `authoredRef` content hashes |
| `open(target)` | view-model | Open an edit session on a machine; resolves with the full graph view-model (`sessionId`, `rev`, `title`, `units`, `connections`, `variables`, `warnings`) |
| `ops(batch)` | view-model | Apply an op batch `{ sessionId, baseRev, ops }` to the session's staging graph. All or nothing: on failure it resolves with `{ error, code: "opFailed", failedOpIndex }`, or `{ error, code: "revMismatch", rev }` when `baseRev` isn't the session's current `rev`, and the staging graph is untouched |
| `save(sessionId)` | envelope | Serialize the session's staging graph into a persistable envelope |
| `apply(sessionId)` | `{ ok, invalidated }` | Swap the session's staging graph onto the live machine. Other sessions open on that machine are closed; their ids are in `invalidated` |
| `applyEnvelope(envelope, target?)` | `{ ok, warnings, invalidated }` | Push a previously saved envelope straight onto a machine: `target`, or the one it was saved from |
| `revertMachine(target)` | `{ ok, reverted, invalidated, machine }` | Put back the graph the world was built with, undoing every apply |
| `close(sessionId)` | `{ ok }` | Release a session and its staging graph |
| `create(target)` | `{ ok, machine }` | Add a new Script Machine (fresh graph with Start/Update events) to the target object |
| `removeMachine(target)` | `{ ok, invalidated }` | Remove a Script Machine from its object, closing every session on that object |
| `pause(target, paused)` | `{ ok, paused }` | Pause or resume a live machine |
| `watch(sessionId, enabled)` | `{ ok }` | Enable/disable the debug-data sampler for a session |
| `watchPoll(sessionId)` | delta | Poll the sampler: units and connections that fired since the last poll |
| `toBase64(text)` | string | Encode a string as base64 of its UTF-8 bytes |
| `fromBase64(b64)` | string | Decode base64 back to a UTF-8 string |

Applied graphs are checked against the allow list, nested graphs included: a batch or an envelope that would add a node the app doesn't run is refused.

**Edit ops:** an `ops` batch is an array of small operations — `addUnit`, `addMemberUnit`, `removeUnit`, `setPosition`, `connect`, `disconnect`, `setPortDefault`, `clearPortDefault`, `setObjectRef`, `setGraphTitle`, `setGraphVariable`, `removeGraphVariable`. A unit's `unitId` is its GUID: you mint it when you add the unit (for example with `crypto.randomUUID()`), and the view-model reports it as each unit's `id`. Positions are `[x, y]` arrays. A few op shapes:

```js
// Node by unit type (its full type name), at a canvas position
{ op: "addUnit", unitId: "3f6c1a52-9a0e-4c47-8f0d-2b8f6f1d7e10",
  type: "Unity.VisualScripting.Sequence", pos: [0, 0] }

// Codebase member node: memberKind is "get", "set", "invoke" or "ctor".
// paramTypes picks an overload (full type names); leave it out for none.
{ op: "addMemberUnit", unitId: "8d2e4b7a-1c3f-4e5d-9a6b-7c8d9e0f1a2b", memberKind: "invoke",
  declaringType: "UnityEngine.Transform", member: "Rotate",
  paramTypes: ["System.Single", "System.Single", "System.Single"], pos: [300, 0] }

// Connection: kind is "control" or "value"; endpoints are { unitId, port }
{ op: "connect", kind: "control", src: { unitId: srcId, port: srcPort },
                                  dst: { unitId: dstId, port: dstPort } }

// Port default: { type, v }, type being bool, int, float, string, Vector2, Vector3,
// Vector4, Color, Rect (v an array of numbers) or enum (with enumType)
{ op: "setPortDefault", unitId: id, port: "%yAngle", value: { type: "float", v: 15 } }
```

Unit `type` strings and port keys are exactly as the view-model reports them — each unit in `vm.units` lists its `id`, `type`, `title`, `pos` plus `controlIn`, `controlOut`, `valueIn` and `valueOut` ports, each with its `key`. Connections are listed as `[[sourceId, sourcePort], [destinationId, destinationPort]]` pairs under `vm.connections.control` and `vm.connections.value`.

**Flow watching:** `watch(sessionId, true)` turns on the sampler, then each `watchPoll(sessionId)` returns a delta `{ t, live, units, control, values }` for the session's live machine:

| Field | What it holds |
|---|---|
| `t` | The sample time |
| `live` | `false` while watching is off or the machine can't be sampled; the lists are then empty |
| `units` | The ids of the units that ran since the last poll |
| `control` | Control connections that fired: `[sourceId, sourcePort, destinationId, destinationPort]` rows |
| `values` | Value connections that carried a value: `[sourceId, sourcePort, destinationId, destinationPort, value]` rows, the value as display text (up to 60 characters) |

```js
// Rename the first machine's graph, then push it live.
const { machines } = await BS.ScriptGraphBridge.list();
const target = { bid: machines[0].bid, machineIndex: machines[0].machineIndex, path: machines[0].path };

const vm = await BS.ScriptGraphBridge.open(target);   // vm.sessionId, vm.rev, vm.units...
if (vm.error) throw new Error(vm.error);

const result = await BS.ScriptGraphBridge.ops({
    sessionId: vm.sessionId,
    baseRev: vm.rev,                                  // revision the batch is based on
    ops: [{ op: "setGraphTitle", title: "My Button" }]
});
if (result.error) console.log("Not applied:", result.error);

await BS.ScriptGraphBridge.apply(vm.sessionId);       // swap staging graph onto the live machine
await BS.ScriptGraphBridge.close(vm.sessionId);       // always release the session
```

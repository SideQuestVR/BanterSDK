# Advanced: ScriptGraphBridge

`BS.ScriptGraphBridge` is the low-level session API for listing, editing and live-controlling script graphs from JavaScript. It is an advanced API — most creators never need it; use the nodes and `BS.ScriptGraph` instead.

All bridge calls are async and return Promises (the `toBase64` / `fromBase64` helpers are synchronous). Machines are addressed by a target: `{ bid, machineIndex }` — the object's BS id plus the index of the Script Machine on that object. Each row returned by `list()` describes one machine: `bid`, `machineIndex`, `objectName`, `path`, `active`, `source`, `graphTitle`, `unitCount`, `blocked` (the graph contains nodes outside the platform's allow list) and `paused`.

| Function | Returns | Description |
|----------|---------|-------------|
| `list()` | `{ machines }` | Enumerate every Script Machine in the scene (including on inactive objects) |
| `open(target)` | view-model | Open an edit session on a machine; resolves with the full graph view-model (`sessionId`, `rev`, units, connections, warnings) |
| `ops(batch)` | view-model | Apply an op batch `{ sessionId, baseRev, ops }` to the session's staging graph. Validate-all-then-mutate: on failure resolves with `error` / `failedOpIndex` and the staging graph is untouched |
| `save(sessionId)` | envelope | Serialize the session's staging graph into a persistable envelope |
| `apply(sessionId)` | ack | Swap the session's staging graph onto the live machine |
| `applyEnvelope(envelope)` | ack | Push a previously saved envelope straight onto its target machine |
| `close(sessionId)` | ack | Release a session and its staging graph |
| `create(target)` | ack | Add a new Script Machine (fresh graph with Start/Update events) to the target object |
| `removeMachine(target)` | ack | Remove a Script Machine from its object |
| `pause(target, paused)` | ack | Pause or resume a live machine |
| `watch(sessionId, enabled)` | ack | Enable/disable the debug-data sampler for a session |
| `watchPoll(sessionId)` | delta | Poll the sampler: units and connections that fired plus live port values since the last poll |
| `toBase64(text)` | string | Encode a string as base64 of its UTF-8 bytes |
| `fromBase64(b64)` | string | Decode base64 back to a UTF-8 string |

**Edit ops:** an `ops` batch is an array of small operations — `addUnit`, `addMemberUnit`, `removeUnit`, `setPosition`, `connect`, `disconnect`, `setPortDefault`, `clearPortDefault`, `setObjectRef`, `setGraphTitle`, `setGraphVariable`, `removeGraphVariable`. Units are referenced by a client-minted `unitId`. A few op shapes:

```js
// Node by unit type, at a canvas position
{ op: "addUnit", unitId: "u1", type: unitType, pos: { x: 0, y: 0 } }

// Codebase member node: kind is "get", "set", "invoke" or "ctor"
{ op: "addMemberUnit", unitId: "u2", kind: "invoke",
  declaringType: typeName, member: memberName, pos: { x: 300, y: 0 } }

// Connection: kind is "control" or "value"; endpoints are { unitId, port }
{ op: "connect", kind: "control", src: { unitId: "u1", port: srcPort },
                                  dst: { unitId: "u2", port: dstPort } }
```

Unit `type` strings and port keys are exactly as the view-model reports them — each unit in `vm.units` lists its `type` plus `controlIn`, `controlOut`, `valueIn` and `valueOut` ports.

**Flow watching:** `watch(sessionId, true)` turns on the sampler, then each `watchPoll(sessionId)` returns a delta `{ t, units, control, values }` — the units and control connections that fired since the last poll, plus `values` rows of `[unit guid, port key, display string]`.

```js
// Rename the first machine's graph, then push it live.
const { machines } = await BS.ScriptGraphBridge.list();
const target = { bid: machines[0].bid, machineIndex: machines[0].machineIndex };

const vm = await BS.ScriptGraphBridge.open(target);   // vm.sessionId, vm.rev, vm.units...

await BS.ScriptGraphBridge.ops({
    sessionId: vm.sessionId,
    baseRev: vm.rev,                                  // revision the batch is based on
    ops: [{ op: "setGraphTitle", title: "My Button" }]
});

await BS.ScriptGraphBridge.apply(vm.sessionId);       // swap staging graph onto the live machine
await BS.ScriptGraphBridge.close(vm.sessionId);       // always release the session
```

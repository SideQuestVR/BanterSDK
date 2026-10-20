# MCP Tools & Events

Every tool and event of the app's MCP server. To start the server and connect an agent, see [AI Agents (MCP)](ai-agents-mcp.md).

Agents read these descriptions from the server themselves, so you rarely need them to write a prompt. They're here so you know what an agent can and can't do, and what it gets back.

## Tools

| Tool | What it does |
|---|---|
| [`get_state`](#get_state) | Where the player is and is looking, what's available, and the latest event number. |
| [`wait_for_events`](#wait_for_events) | Waits until something happens, then returns it. |
| [`take_snapshot`](#take_snapshot) | A picture of what the player sees right now. |
| [`move`](#move) | Walks, turns, looks up or down and jumps, in any combination. |
| [`teleport`](#teleport) | Moves the player instantly, optionally facing a direction. |
| [`list_users`](#list_users) | Who is in the space, where they are, and who is talking. |
| [`say`](#say) | Speaks text aloud into the space. |
| [`stop_speaking`](#stop_speaking) | Stops speaking straight away. |
| [`list_voices`](#list_voices) | The voices available, and which one is selected. |
| [`set_voice`](#set_voice) | Changes the voice for everything said from then on. |
| [`set_listening`](#set_listening) | Starts or stops hearing and transcribing the people in the space. |
| [`get_model_status`](#get_model_status) | Which speech recognition models are downloaded and which is in use. |
| [`download_model`](#download_model) | Downloads a speech recognition model in the background. |
| [`speak_tone`](#speak_tone) | Sends a test tone, to check that people can hear the agent at all. |

A tool that can't run says why in its answer, for example when the app isn't in a space yet, rather than failing silently. No call runs for longer than 5 minutes.

### Directions and positions

Positions are world coordinates in metres, with **y** up, the same as in Unity and in your world's scripts. **Yaw** is a compass heading in degrees: 0 faces **+Z**, 90 faces **+X**. **Pitch** is how far up the player looks, from -85 (down) to 85 (up), 0 being level.

## Seeing and moving

### get_state

No arguments. Returns a snapshot of the app, in sections. Agents call it first to find out where they are.

| Section | What's in it |
|---|---|
| `server` | Server name, version, address and uptime. |
| `client` | Product, platform and whether it's running in the Unity editor. |
| `events` | `high_water_mark`: the number of the latest event. |
| `player` | `available`, `position`, `head_position`, `yaw`, `pitch`, `facing`, `grounded`, `moving`, `under_agent_control`, `can_look_up_down`, `can_jump`, `can_teleport`. |
| `networking` | Whether the app is connected to the space's room, the room, this player's id and how many people are in it. |
| `speech` | Whether the agent can speak, the selected voice and the voice engines. |
| `hearing`, `transcription`, `models` | Whether the agent is listening, audio counters, and the speech recognition model. |

### take_snapshot

A JPEG of the view in front of the player, taken from the app's camera.

| Argument | Default | Range |
|---|---|---|
| `width` | 1024 | 64 to 1920 pixels |
| `height` | 576 | 64 to 1080 pixels |
| `quality` | 80 | JPEG quality, 10 to 100 |

One snapshot is taken at a time: a second call while one is being taken is answered with "try again in a moment". To see somewhere else, turn or walk first, then take another.

### move

Walking, turning, looking up or down and jumping, in one call, because they happen together: you walk while watching someone, and jump mid-stride. The player is moved the way a person moves it, through the same stick input, so it collides, climbs stairs and steps up as a person would.

| Argument | What it does |
|---|---|
| `to` | Walk to this position: `{ "x": 0, "y": 0, "z": 5 }`. |
| `yaw` | Face this heading, 0 to 360. |
| `turn_degrees` | Turn this much from the current heading, -360 to 360; positive turns right. |
| `look_at` | Face and aim at this position, up or down included. |
| `pitch` | Look this far up, -85 to 85. |
| `pitch_degrees` | Look this much further up than now; negative looks further down. |
| `jump` | Jump. Combine it with `to` to jump while walking. |
| `timeout_s` | Give up after this long. Default 30, at most 120. |
| `arrive_radius` | How close counts as arriving, in metres. Default 0.5, from 0.1 to 5. |

Give at least one of them. `look_at` wins over `yaw`, which wins over `turn_degrees`; `pitch` wins over `pitch_degrees`.

Walking on its own steers towards the destination. Give a facing as well and the player keeps that facing and sidesteps there instead, which is how an agent keeps watching someone while it moves.

The call waits until the player arrives and faces the right way, or stops making progress, or runs out of time. Looking and jumping happen straight away. It answers with:

| Field | Meaning |
|---|---|
| `outcome` | `Arrived`, `Timeout`, `Stuck` (something is in the way), `Busy` (another move is still running), `Cancelled` or `Unavailable`. |
| `position`, `yaw`, `pitch` | Where the player ended up. |
| `remaining_m` | How far from the destination it stopped, ignoring height. |
| `requested_yaw`, `off_by_degrees` | The heading asked for and how far off it ended; the sign tells an overshoot from a shortfall. |
| `requested_pitch`, `pitch_changed` | The pitch asked for and whether it could be set. |
| `jumped` | Whether the player left the ground. |
| `detail` | A sentence saying what happened. |

Not arriving is a normal answer, not an error: the agent reads `detail` and tries another way. Only `Unavailable` is reported as an error.

Looking up and down only works when the app isn't in a headset: in a headset the person wearing it decides where it looks, and `move` says so. Snap turning turns in whole steps, so a turn ends within half a step of the heading asked for.

### teleport

Moves the player instantly. Use it to cover distance; use `move` when the journey should be seen.

| Argument | What it does |
|---|---|
| `position` | Required. Where to appear: `{ "x": 0, "y": 0, "z": 5 }`. |
| `yaw` | The heading to face afterwards, 0 to 360. |

Answers with `teleported` and the `position` the player landed at.

### list_users

No arguments. Everyone in the space, the agent's own player included:

| Field | Meaning |
|---|---|
| `name` | Display name. |
| `user_id` | Their SideQuest account id. |
| `peer_id` | Their id in the space's room, used in events. |
| `is_local` | `true` for the agent's own player. |
| `talking` | Whether they're talking right now. |
| `position` | Where their avatar is, when it has appeared. |

Walk towards someone with `move` and `to`, or face them with `look_at`.

## Speaking

Speaking and hearing work only inside a space, once the app has connected to its room. People hear the agent as your avatar's voice. While the agent speaks, its voice replaces your microphone; the microphone comes back when it finishes.

### say

Speaks text aloud and returns as soon as it's queued, with an `utterance_id`. A `say_started` event marks the start and `say_finished` the end.

| Argument | Default | What it does |
|---|---|---|
| `text` | | Required. What to say. Plain conversational sentences work best. |
| `voice` | | Say this one line in another voice, from `list_voices`, without changing the default. |
| `rate` | 1 | Speaking rate, 0.25 to 4. |
| `volume` | 0.7 | Loudness, 0 to 1. |
| `pitch_hz` | 110 | Pitch of the built-in voice, 50 to 300. |
| `robot` | 0 | How mechanical the built-in voice sounds, 0 to 1. Past about 0.3 it gets hard to understand. |

`rate` and `volume` apply to the built-in and Windows voices; ElevenLabs voices speak as they are.

### stop_speaking

No arguments. Stops straight away and drops anything still queued, for when someone interrupts. Answers with `was_speaking`.

### list_voices

No arguments. Every voice available, each with `name`, `engine`, `culture`, `gender` and `notes`, and the `selected` one. The best engine present is used by default:

| Engine | Sounds | Needs |
|---|---|---|
| ElevenLabs | Human | `-elevenlabs-key <key>` on the command line, and a connection |
| Windows | Clear | The voices installed in Windows |
| Built-in, named `robot` | Like a 1980s speech chip, hard to make out | Nothing; always there |

When no Windows voices can be used, the answer says why in `system_voices_unavailable`.

### set_voice

| Argument | What it does |
|---|---|
| `voice` | Required. A name from `list_voices`, or a distinctive part of one: `zira` finds `Microsoft Zira Desktop`. `robot` picks the built-in voice. |

Changes the voice for everything said from then on. If the name matches nothing, the voice stays as it was.

### speak_tone

Sends a short tone as the agent's voice, to check that people in the space can hear it at all.

| Argument | Default | Range |
|---|---|---|
| `frequency_hz` | 440 | 50 to 4000 |
| `duration_ms` | 600 | 50 to 5000 |
| `volume` | 0.4 | 0 to 1 |

## Hearing

### set_listening

| Argument | What it does |
|---|---|
| `listening` | Required. `true` to start listening, `false` to stop. |

While listening, each person's voice is heard separately, so two people talking at once give two transcripts. The person using the app is heard too, from the microphone, as speaker `local` named `You`. Each utterance is transcribed when the speaker pauses, or after 15 seconds of talking without a pause, and arrives as a `speech_transcript` event.

Transcription needs a speech recognition model. Without one, listening still works and the talking events still arrive, but there are no transcripts; the answer says so.

### get_model_status

No arguments. Lists the speech recognition models, which are downloaded, which is in use, and the listening state.

### download_model

| Argument | What it does |
|---|---|
| `model` | Required. One of the models below. |

Returns straight away and downloads in the background; `model_progress` events report progress.

!> **Restart the app after the first download.** The model is loaded when the app starts, so one downloaded during a session isn't used until the next one. Until then `set_listening` works but no transcripts arrive. `get_model_status` lists the model as downloaded; the `ready` field under `listening` says whether it's actually loaded.

### Speech recognition models

Transcription runs on your PC with [whisper.cpp](https://github.com/ggml-org/whisper.cpp). The models are downloaded from Hugging Face once, into `%USERPROFILE%\AppData\LocalLow\SideQuest\AltspaceVR\greenfield-mcp\models`. Each time the app starts, the most accurate model downloaded is loaded, once the app first connects to a space.

| Model | Size | Notes |
|---|---|---|
| `tiny.en` | 74 MB | English. Fastest; a good default. |
| `base.en` | 141 MB | English. More accurate, a little slower. |
| `small.en` | 465 MB | English. Most accurate, slowest. |
| `tiny` | 74 MB | Many languages. |
| `base` | 141 MB | Many languages. |

## Events

Things the agent may want to react to. Each has a `seq` number that only goes up, the time it happened (`at`), a `type` and a `payload`:

```json
{
  "seq": 42,
  "at": "2026-10-20T14:03:11.5210000+00:00",
  "type": "speech_transcript",
  "payload": { "speaker_id": "p-7", "speaker_name": "Sam", "text": "Hi there!", "seconds": 1.4 }
}
```

| Type | When | Payload |
|---|---|---|
| `speech_transcript` | Someone finished saying something. | `speaker_id`, `speaker_name`, `text`, `seconds` |
| `user_started_talking` | Someone started talking. | `peer_id`, `name`, `user_id` |
| `user_stopped_talking` | They've been quiet for 0.6 seconds. | `peer_id`, `name`, `user_id` |
| `user_joined` | Someone came into the space. | `peer_id`, `name`, `user_id` |
| `user_left` | Someone left. | `peer_id`, `name`, `user_id` |
| `connection_state` | The connection to the space's room changed. | `state` |
| `walk_finished` | A `move` ended. | `outcome`, `position`, `yaw` |
| `teleported` | A `teleport` happened. | `requested`, `position` |
| `say_started` | The agent started speaking, or sent a tone. | `utterance_id`, `text`; for a tone `kind`, `frequency_hz`, `duration_ms` |
| `say_finished` | It finished, or was stopped. | `utterance_id`, `cancelled`, `spoken_seconds` |
| `model_progress` | A model download moved on. | `model`, `percent`, `received_mb`, `total_mb`; `done` at the end |
| `dropped_events` | Events were missed (see below). | `count`, `reason` |
| `error` | Something went wrong in the background, such as speech that couldn't be sent. | `source`, `message` |

### wait_for_events

Waits until at least one event happens, then returns at once. If nothing happens before the timeout, it returns an empty list, which is a normal answer: the agent calls it again to keep listening. This works with every MCP client, because to the client it's just a tool that takes a while.

| Argument | Default | What it does |
|---|---|---|
| `timeout_s` | 20 | How long to wait, 0 to 120 seconds. |
| `since_seq` | where the last call ended | Return only events after this number. |

It answers with `events`, `count`, `timed_out`, and `next_seq` to pass as `since_seq` next time. Without `since_seq`, each call carries on where the agent's previous one ended, and an agent that has just connected starts from now rather than receiving old events.

The server keeps the latest 256 events. If an agent falls further behind than that, the first event it gets is `dropped_events` with how many it missed, so a gap never looks like a quiet room.

An agent that reacts to the space loops: act, `wait_for_events`, react, act again.

## Protocol details

For writing your own client, or checking the server by hand.

| | |
|---|---|
| Transport | MCP streamable HTTP, at `/mcp`. |
| Revisions | `2025-06-18`, `2025-03-26` and `2024-11-05`. A client asking for another is answered with `2025-06-18`. |
| Session | `initialize` returns an `Mcp-Session-Id` header; send it with every later request. A client that leaves it out is matched to the only open session, if there's exactly one. Sessions end after 2 hours unused, when the app restarts, or with `DELETE /mcp`. An unknown session gets HTTP 404: send `initialize` again. |
| Methods | `initialize`, `notifications/initialized`, `ping`, `tools/list`, `tools/call`, `logging/setLevel`. |
| Responses | Every POST is answered with a single JSON object; batches (the `2024-11-05` revision) with a JSON array. |
| Event stream | `GET /mcp` with `Accept: text/event-stream` opens a stream that repeats every event as a `notifications/message` notification, with a heartbeat every 15 seconds. It needs no session, so you can watch it with curl. |
| Health check | `GET /healthz` answers `{"ok":true,"server":"greenfield-mcp","version":"0.1.0"}`. |
| Authentication | With `-mcp-token`, every request needs `Authorization: Bearer <secret>`; without it the answer is 401. |
| Origin | Requests with an `Origin` header are accepted only from `localhost`, `127.0.0.1` or `::1`; others get 403. |
| Limits | 8 connections at once, 8 MB per request body, 5 minutes per tool call. |

Tool results are MCP content: text holding a JSON object for most tools, and a JPEG image for `take_snapshot`. A tool that couldn't do what was asked sets `isError`.

To try it by hand with curl (in PowerShell, write `curl.exe`), send `initialize`, note the `Mcp-Session-Id` header that comes back, then call a tool with it:

```sh
curl -i http://127.0.0.1:42070/mcp \
  -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"curl","version":"1"}}}'

curl http://127.0.0.1:42070/mcp \
  -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream" \
  -H "Mcp-Session-Id: <id from the first answer>" \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"get_state","arguments":{}}}'
```

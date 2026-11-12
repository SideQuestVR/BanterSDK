# AI Agents (MCP)

The Windows app has an [MCP](https://modelcontextprotocol.io) server built in. An AI agent such as Claude Code, Codex or Antigravity connects to it over HTTP and plays as you: it walks, turns, looks around and jumps, sees what the player sees, speaks into the space, hears and transcribes the people in it, and reacts when something happens.

Use it to walk an agent through your published world and have it report what it finds, take screenshots from the player's eyes, try a greeting out on the people in a space, or have a second player around while you test.

!> **Windows app only, and off unless you ask for it.** The server isn't in the Quest app or in SDK Play mode. It starts only when the app is launched with `-mcp`, and it only listens on your own computer unless you open it up with a token.

The agent drives the same player you do. While it walks or turns, your own movement input is paused, and it comes back as soon as the move ends.

**What an agent can't do:** click or grab objects, use menus or web pages, or run your world's scripts. It moves, looks, speaks and listens. In a headset, it can't look up or down: the person wearing it decides where it points.

## Start the app with MCP

The app's folder has a launcher next to `AltspaceVR.exe`: **`Altspace MCP.cmd`**. Double-click it. It starts the app with the server on and prints the address to connect to:

```
Starting AltspaceVR with the MCP endpoint enabled.

  Endpoint:  http://127.0.0.1:42070/mcp
  Connect:   claude mcp add --transport http greenfield http://127.0.0.1:42070/mcp
```

Anything you pass to the launcher goes to the app, so `"Altspace MCP.cmd" 42071` uses port 42071 and ordinary Unity switches such as `-screen-width` work too. You can also start the app yourself with `AltspaceVR.exe -mcp`.

?> **Starting it from a code editor's terminal?** Use the launcher. VS Code, Cursor and Claude Code set `ELECTRON_RUN_AS_NODE=1` for everything they start, which stops the app's built-in browser from starting, so spaces stay on **Loading** forever. The launcher clears that variable and runs from the app's folder, which the browser also needs. To run `AltspaceVR.exe -mcp` directly from such a terminal, clear `ELECTRON_RUN_AS_NODE` and start it from the app's folder.

### Command-line arguments

| Argument | What it does |
|---|---|
| `-mcp` | Starts the server on port 42070. The launcher always adds it. |
| `-mcp <port>` | Starts the server on that port instead. The number must come straight after `-mcp`. |
| `-mcp-token <secret>` | Requires every request to carry `Authorization: Bearer <secret>`. |
| `-mcp-lan` | Listens on every network interface instead of only this computer, so another machine can connect. Needs `-mcp-token`: without one the server refuses to start. |
| `-elevenlabs-key <key>` | Lets the agent speak with [ElevenLabs](https://elevenlabs.io) voices. The key is never logged. |
| `-elevenlabs-model <id>` | The ElevenLabs model to speak with. Default `eleven_turbo_v2_5`, a low-latency model. |

`-mcp-token` and `-mcp-lan` don't start the server on their own; `-mcp` is what turns it on.

### Finding the port

Ports 42067 to 42069 belong to the app's browser, so the server starts at **42070**. If that port is taken, it tries the next one up, as far as 42079. Wherever it lands, the app writes the address to:

```
%USERPROFILE%\AppData\LocalLow\SideQuest\AltspaceVR\greenfield-mcp\endpoint.json
```

```json
{
  "url": "http://127.0.0.1:42070/mcp",
  "port": 42070,
  "bind": "Loopback",
  "requires_token": false,
  "started_at": "2026-10-19T18:06:14.2814442+00:00"
}
```

The app's log has the same line: `[GreenfieldMcp] MCP endpoint listening on http://127.0.0.1:42070/mcp`. When an agent can't connect, check this file first: the address you gave the agent pins one port, and the server may have moved.

## Connect your agent

The server speaks MCP's **streamable HTTP** transport at `http://127.0.0.1:42070/mcp`. Add it to your agent once, then start the app with MCP whenever you want to use it. The examples name the server `altspace`; any name works (the launcher's window suggests `greenfield`).

Most agents only load a new server's tools when they start, so restart or reload the agent after adding it.

?> **Tool timeouts.** `move` can take up to 2 minutes and `wait_for_events` waits up to 2 minutes for something to happen. If your agent gives tools less time than that (Codex defaults to 60 seconds), raise its limit as shown below, or ask the agent to pass shorter `timeout_s` values.

### Claude Code

```sh
claude mcp add --transport http altspace http://127.0.0.1:42070/mcp
```

That adds it for the current folder. Add `--scope user` to have it in every project, or `--scope project` to save it in the folder's `.mcp.json` for everyone who works there:

```json
{
  "mcpServers": {
    "altspace": { "type": "http", "url": "http://127.0.0.1:42070/mcp" }
  }
}
```

With a token, add `--header "Authorization: Bearer <secret>"`. Check the connection with `/mcp` inside Claude Code, where you can also reconnect after restarting the app. To change the port, `claude mcp remove altspace` and add it again.

### Codex

```sh
codex mcp add altspace --url http://127.0.0.1:42070/mcp
```

Or add it to `~/.codex/config.toml` (or `.codex/config.toml` in a trusted project), with a longer tool timeout:

```toml
[mcp_servers.altspace]
url = "http://127.0.0.1:42070/mcp"
tool_timeout_sec = 180
```

With a token, put it in an environment variable and add `bearer_token_env_var = "ALTSPACE_MCP_TOKEN"`. The Codex CLI, the IDE extension and the desktop app share this file. `codex mcp list` shows what's configured.

### Antigravity

In the Antigravity IDE, click **…** at the top of the agent panel, choose **MCP Servers**, then **Manage MCP Servers** and **View raw config**. In the Antigravity CLI, type `/mcp`. Both edit `mcp_config.json`: the global one is `%USERPROFILE%\.gemini\config\mcp_config.json`, and `.agents/mcp_config.json` in a project applies to that project only.

```json
{
  "mcpServers": {
    "altspace": { "serverUrl": "http://127.0.0.1:42070/mcp" }
  }
}
```

Antigravity needs `serverUrl`; it doesn't accept `url` or `httpUrl`. With a token, add `"headers": { "Authorization": "Bearer <secret>" }` beside it. Reload the servers from the MCP manager after editing.

### Gemini CLI

```sh
gemini mcp add --transport http altspace http://127.0.0.1:42070/mcp
```

That saves it for the current project; add `--scope user` for every project. In `settings.json` (`~/.gemini/settings.json`, or `.gemini/settings.json` in a project) it's `httpUrl`:

```json
{
  "mcpServers": {
    "altspace": { "httpUrl": "http://127.0.0.1:42070/mcp" }
  }
}
```

With a token, add `--header "Authorization: Bearer <secret>"`, or a `headers` object in the file. `/mcp` lists the servers and their tools.

### Cursor

Add it to `~/.cursor/mcp.json`, or `.cursor/mcp.json` in a project:

```json
{
  "mcpServers": {
    "altspace": { "url": "http://127.0.0.1:42070/mcp" }
  }
}
```

### VS Code (GitHub Copilot)

Run **MCP: Add Server** from the Command Palette and choose HTTP, or add `.vscode/mcp.json` to the folder:

```json
{
  "servers": {
    "altspace": { "type": "http", "url": "http://127.0.0.1:42070/mcp" }
  }
}
```

Copilot uses MCP tools in agent mode.

### Windsurf

Add it to `%USERPROFILE%\.codeium\windsurf\mcp_config.json`:

```json
{
  "mcpServers": {
    "altspace": { "serverUrl": "http://127.0.0.1:42070/mcp" }
  }
}
```

### Claude Desktop

Claude Desktop's config file starts local programs rather than connecting to addresses, so bridge to the app with [`mcp-remote`](https://www.npmjs.com/package/mcp-remote), which needs [Node.js](https://nodejs.org). Open **Settings > Developer > Edit Config** and add:

```json
{
  "mcpServers": {
    "altspace": {
      "command": "npx",
      "args": ["-y", "mcp-remote", "http://127.0.0.1:42070/mcp"]
    }
  }
}
```

Restart Claude Desktop afterwards.

### Other agents

Any MCP client that supports streamable HTTP can connect: give it `http://127.0.0.1:42070/mcp`. A client that only starts local (stdio) servers can use the same `mcp-remote` bridge as Claude Desktop. The server speaks the MCP revisions `2025-06-18`, `2025-03-26` and `2024-11-05`; see [Protocol details](mcp-tools.md#protocol-details).

## Your first session

Start the app with the launcher, go into a space, then ask your agent something like:

> Look around this space and describe it. Then walk over to the nearest person, face them and say hello. Keep listening and answer when someone talks to you.

A typical agent then:

1. calls `get_state` to learn where the player is and what's available,
2. calls `take_snapshot` to see the view, and `list_users` to find people and where they stand,
3. uses `move` to walk over and face someone, and `say` to speak,
4. loops on `wait_for_events`, which returns as soon as someone talks, joins, leaves, or a move finishes.

To hear what people say, the agent turns on listening with `set_listening`. Transcription needs a speech model: the agent can fetch one with `download_model`, and the app uses it from the next time it starts. [MCP Tools & Events](mcp-tools.md) describes every tool and event.

## Connect from another computer

To drive the app from a different machine, start it listening on the network with a token:

```
"Altspace MCP.cmd" -mcp-lan -mcp-token <secret>
```

Connect the agent to `http://<this PC's address>:42070/mcp` with the header `Authorization: Bearer <secret>`. Windows may ask whether to let the app through the firewall the first time. Use a long, random token: anyone who has it and can reach the port controls your player.

!> **The connection isn't encrypted.** It's plain HTTP, so the token, the snapshots and the transcripts cross your network as they are. Only use `-mcp-lan` on a network you trust.

## What it can reach

The server moves your player, captures your screen and speaks with your account, so it stays closed unless you open it:

- **Off by default.** Only `-mcp` starts it.
- **Only this computer**, unless you pass `-mcp-lan`, and then only with a token.
- **Web pages can't use it.** Requests from a browser page are refused unless the page is on `localhost`, which stops a site you visit from driving the app. Agents send no browser origin and aren't affected.
- **People hear the agent as you.** It speaks through your account and appears as your avatar.
- **Muting doesn't stop it.** With the app muted, people still hear the agent, and the agent still hears your microphone while it's listening.
- **Hearing stays on your PC.** Transcription runs locally with whisper.cpp; only the speech model is downloaded, once. Windows voices are local too. ElevenLabs voices send what the agent says to ElevenLabs.

## Bots in your world

An app started with `-mcp` joins every space as a **bot** while its server is running, whether or not an agent is connected. (An account that SideQuest has marked as an AI agent counts as an AI agent instead.) World owners can turn bots away: open the world's page on altvr.app, choose **Edit world**, and under **Who is turned away** tick **Bots**. **AI agents** covers headless agents joining on their own, and **Guests** covers people who aren't signed in. Blocking guests doesn't block bots or AI agents, even ones that aren't signed in: tick those too if you want them out.

These settings only change who can get in, not who can see the world. A blocked bot still gets in if it's signed in as:

- the world's owner, so you can always test your own world with an agent,
- an account on the world's **Allowed bots and AI agents** list, on the same page,
- a manager of the group the world belongs to,
- a SideQuest admin.

Allowed accounts still have to pass **Who can join**, and a banned account stays banned. Only worlds have these settings; community spaces and spaces that aren't registered as worlds let bots in.

## Troubleshooting

### The agent can't connect

- **The app isn't running with MCP.** Start it with the launcher or `-mcp`; the window and the log print the address.
- **The port moved.** Compare the agent's address with `endpoint.json` (see [Finding the port](#finding-the-port)) and update the agent if they differ.
- **A token is required.** A `401` answer means the token is missing or wrong. `requires_token` in `endpoint.json` says whether one is needed.
- **`403 Origin not allowed`.** The client is a web page on a site other than `localhost`. Use a desktop agent instead.

To check the server by hand, open `http://127.0.0.1:42070/healthz` in a browser. It answers `{"ok":true,"server":"greenfield-mcp","version":"0.1.0"}`.

### The agent connected but has no tools

Restart or reload the agent: most only list a new server's tools when they start. In Claude Code, `/mcp` reconnects.

### "Unknown or expired session. Send initialize again."

The app was restarted, so the agent's session is gone. Reconnect the agent (in Claude Code, `/mcp`), or restart it.

### Spaces stay on Loading

The app was started from a code editor's terminal. Use the launcher; see [Start the app with MCP](#start-the-app-with-mcp).

### Movement is unavailable

The player hasn't been loaded yet, for example during a space's loading screen. `get_state` shows `"player": { "available": false }` until it is; try again once you're in the space. `can_teleport` in the same section says whether `teleport` can be used yet.

### The agent can't speak or hear

Voice works only inside a space, once the app has connected to its room: `networking.state` in `get_state` is `Connected`. Before then, `say` still answers that the text is queued, but nothing is heard, and an `error` event follows saying the app isn't connected to a room yet.

If the agent hears nothing, check that it called `set_listening`, then `get_model_status`: without a speech model, audio arrives and the talking events still come in, but no transcripts. A model downloaded during a session is only used after the app restarts. See [Speech recognition models](mcp-tools.md#speech-recognition-models).

### The app can't get into a world

The world blocks bots. Ask its owner to add your account to **Allowed bots and AI agents**, or start the app without `-mcp` to go in as yourself. See [Bots in your world](#bots-in-your-world).

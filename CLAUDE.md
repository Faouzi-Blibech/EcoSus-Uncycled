@AGENTS.md

# Claude Code notes

Everything in `AGENTS.md` applies. These lines only add Claude-specific help.

- Skills worth loading for this repo: `gamedev:godot-gdscript`, `gamedev:godot-nodes-scenes`,
  `gamedev:godot-signals-groups`, `gamedev:godot-2d-movement`, `gamedev:godot-ui-control`,
  `gamedev:godot-export`, `gamedev:input-systems`, `gamedev:game-ai` (bots),
  `gamedev:game-feel`, `gamedev:game-jam` (scope cuts).
- `gamedev:godot-multiplayer` covers Godot's built-in multiplayer API. **We do not use it**: our
  server is a .NET app and the client talks to it with a plain `WebSocketPeer` and JSON.
- For C# rules work, open `reference/docs/logic/` before writing a rule (see AGENTS.md s4).
- Keep this file and `AGENTS.md` under ~200 lines. Split by folder (`client/AGENTS.md`,
  `server/AGENTS.md`) when either grows past that.

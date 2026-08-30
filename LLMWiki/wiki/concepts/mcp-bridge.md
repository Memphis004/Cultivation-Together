---
title: MCP Bridge
type: concept
sources:
  - McpBridge/Program.cs
  - UnityProject/Assets/Scripts/Core/GameLifetimeScope.cs
related:
  - "[[concepts/message-pipe-bus]]"
  - "[[concepts/vcontainer-composition]]"
  - "[[sources/architecture]]"
created: 2026-08-31
updated: 2026-08-31
confidence: high
tags: [mcp, bridge, ai, game-master, ipc]
---

# MCP Bridge

> The .NET 8 console app that exposes game state and actions as MCP tools
> for an AI Game Master client (e.g. Open-LLM-VTuber).

## Architecture

```
┌──────────────────┐    ┌────────────────────────┐    ┌──────────────────┐
│  AI GM Client    │    │  McpBridge             │    │  Unity           │
│  (Open-LLM-VTuber│    │  (.NET 8 console app)  │    │  (TCP server)    │
│   or similar)    │    │                        │    │                  │
│                  │    │  - ModelContextProtocol│    │  - GameLifetime- │
│  - Reads state   │◀──▶│    SDK (stdio)         │    │    Scope         │
│  - Decides       │    │  - MessagePipe client  │◀──▶│  - 4 subsystems  │
│  - Calls tools   │    │  - MCP tool handlers   │TCP │  - StateProvider │
└──────────────────┘    │                        │3215│                  │
                        │  Tools:                │    │                  │
                        │  - get_sect_state      │    │                  │
                        │  - await_next_world_   │    │                  │
                        │    event               │    │                  │
                        │  - execute_decision    │    │                  │
                        │  - purchase_item       │    │                  │
                        └────────────────────────┘    └──────────────────┘
```

## Connection

- Unity hosts TCP endpoint at `127.0.0.1:3215` (configurable in
  `GameLifetimeScope` inspector)
- McpBridge connects as a client using `MessagePipe.Interprocess`
- AI client connects to McpBridge via stdio (MCP protocol)

**Order**: Start Unity first (it binds the port), then run `dotnet run` in
`McpBridge/`.

## MCP Tools Exposed

| Tool | Type | Returns |
|---|---|---|
| `get_sect_state` | Read | Full state snapshot (MessagePack bytes → bridge decodes to JSON for AI) |
| `await_next_world_event` | Read (blocking) | Next world event with choices (blocks until event fires) |
| `execute_decision` | Write | Success/failure (logs but currently doesn't return detailed result) |
| `purchase_item` | Write | Success/failure + remaining contribution |

## Read vs Write Tools

Per project_summary.md: "แยก read (`get_sect_state`, `await_next_world_event`)
กับ write (`execute_decision`) เป็นคนละ tool type ชัดเจนในโค้ดฝั่ง bridge"

Tools are organized in `SectQueryTools` (read) and `SectActionTools` (write)
classes on the bridge side.

## Why Separate Process (Not In-Unity MCP Server)

- AI GM client (e.g. Open-LLM-VTuber) typically speaks stdio MCP, not HTTP
- .NET 8 has best-in-class MCP SDK (`ModelContextProtocol`)
- Process isolation: if McpBridge crashes, Unity keeps running
- Clean rebuild without Unity restart: just `dotnet run` again

## When to Use This (vs Unity Editor MCP)

| Use case | Pick |
|---|---|
| AI agent playing the game in a real session | **McpBridge** (this) |
| Claude Code / Codex / OpenCode driving Unity Editor tools | **Ivan Murzak Unity MCP** (separate, 80+ tools) |
| Debugging game state from outside the Editor | McpBridge |
| Generating ScriptableObjects, running tests, scene manipulation | Ivan Murzak Unity MCP |

## Common Debugging Steps

When a tool call fails:

1. **Is Unity running?** (TCP port 3215 must be bound)
2. **Is the McpBridge connected?** (check its console for "connected" or errors)
3. **Is Unity Editor's Pause button engaged?** (separate from `TimeSystem.IsPaused`)
   - **CRITICAL GOTCHA**: Editor Pause halts `Update()`/`Tick()` for all
     tickable systems including `WorldEventSystem`
4. **Is Unity's `Run In Background` enabled?** (Player Settings > Resolution
   and Presentation) — otherwise switching to browser halts ticks
5. **Check Unity Console** for any error from the request handler
6. **Check McpBridge console** for serialization errors

## After `execute_decision` for a `requiresDecision: true` event

The game was paused. `DecisionLogger` (in Unity) calls `TimeSystem.SetPaused(false)`,
unpausing the game loop. Next `await_next_world_event` can proceed normally.

**Don't call `await_next_world_event` again without first calling
`execute_decision`** if the last event required a decision — it will block
until the next event, which won't fire because the game is paused.

## Related Pages

- [[concepts/message-pipe-bus|MessagePipe Bus]]
- [[concepts/vcontainer-composition|VContainer Composition Root]]
- [[concepts/decision-pipeline|Decision Pipeline]]
- [[sources/architecture|Architecture]]

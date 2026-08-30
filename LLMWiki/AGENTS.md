# AGENTS.md — Cultivation Together LLM Wiki Schema

> Schema file for the LLM agent (Claude Code, Codex, OpenCode).
> Co-evolves with the human over time. The agent should propose edits here when
> new conventions are discovered.

## Project Snapshot

| | |
|---|---|
| **Game** | Xianxia sect-management simulator (Unity 6.3 / 6000.3.9f1) |
| **Goal** | Single-player with AI Game Master support via MCP (Open-LLM-VTuber or similar) |
| **Target use case** | Streamer relief: AI plays Game Master role, viewers vote on choices |
| **Reference games** | 龙胤立志传 / The Scroll of Taiwu / Rimworld / Amazing Cultivation Simulator |

## Tech Stack (locked)

- **Engine**: Unity 6.3 (C# 8.0 compatible — no `record`/`init`/`global using`)
- **DI**: VContainer (composition root = `GameLifetimeScope.cs`)
- **Internal bus**: MessagePipe (in-memory pub/sub + request-response)
- **IPC**: MessagePipe.Interprocess (TCP, Unity = server on `127.0.0.1:3215`, Bridge = client)
- **State serialization**: MessagePack (decided in lab 7 — protobuf permanently dropped)
- **Data pipeline**: Luban (Excel → JSON → C# runtime classes) for design-time data
- **UI**: UGUI + TextMeshPro only (NO UI Toolkit)
- **AI integration**: MCP server in .NET 8 console app (`McpBridge/`) exposes tools via stdio
- **Unity MCP**: `com.ivanmurzak.unity.mcp` 0.90.0 inside Editor (~80 tools available)

## Directory Structure

```
LLMWiki/                          ← this folder (Obsidian vault)
├── raw/                          ← Layer 1: immutable source documents
│   └── articles/                 ←   reference articles (tutorials, papers)
├── wiki/                         ← Layer 2: LLM-maintained markdown
│   ├── index.md                  ←   master catalog (read first on every query)
│   ├── log.md                    ←   append-only operation log
│   ├── overview.md               ←   high-level project summary
│   ├── conventions.md            ←   workflow rules
│   ├── sources/                  ←   YOU write the seeds (6 design-doc pages)
│   │   └── code-snippets/        ←   important scripts as markdown
│   ├── concepts/                 ←   AI maintains: one page per system/concept
│   ├── entities/                 ←   AI maintains: one page per entity type
│   └── outputs/                  ←   generated reports (lint results, audits)
└── AGENTS.md                     ← this file
```

## Two-Layer Wiki Philosophy (per Karpathy)

- **`raw/` is immutable** — never edit, never delete. Reference articles, blog posts, papers go here.
- **`wiki/sources/` is your design log** — GDD, mechanics, bug log, devlogs, important code snippets. You write and maintain these.
- **`wiki/concepts/` and `wiki/entities/` are AI-curated** — agent extracts and updates these from sources on every ingest.

## Page Types and Conventions

### YAML Frontmatter (required on all wiki pages)

```yaml
---
title: Page Title
type: gdd | mechanics | bug-log | devlog | snippet | concept | entity
sources:
  - raw/articles/filename.md
related:
  - "[[concept-name]]"
created: YYYY-MM-DD
updated: YYYY-MM-DD
confidence: high | medium | low
tags: [combat, ai, ui, ...]
---
```

### Naming

- **Filenames**: kebab-case matching concept (e.g. `combat-system.md`, `mcp-bridge.md`)
- **Cross-references**: `[[wikilinks]]` for all internal links
- **Source references**: always include the actual C# file path in backticks, e.g. `` `Assets/Scripts/Systems/SectStateProvider.cs` ``
- **Code snippets**: store as `.md` files with full code in fenced blocks (not raw `.cs`)

### Code Snippet Pages (`wiki/sources/code-snippets/`)

One page per important script. Always include:
- Full path to the source file
- Purpose (1 paragraph)
- Public API table (method/field/return)
- Dependencies (other scripts it uses)
- The actual code in a fenced block (or key sections if >300 lines)
- Known issues (link to `[[sources/bug-log]]`)

## Workflows

### Ingest

1. Human adds new content to `raw/` or edits a `wiki/sources/` page
2. Tell the agent: "ingest raw/articles/<file>.md" or "I updated sources/mechanics.md, sync concepts"
3. Agent:
   - Reads the source
   - Discusses 1-3 key takeaways
   - Updates or creates concept pages in `wiki/concepts/`
   - Updates or creates entity pages in `wiki/entities/`
   - Updates `wiki/index.md` (add new entries, keep alphabetical by section)
   - Appends to `wiki/log.md` (format: `## [YYYY-MM-DD] ingest | <source>`)
   - Flags contradictions with existing pages

### Query

1. Agent reads `wiki/index.md` first
2. Reads relevant pages, synthesizes answer
3. Cites sources using `[[wikilinks]]`
4. If novel and valuable, offers to file as new page

### Lint (periodic health check)

Scans for:
- **Contradictions** between pages (cite both)
- **Orphan pages** (no incoming `[[wikilinks]]`)
- **Stale claims** superseded by newer sources
- **Missing concepts** referenced but no page exists
- **Inconsistent naming** (e.g. `CombatSystem` vs `combat-system.md`)
- **Low-confidence pages** not updated in 30+ days

Saves to `wiki/outputs/lint-YYYY-MM-DD.md`.

## Tone and Style

- **Language**: Mixed Thai/English (Thai for prose, English for technical terms)
- **Concise** — bullets over prose where possible
- **Technical** — proper game-dev terminology (FSM, BT, GOAP, IPC, etc.)
- **Opinionated** — when asked, recommend one specific approach, don't list options
- **Cite sources** — every claim traces back to `raw/` or `wiki/sources/`
- **Quote real code paths** — when explaining a system, link to the actual `.cs` file

## Code Citation Convention

When referencing a script, use this format:

```markdown
Implemented in `Assets/Scripts/Systems/SectStateProvider.cs:177` —
`RecruitOuterDisciple()` publishes `DiscipleRecruitedMessage` on success.
```

## Human-in-the-Loop Rules

- **Never** delete a `raw/` file — immutable source of truth
- **Always** discuss key takeaways before mass-updating wiki pages
- **Always** update `wiki/index.md` and `wiki/log.md` on every operation
- **Ask before** creating new top-level entity/concept categories
- **Prefer the source of truth in code** — when design doc and code disagree, code wins; flag the doc for update

## Open Design Questions (from project_summary.md)

These are intentionally unresolved. The agent should help brainstorm when asked, not assume:

1. Final core loop (waiting for gameplay systems to solidify)
2. Combat system (ACS-style vs Rimworld-style)
3. Stat system, techniques/manuals, items/weapons/armor/elixirs (detailed)
4. Sect war (WIP)
5. Spirit stone robbery between disciples (not in schema yet)
6. `BuildingSystem` is a stub — deferred until needed
7. `WorldEventSystem` still uses random weighted pick — no state-conditional logic yet
8. Event consequences and shop prices are placeholders — awaiting real balance
9. `DiscipleListPresenter` panel not implemented (enum reserved, throws NotImplemented)
10. No dedicated wallet-changed message (HUD piggybacks on resource-changed event)

## Tooling Notes

- **Obsidian** recommended as frontend (graph view, backlinks, dataview)
- **Git** the `LLMWiki/` folder for history and diffs
- **MCP**: Unity Editor has Ivan Murzak's Unity MCP (80+ tools); can be used directly
- **McpBridge**: separate .NET 8 console app exposes game-specific tools (`get_sect_state`, `await_next_world_event`, `execute_decision`, `purchase_item`) to AI GM clients
- **Open-source preferred**: avoid paid assets when free alternatives exist (already the case — every dependency is OSS or Unity built-in)

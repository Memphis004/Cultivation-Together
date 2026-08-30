# Cultivation Together — LLM Wiki

> Second brain for the game. AI maintains the wiki; you curate the sources.

Based on Andrej Karpathy's [LLM Wiki pattern](https://gist.github.com/karpathy/442a6bf555914893e9891c11519de94f).

## What's in here

```
LLMWiki/
├── raw/                          ← You write (immutable). LLM reads.
├── wiki/                         ← LLM writes. You read.
│   ├── index.md                  ← Master catalog (read first)
│   ├── log.md                    ← Chronological operation log
│   ├── overview.md               ← High-level project summary
│   ├── conventions.md            ← Your workflow rules
│   ├── sources/                  ← You write the design-doc seeds
│   │   ├── game-design-doc.md
│   │   ├── architecture.md
│   │   ├── mechanics.md
│   │   ├── devlog-history.md
│   │   ├── bug-log.md
│   │   ├── open-questions.md
│   │   └── code-snippets/        ← Important scripts as markdown
│   ├── concepts/                 ← LLM maintains: per system/concept
│   │   ├── vcontainer-composition.md
│   │   ├── message-pipe-bus.md
│   │   ├── mcp-bridge.md
│   │   ├── decision-pipeline.md
│   │   ├── state-management.md
│   │   ├── time-system.md
│   │   ├── world-events.md
│   │   ├── gathering-system.md
│   │   ├── crafting-system.md
│   │   ├── purchase-store.md
│   │   ├── mvp-ui.md
│   │   └── data-pipeline.md
│   └── entities/                 ← LLM maintains: per entity type
│       ├── disciples.md
│       ├── world-events.md
│       ├── events.md
│       ├── resources.md
│       ├── items.md
│       └── sects.md
├── AGENTS.md                     ← Schema — tells the LLM how to behave
└── README.md                     ← This file
```

## How to Use

### 1. Browse

Open `LLMWiki/` as an **Obsidian vault** to get:
- Graph view (see knowledge structure)
- Backlinks (auto-tracked)
- Dataview queries
- Search across all pages

### 2. Query

Ask the LLM agent (Claude Code, Codex, OpenCode) questions like:
- "How does the decision pipeline work?"
- "What bugs were hit during lab 6?"
- "How do I add a new world event?"
- "Why does `await_next_world_event` timeout after a decision event?"

The agent reads `wiki/index.md` first, finds relevant pages, answers with
`[[wikilinks]]` citations.

### 3. Add a Source

- **Design notes**: edit files in `wiki/sources/`
- **External articles**: drop into `raw/articles/`
- **Code**: paste important scripts into `wiki/sources/code-snippets/<Name>.cs.md`

Then tell the agent: "ingest wiki/sources/mechanics.md" or "I added a new
ScriptableObject for buildings, please document it"

### 4. Lint (Periodic Health Check)

```
> lint the wiki
```

Saves a report to `wiki/outputs/lint-YYYY-MM-DD.md` covering:
- Contradictions between pages
- Orphan pages (no incoming links)
- Stale claims
- Missing concepts

## Files You Write vs Files the LLM Writes

| Layer | Who writes | Files |
|---|---|---|
| `raw/` | You | All files (immutable) |
| `wiki/sources/` | You | Design-doc seeds + code snippets |
| `wiki/concepts/` | LLM | Concept pages per system |
| `wiki/entities/` | LLM | Entity pages per type |
| `wiki/index.md` | LLM | Master catalog |
| `wiki/log.md` | LLM | Operation log |
| `AGENTS.md` | You + LLM | Schema (co-evolves) |
| `wiki/conventions.md` | You | Your preferences |

## Next Steps

1. ✅ Wiki is bootstrapped with full project understanding
2. **Fill in `[[sources/open-questions|Open Questions]]` answers** as you decide
3. **Start a daily devlog** (`devlog-YYYY-MM-DD.md`) for each work session
4. **Add code snippets** for any script you want the LLM to know about
5. **Periodically ask the LLM to lint the wiki** for health

## Tips

- **Commit `LLMWiki/` to git** — every change is a trackable diff
- **Start with real project content** — the wiki already has 31 pages grounded
  in your actual code; add more as you write more
- **Reference the actual file paths** in backticks when explaining systems,
  e.g. `Assets/Scripts/Systems/SectStateProvider.cs:177`
- **Talk to the LLM** — discuss takeaways before mass updates; don't let it
  run unsupervised on the whole wiki

## Tech Stack Reminder

| | |
|---|---|
| Engine | Unity 6.3 (C# 8.0) |
| DI | VContainer |
| Internal bus | MessagePipe |
| IPC to MCP | MessagePipe.Interprocess (TCP) |
| MCP bridge | .NET 8 console app |
| State serialization | MessagePack |
| Data pipeline | Luban (Excel → JSON → C#) |
| UI | UGUI + TextMeshPro |

## Related Project Docs (Outside the Wiki)

- `../project_summary.md` — **THE design log** (Thai, 546 lines, 13 labs)
- `../README.md` — workspace overview
- `../XIANXIA_UI_MVP_LITE_SPEC.md` — UI framework spec

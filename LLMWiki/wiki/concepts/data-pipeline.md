---
title: Data Pipeline (Luban)
type: concept
sources:
  - UnityProject/Assets/Scripts/Data/LubanEventPool.cs
  - DataTables/luban.conf
  - DataTables/Defines/worldevent.xml
  - DataTables/Data/event.xlsx
related:
  - "[[concepts/world-events]]"
  - "[[concepts/vcontainer-composition]]"
created: 2026-08-31
updated: 2026-08-31
confidence: high
tags: [luban, excel, codegen, data, events]
---

# Data Pipeline (Luban)

> Design-time data for world events comes from Excel, gets compiled to JSON
> + C# runtime classes by Luban, and is loaded at runtime by
> `LubanEventPool`. Replaced hand-created ScriptableObject `.asset` files
> in lab 12.

## Why Luban (vs ScriptableObject)

| Aspect | ScriptableObject (old) | Luban (new) |
|---|---|---|
| Source of truth | One `.asset` per item, scattered in `Assets/` | One Excel file in `DataTables/Data/` |
| Version control | YAML diffs are noisy | Excel diffs are clean |
| Bulk edits | Open Unity, click, save, repeat | Edit Excel, run `gen.sh` |
| C# integration | Author a wrapper MonoBehaviour | Generated runtime class, ready to use |
| Pain point | "ขี้เกียจคลิกสร้าง Asset หลายที" (lab 12) | One command, all regenerated |

## Pipeline

```
DataTables/Data/event.xlsx          ← human edits
        ↓
DataTables/gen.sh (or gen.bat)      ← calls Luban
        ↓
Tools/Luban/Luban.exe               ← C# scripting engine
        ↓
- DataTables/Gen/cfg/Tables.cs      ← C# runtime entry point
- DataTables/Gen/cfg/worldevent/*.cs ← typed row classes
- Assets/Resources/DataTables/worldevent_*.json  ← runtime data
        ↓
Unity loads JSON via Resources.Load<TextAsset>
        ↓
LubanEventPool constructs cfg.Tables(JSON parser)
        ↓
WorldEventSystem queries: GetRandomEvent(), GetChoices(eventId)
```

## Files Involved

| Path | Purpose |
|---|---|
| `DataTables/luban.conf` | Luban configuration (input/output dirs, target lang) |
| `DataTables/Defines/worldevent.xml` | Schema (table names, columns, types) |
| `DataTables/Data/event.xlsx` | Event rows (id, description, weight, requiresDecision) |
| `DataTables/Data/event_choice.xlsx` | Choice rows (eventId, choiceId, label, ...) |
| `DataTables/gen.sh` / `gen.bat` | Run Luban to regenerate |
| `Tools/Luban/Luban.exe` | Luban binary (downloaded from official release) |
| `Assets/Resources/DataTables/worldevent_tbevent.json` | Generated runtime data |
| `UnityProject/Assets/Scripts/Data/LubanEventPool.cs` | Plain C# wrapper around `cfg.Tables` |

## Adding a New Event Column

1. Edit `DataTables/Defines/worldevent.xml` to add the column
2. Run `DataTables/gen.sh`
3. Update `LubanEventPool` if the column needs custom logic (e.g. new
   weight calculation)
4. Update `WorldEventSystem` if the column changes behavior

## Critical Bug Avoided (lab 12)

`<module name="event">` — `event` is a C# reserved keyword. Generated code
becomes `cfg.event.X` which compiler can't parse. **Module renamed to
`worldevent`** to produce `cfg.worldevent.X`.

**Rule**: when naming Luban modules, check the C# reserved word list.

## Another Gotcha (lab 12)

`Luban.SimpleJSON` namespace, not `SimpleJSON`. The package bundles its own
JSON library with a namespaced name.

## Code — LubanEventPool.cs (key bits)

```csharp
using Luban.SimpleJSON;  // <-- NOT SimpleJSON, namespace is prefixed

namespace Xianxia.Sect
{
    public class LubanEventPool
    {
        private readonly cfg.Tables _tables;
        private readonly Dictionary<string, List<cfg.worldevent.EventChoiceRow>> _choicesByEventId;

        public LubanEventPool()
        {
            _tables = new cfg.Tables(LoadJson);
            _choicesByEventId = _tables.TbEventChoice.DataList
                .GroupBy(c => c.EventId)
                .ToDictionary(g => g.Key, g => g.ToList());
        }

        private static JSONNode LoadJson(string file)
        {
            var textAsset = Resources.Load<TextAsset>($"DataTables/{file}");
            if (textAsset == null) {
                Debug.LogError($"[LubanEventPool] Missing generated data file 'DataTables/{file}' in Resources - did you run DataTables/gen.sh?");
                return null;
            }
            return JSON.Parse(textAsset.text);
        }

        public cfg.worldevent.EventRow GetRandomEvent() { /* weighted random */ }
        public IReadOnlyList<cfg.worldevent.EventChoiceRow> GetChoices(string eventId) { ... }
    }
}
```

## Luban Configuration (luban.conf)

Key fields (illustrative):
- `target`: `cs-simple-json` (NOT protobuf — see lab 7 decision)
- `inputDataDir`: `Data`
- `outputCodeDir`: `../UnityProject/Assets/Scripts/Data/Gen`
- `outputDataDir`: `../UnityProject/Assets/Resources/DataTables`
- `tables`: list of tables including `worldevent.TbEvent`, `worldevent.TbEventChoice`

## Why `cs-simple-json` (not `protobuf` or `binary`)

Per lab 7: dropped protobuf permanently because the cross-language need is
gone (bridge is C# too). JSON is debug-friendly (open in text editor).

When data grows, switch to `cs-bin` (binary MessagePack) — same library,
just config change.

## Related Pages

- [[concepts/world-events|World Events]]
- [[concepts/vcontainer-composition|VContainer Composition Root]]
- [[sources/bug-log|Bug Log]] (BUG-L12-01, BUG-L12-02, BUG-L12-03)

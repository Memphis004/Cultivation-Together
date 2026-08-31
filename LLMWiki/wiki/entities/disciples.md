---
title: Disciples
type: entity
sources:
  - UnityProject/Assets/Scripts/Shared/SectEconomyState.cs
  - UnityProject/Assets/Scripts/Shared/MockSectData.cs
  - UnityProject/Assets/Scripts/Systems/SectStateProvider.cs
related:
  - "[[concepts/gathering-system]]"
  - "[[concepts/crafting-system]]"
  - "[[concepts/purchase-store]]"
created: 2026-08-31
updated: 2026-08-31
confidence: high
tags: [disciple, character, rank, mock-data]
---

# Disciples

> The main entities in the game. Each has a rank, wallet, inventory, and a
> current task (gathering or crafting).

## Data Model

```csharp
DiscipleState {
    DiscipleId: string      // e.g. "d000", "d001"
    DisplayName: string
    Rank: DiscipleRank      // OuterDisciple | InnerDisciple | Elder | SectMaster
    Wallet: CurrencyWallet {
        SpiritStones: long
        Contribution: long
    }
    PersonalInventory: List<InventoryItem>
    CurrentTask: string     // e.g. "gathering_herb", "refining_elixir", "meditation"
    Avatar: AvatarAppearance
                            // Sprite-swap: Body/Hair/Accessory slot PartIds ("")=def default
}
```

```csharp
// Avatar slots, drawn bottom -> top. Stored on `AvatarAppearance` as a
// per-slot PartId ("" = use Def-table default).
[enum AvatarSlot {
    Body,     // robe/tunic/armor (draw order 0..N)
    Head,     // face/hat/head piece
    Hair,     // hair/hairpiece
    Accessory // ring/pendant/fan (draw order top-most)
}]
```

```csharp
new DiscipleState
{
    // ... existing fields ...
    Avatar = new AvatarAppearance 
    { 
        Body = "robe_outer_white", 
        Head = "face_male_01", 
        Hair = "hair_bun_black", 
        Accessory = "" 
    }
}
```

## Ranks

```csharp
enum DiscipleRank {
    Unspecified,
    OuterDisciple,
    InnerDisciple,
    Elder,
    SectMaster
}
```

**Ownership rule** (from economy design):
- **Outer / Inner Disciple**: crafted items go to **Sect Stockpile** (others
  can buy with contribution)
- **Elder / SectMaster**: crafted items go to **Personal Inventory**

## Mock Data (Current Roster)

From `Shared/MockSectData.cs` — 4 disciples to start:

| ID | Name | Rank | Task | Wallet (stones/contrib) |
|---|---|---|---|---|
| `d000` | Liu YiFeng | **SectMaster** | meditation | 1200 / 3400 |
| `d001` | Lin Feng | OuterDisciple | `gathering_herb` | 12 / 340 |
| `d002` | Su Yan | InnerDisciple | `refining_elixir` | 45 / 1120 |
| `d003` | Elder Zhao | Elder | `forging_artifact` | 210 / 4300 |

Elder Zhao starts with `sword_azure_flame (grade 5)` in personal inventory.

## Tasks

Each disciple has exactly ONE `CurrentTask` at a time. Known tasks:

| Task ID | Type | Effect |
|---|---|---|
| `meditation` | (no effect yet) | SectMaster starts here; no tick |
| `gathering_herb` | passive | +0.2 herb/s |
| `gathering_wood` | passive | +0.2 wood/s |
| `gathering_ore` | passive | +0.15 ore/s |
| `gathering_provisions` | passive | +0.25 provisions/s |
| `refining_elixir` | crafting | 20s → 1 × elixir_qi_gathering (grade 3), costs herb × 10 |
| `forging_artifact` | crafting | 30s → 1 × sword_azure_flame (grade 5), costs ore × 15, wood × 10 |

⏳ **TODO**: inner/elder promotion, task assignment UI, technique learning

## Recruitment

Triggered by `new_disciple_applicant` world event → `execute_decision` with
`choiceId` containing "accept" (case-insensitive).

```csharp
public void RecruitOuterDisciple()
{
    var index = _state.Disciples.Count;
    var task = GatheringTasks[index % GatheringTasks.Length];  // round-robin
    var name = RecruitNamePool[index % RecruitNamePool.Length];  // round-robin from placeholder pool

    var disciple = new DiscipleState {
        DiscipleId = $"d{index + 1:000}",
        DisplayName = name,
        Rank = DiscipleRank.OuterDisciple,
        Wallet = new CurrencyWallet(),
        PersonalInventory = new List<InventoryItem>(),
        CurrentTask = task,
    };

    _state.Disciples.Add(disciple);
    _discipleRecruitedPublisher.Publish(new DiscipleRecruitedMessage {
        DiscipleId = disciple.DiscipleId,
        DisplayName = disciple.DisplayName,
    });
}
```

**Placeholder name pool**: Chen Wei, Bai Ling, Zhou Tao, Xiao Mei, Jiang Yu, Wen Hao
(replaces when pool exhausted — same name appears again).

**Always Outer Disciple** for now. Inner/Elder promotion not implemented.

## Promotion (Not Implemented)

Ranks in enum but no promotion flow. ⏳ TODO when position system from
buildings is ready.

## Related Pages

- [[concepts/gathering-system|Gathering System]]
- [[concepts/crafting-system|Crafting System]]
- [[concepts/purchase-store|Purchase Store]]
- [[concepts/state-management|State Management]]
- [[sources/avatar-appearance|Avatar Appearance]]

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
[MessagePackObject]
public class DiscipleState
{
    [Key(0)] public string DiscipleId { get; set; }
    [Key(1)] public string DisplayName { get; set; }
    [Key(2)] public DiscipleRank Rank { get; set; }
    [Key(3)] public CurrencyWallet Wallet { get; set; } = new CurrencyWallet();
    [Key(4)] public List<InventoryItem> PersonalInventory { get; set; } = new List<InventoryItem>();
    [Key(5)] public string CurrentTask { get; set; }
    [Key(6)] public AvatarAppearance Avatar { get; set; } = new AvatarAppearance();
}
```
> 📎 Source: Assets/Scripts/Shared/SectEconomyState.cs

```csharp
// Example instantiation with the new Avatar system using SlotPart
var disciple = new DiscipleState
{
    DiscipleId = "d000",
    DisplayName = "Liu YiFeng",
    Rank = DiscipleRank.SectMaster,
    // ... existing fields ...
    Avatar = AvatarAppearance.FromSlots(
        new SlotPart(AvatarSlots.Body, "body_robe_azure"),
        new SlotPart(AvatarSlots.Head, "head_male_01"),
        new SlotPart(AvatarSlots.Hair, "hair_topknot_long"),
        new SlotPart(AvatarSlots.Accessory, "acc_jade_crown")
    )
};
```
> 📎 Source: Assets/Scripts/Shared/MockSectData.cs

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

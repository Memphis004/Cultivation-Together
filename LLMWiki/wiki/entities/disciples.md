---
title: Disciples
type: entity
sources:
  - UnityProject/Assets/Scripts/Shared/SectEconomyState.cs
  - UnityProject/Assets/Scripts/Shared/MockSectData.cs
  - UnityProject/Assets/Scripts/Systems/SectStateProvider.cs
related:
  - "[[entities/avatar-appearance]]"
  - "[[sources/sex-gender-system]]"
  - "[[concepts/gathering-system]]"
  - "[[concepts/crafting-system]]"
created: 2026-08-31
updated: 2026-09-04
confidence: high
tags: [disciple, character, rank, mock-data, avatar]
---

# Disciples

The main entities in the game. Each has a rank, wallet, inventory,
and a current task (gathering or crafting).

## Data Model

```csharp
[MessagePackObject]
public class DiscipleState
{
    [Key(0)] public string DiscipleId { get; set; }
    [Key(1)] public string DisplayName { get; set; }
    [Key(2)] public DiscipleRank Rank { get; set; }
    [Key(3)] public CurrencyWallet Wallet { get; set; } = new CurrencyWallet();
    [Key(4)] public List<InventoryItem> PersonalInventory { get; set; } = new();
    [Key(5)] public string CurrentTask { get; set; }
    [Key(6)] public AvatarAppearance Avatar { get; set; } = new AvatarAppearance();
    [Key(7)] public DiscipleSex Sex { get; set; } = DiscipleSex.Unspecified;   // implemented — ใช้เลือก head ตอนสร้าง/สุ่ม
}
```
📎 Source: `Assets/Scripts/Shared/SectEconomyState.cs`

### Avatar field — implemented (parts-only schema, ไม่ใช่ placeholder)

`Avatar` เก็บหน้าตาทั้งตัวของศิษย์เป็น `AvatarAppearance`:

| key | ความหมาย |
|---|---|
| `Parts` | `slot → partId` — **ค่าว่าง = ใช้ default ของ slot นั้น** |
| `Colors` | `slot → colorId` — เฉพาะ slot ที่ `tintable` (schema พร้อม, ยังไม่มี UI) |
| `PoseId` | คงค่าเดียวเสมอ (`""` → fallback `pose_idle_01`) — อ่านเพื่อ pose validation ตอน mutate + filter ตอน Randomize; ไม่มี UI เปลี่ยน (ดู [[entities/avatar-appearance]] §PoseId) |

ทั้ง `Parts` และ `Colors` เป็น `Dictionary<string, string>`
→ **รองรับ slot ใหม่โดยไม่แก้ schema** (สำคัญมากสำหรับ face customization ในอนาคต)

> 🔸 **หมายเหตุ (2026-09-03):** ระบบ **outfit / ชุดสำเร็จรูป ถูกยกเลิก**
> ไม่มี `OutfitId` บน `DiscipleState` และไม่มี `TryApplyOutfit()`
> การแต่งตัว = เปลี่ยน part ทีละ slot ผ่าน `TryChangeAvatarPart()` เท่านั้น
> เหตุผล: [[sources/avatar-appearance]] §1

รายละเอียดครบที่ [[entities/avatar-appearance]]

```csharp
// Example instantiation
var disciple = new DiscipleState
{
    DiscipleId  = "d000",
    DisplayName = "Liu YiFeng",
    Rank        = DiscipleRank.SectMaster,
    // ... existing fields ...
    Avatar = AvatarAppearance.FromSlots(
        new SlotPart(AvatarSlots.Body,      "body_robe_azure"),
        new SlotPart(AvatarSlots.Head,      "head_male_01"),
        new SlotPart(AvatarSlots.Hair,      "hair_topknot_long"),
        new SlotPart(AvatarSlots.Accessory, "acc_jade_crown")
    )
};
```
📎 Source: `Assets/Scripts/Shared/MockSectData.cs`

## Ranks

```csharp
enum DiscipleRank { Unspecified, OuterDisciple, InnerDisciple, Elder, SectMaster }
```

**Ownership rule** (from economy design):
- **Outer / Inner Disciple** → crafted items ไปที่ **Sect Stockpile**
  (คนอื่นซื้อได้ด้วย contribution)
- **Elder / SectMaster** → crafted items ไปที่ **Personal Inventory**

## Mock Data (Current Roster)

From `Shared/MockSectData.cs` — 4 disciples to start:

| ID | Name | Rank | Task | Wallet (stones/contrib) |
|---|---|---|---|---|
| d000 | Liu YiFeng | **SectMaster** | meditation | 1200 / 3400 |
| d001 | Lin Feng | OuterDisciple | gathering_herb | 12 / 340 |
| d002 | Su Yan | InnerDisciple | refining_elixir | 45 / 1120 |
| d003 | Elder Zhao | Elder | forging_artifact | 210 / 4300 |

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

> 🎨 **Chibi activity (Phase 4):** `CurrentTask` ยังขับเคลื่อน gathering/crafting ticks เหมือนเดิม และตอนนี้ **มองเห็นได้ในฉากแล้ว** — `TaskActivityMapper` (data-driven, `chibi_activity_task_map.json`) แปลง task เป็น chibi activity ตอน Reconcile (event-triggered ไม่มี polling): `gathering_*`→Walk, `refining_/forging_/crafting_/training`→Working, `meditation`→Resting, ไม่ match→Idle — ฝั่ง Sprite tier ยังเล่นได้แค่ Idle/Walk (state อื่น fallback เป็น Idle + warn ครั้งเดียว) ดู [[concepts/disciple-visual-system]] §6.2/§11

## Recruitment

Triggered by `new_disciple_applicant` world event → `execute_decision` with
`choiceId` containing "accept" (case-insensitive).

```csharp
public void RecruitOuterDisciple(DiscipleSex sex = DiscipleSex.Unspecified)
{
    var index = _state.Disciples.Count;
    var task = GatheringTasks[index % GatheringTasks.Length];  // round-robin
    var name = RecruitNamePool[index % RecruitNamePool.Length];  // round-robin from placeholder pool

    // Default parity: even index → Male, odd → Female (เมื่อ caller ไม่ระบุ sex)
    var resolvedSex = (sex != DiscipleSex.Unspecified) ? sex
        : (index % 2 == 0) ? DiscipleSex.Male : DiscipleSex.Female;

    var disciple = new DiscipleState {
        DiscipleId = $"d{index + 1:000}",
        DisplayName = name,
        Rank = DiscipleRank.OuterDisciple,
        Wallet = new CurrencyWallet(),
        PersonalInventory = new List<InventoryItem>(),
        CurrentTask = task,
        Sex = resolvedSex,
        Avatar = CreateStarterAvatar(index, resolvedSex),
    };

    _state.Disciples.Add(disciple);
    _discipleRecruitedPublisher.Publish(new DiscipleRecruitedMessage {
        DiscipleId = disciple.DiscipleId,
        DisplayName = disciple.DisplayName,
    });
}
```
> 📎 Source: Assets/Scripts/Systems/SectStateProvider.cs

**Placeholder name pool**: Chen Wei, Bai Ling, Zhou Tao, Xiao Mei, Jiang Yu, Wen Hao
(replaces when pool exhausted — same name appears again).

**Always Outer Disciple** for now. Inner/Elder promotion not implemented.

ศิษย์ใหม่ได้ starter avatar ทันทีจาก `CreateStarterAvatar(index, sex)` — `body_robe_grey` + หัวตามเพศ
(`head_male_01`/`head_female_01`) + ทรงผม round-robin จาก `StarterHair = { "hair_short", "hair_topknot", "hair_twin_tail" }`

## Promotion (Not Implemented)

Ranks in enum but no promotion flow. ⏳ TODO when position system from
buildings is ready.

## Related Pages

- [[entities/avatar-appearance|Avatar Appearance]] — dictionary schema + rendering + customization
- [[concepts/gathering-system|Gathering System]]
- [[concepts/crafting-system|Crafting System]]
- [[concepts/purchase-store|Purchase Store]]
- [[concepts/state-management|State Management]]
- [[sources/avatar-appearance|Avatar Appearance (design source)]]

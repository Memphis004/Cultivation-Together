---
title: Sex / Gender for Disciples (Implemented)
type: gdd
sources:
  - UnityProject/Assets/Scripts/Shared/SectEconomyState.cs
  - UnityProject/Assets/Scripts/Shared/MockSectData.cs
  - UnityProject/Assets/Scripts/Systems/SectStateProvider.cs
  - UnityProject/Assets/Scripts/UI/Presenters/AvatarCustomizationPresenter.cs
related:
  - "[[entities/disciples]]"
  - "[[entities/avatar-appearance]]"
  - "[[concepts/state-management]]"
created: 2026-09-01
updated: 2026-09-04
confidence: high
tags: [disciple, sex, gender, recruitment, avatar, data-model]
---

# Sex / Gender for Disciples (Implemented)

> **สถานะ: ACCEPTED + implemented** — `DiscipleState.Sex [Key(7)]` มีจริงในโค้ดแล้ว
> ตั้งแต่ v0.10 (เดิมหน้านี้เป็น proposal "Addable Property" — ตอนนี้คือ decision record ของสิ่งที่ implement แล้ว)

สรุปสิ่งที่ implement:
- `enum DiscipleSex { Unspecified, Male, Female }` + `[Key(7)] Sex` บน `DiscipleState`
  (`Shared/SectEconomyState.cs`) — serialize ผ่าน MessagePack ไปกับ snapshot อัตโนมัติ
- `MockSectData.Create()` ระบุ `Sex` ให้ทั้ง 4 founder (M/F/F/M)
- `RecruitOuterDisciple(DiscipleSex sex = Unspecified)` รับ sex ได้;
  ถ้าไม่ระบุ → default parity เดิม (index คู่ = Male, คี่ = Female) กัน roster ดูเปลี่ยนไป
- `CreateStarterAvatar(index, sex)` เลือก head ตาม sex (`head_female_01` ถ้า Female, ไม่งั้น `head_male_01`)
- `AvatarCustomizationPresenter` capture `disciple.Sex` ตอนเปิด panel → ใช้กรองหัว/ผมตอน `OnRandomize`
  (`GetPartsForSlot(slot, poseId, sex)`) — ศิษย์ชายไม่สุ่มได้หัวหญิง (และ vice versa)

## 1. Problem ที่ปิดไปแล้ว (historical)

ก่อน v0.10 ไม่มี field ใดเก็บเพศบน `DiscipleState` — เพศเป็นแค่ side effect ของ index:
`CreateStarterAvatar(int rosterIndex)` เลือก head ด้วย `rosterIndex % 2` (male = even, female = odd)
และถามไม่ได้ว่า "ศิษย์ X เพศอะไร" โดยไม่ต้อง parse partId ของหัว/ผม + เรียกศิษย์เพศเฉพาะไม่ได้

```csharp
// ก่อน implement: CreateStarterAvatar(int rosterIndex)
a.SetSlot(AvatarSlots.Head, (rosterIndex % 2 == 0) ? "head_male_01" : "head_female_01");
```

ปัญหานั้นปิดแล้ว — ส่วนที่**ยังไม่ทำ** เหลือแค่ UI surface (§5)

## 2. Design Decision (ยืนยันแล้ว)

> **ใช้ explicit `DiscipleSex` field บน `DiscipleState` — อย่าเก็บเพศไว้ในตัวเลือก slot**

เหตุผล: ตาม invariant "runtime state holds ids/flags, never hidden derivations"
(state-management.md) การเดาเพศจากชื่อ part เป็น bug-wait; enum ถามได้ตรง ๆ,
serialize ได้ และเป็น source of truth เมื่อเนื้อหา gender-gated ตามมา

## 3. Data Model (implemented)

```csharp
[MessagePackObject]
public class DiscipleState
{
    // [Key(0)]..[Key(5)] ...
    [Key(6)] public AvatarAppearance Avatar { get; set; } = new AvatarAppearance();
    [Key(7)] public DiscipleSex Sex { get; set; } = DiscipleSex.Unspecified;
}

public enum DiscipleSex { Unspecified, Male, Female }
```

- `Unspecified` เป็นค่า default ที่ปลอดภัย (roster เก่าที่ serialize ไว้ไม่มี key นี้ → deserialize ได้ 0 ค่า)
- **Backward compatible**: `[Key(7)]` ต่อท้ายตามวินัย append-only ของ MessagePack

## 4. Runtime Wires (implemented)

### 4.1 Recruitment — `SectStateProvider.RecruitOuterDisciple(sex)`

```csharp
public void RecruitOuterDisciple(DiscipleSex sex = DiscipleSex.Unspecified)
{
    var index = _state.Disciples.Count;
    var task = GatheringTasks[index % GatheringTasks.Length];
    var name = RecruitNamePool[index % RecruitNamePool.Length];

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
    // ...
}
```

`CreateStarterAvatar(index, sex)`:

```csharp
private AvatarAppearance CreateStarterAvatar(int rosterIndex, DiscipleSex sex)
{
    var a = new AvatarAppearance();
    a.SetSlot(AvatarSlots.Body, "body_robe_grey");
    a.SetSlot(AvatarSlots.Head, (sex == DiscipleSex.Female) ? "head_female_01" : "head_male_01");
    a.SetSlot(AvatarSlots.Hair, StarterHair[rosterIndex % StarterHair.Length]);
    return a;
}
```

Avatar ตรงกับ state เสมอ — head ถูก derive จาก `Sex` ไม่ใช่จาก index

### 4.2 Existing roster (`MockSectData.cs`) — implemented

Founder ทั้ง 4 ระบุ `Sex` ชัดเจนใน `Create()` → enum มีค่าตั้งแต่วันแรก ไม่ต้องเดาจากหัว:

| id | ชื่อ | Rank | Sex |
|---|---|---|---|
| d000 | Liu YiFeng | SectMaster | Male |
| d001 | Lin Feng | OuterDisciple | Female |
| d002 | Su Yan | InnerDisciple | Female |
| d003 | Elder Zhao | Elder | Male |

### 4.3 Part authoring + การกรอง (`AvatarPartPool`) — implemented บางส่วน

- `AvatarPartDef` ทุกตัวมี `sexTag` (`"male"` / `"female"` / `""` = any) — data พร้อม
- Pool มี overload `GetPartsForSlot(slot, poseId, sex)` กรอง pose/sex (`""` = universal)
- ใช้ที่: **`OnRandomize`** (สุ่มเฉพาะ part ที่เพศตรงกับศิษย์) — implemented
- ยังไม่ใช้ที่: **กริดตัวเลือกใน UI** — แสดงทุก part ของ slot (ผู้เล่นเลือกอิสระตามสเปก v2.5);
  sexTag filter ในกริด = Roadmap ข้อ 5 ของ [[entities/avatar-appearance|Avatar Appearance]]

## 5. UI Surface (⏳ ยังไม่ implement — เป็นงานต่อ)

ตอนนี้ Sex ไม่มี UI ให้แก้ — field ถูก **อ่าน** ฝั่งเดียว (randomize) ไม่ถูกเขียนจาก UI:

1. **Recruit confirmation** (`EventPopup` / `new_disciple_applicant`) — ยังไม่มี sex selector;
   เรียก `RecruitOuterDisciple()` เปล่า → ได้ parity ตาม index
2. **Avatar customization panel — Sex toggle** — ยังไม่มี; ไม่มี `TryChangeSex` บน
   `ISectStateProvider` (ถ้าจะทำ: เพิ่ม method + UI chip ที่หัว panel แล้ว broadcast message ให้ renderer/sync)

ร่างเดิม (ถ้าทำ): chip Male↔Female ที่หัว panel เรียก mutation ผ่าน
`ISectStateProvider` (แบบเดียวกับ `TryChangeAvatarPart`) แล้ว broadcast message ให้ renderer/Sync

## 6. Bridge / MCP (implemented: read / ⏳ write)

- `get_sect_state` พา `[Key(7)] Sex` ไปให้ AI อ่านอยู่แล้ว (MessagePack ทั้ง snapshot — ฟรี ไม่ต้องแก้)
- MCP tool ปัจจุบันที่ bridge เปิด = `get_sect_state`, `await_next_world_event`,
  `execute_decision`, `purchase_item` — ยังไม่มี `set_disciple_sex` (ทำเมื่อ UI ต้องการ)
- หมายเหตุ: `change_avatar_part` (write avatar part) ก็ยังไม่ expose บน bridge เช่นกัน — ดู [[entities/avatar-appearance]]

## 7. Test / Risk

- Old-save round-trip: roster ที่ไม่มี key `Sex` → deserialize ได้ `Unspecified` → render ปกติ
- Parity default: ไม่ระบุ sex → ได้ roster ผสมเหมือนเดิม (head_male_01/head_female_01)
- ไม่กระทบ gathering/crafting/purchase — เป็น data + path การสร้างศิษย์เท่านั้น

## 8. Rollout Status (เทียบกับร่างเดิม)

| # | Change | File | สถานะ |
|---|---|---|---|
| 1 | `DiscipleSex` enum + `[Key(7)] Sex` | `SectEconomyState.cs` | ✅ done |
| 2 | ตั้ง `Sex` ใน `MockSectData` founders | `MockSectData.cs` | ✅ done |
| 3 | `RecruitOuterDisciple(sex)` + `CreateStarterAvatar(index, sex)` | `SectStateProvider.cs` | ✅ done |
| 4 | Recruit popup: sex selector + payload | `EventPopup Presenter/View`, `GameMessages.cs` | ⏳ ยังไม่ทำ |
| 5 | `GetPartsForSlot(slot, sex)` filter | `AvatarPartPool.cs` | 🔶 ทำแล้วเป็น `(slot, poseId, sex)` ใช้ใน Randomize; grid filter ยังไม่เปิด |
| 6 *(optional)* | Sex toggle ใน customization panel (`TryChangeSex`) | `SectStateProvider.cs`, View/Presenter | ⏳ ยังไม่ทำ |
| 7 *(later)* | `set_disciple_sex` MCP write tool | `McpBridge/Program.cs` | ⏳ ยังไม่ทำ |

## Related Pages

- [[entities/disciples]] — `DiscipleState` เจ้าของ field นี้
- [[entities/avatar-appearance]] — `sexTag` บน part + randomize filter + Roadmap grid filter
- [[concepts/state-management]] — `[Key(N)]` MessagePack append discipline
- [[concepts/mcp-bridge]] — `get_sect_state` พา `Sex` ไปแล้ว

---
title: Sex / Gender for Disciples (Addable Property)
type: gdd
.sources:
  - UnityProject/Assets/Scripts/Shared/SectEconomyState.cs
  - UnityProject/Assets/Scripts/Shared/MockSectData.cs
  - UnityProject/Assets/Scripts/Systems/SectStateProvider.cs
  - UnityProject/Assets/Scripts/UI/Views/AvatarCustomizationView.cs
related:
  - "[[entities/disciples]]"
  - "[[concepts/state-management]]"
  - "[[concepts/avatar-appearance]]"
created: 2026-09-01
updated: 2026-09-01
confidence: medium
tags: [disciple, sex, gender, recruitment, avatar, data-model]
---

# Sex / Gender for Disciples — Addable Property

> Feature: give each disciple an **explicit** sex value (Male / Female) so the
> game state and the recruit/edit UI can reference it directly. Today sex is
> only *implied* — `RecruitOuterDisciple()` in SectStateProvider.cs picks head/hair
> by `rosterIndex % 2` (male on even, female on odd), and there is no field to
> query or change it.

## 1. Problem / Why Now

Current state (`disciples.md`, `MockSectData.cs`):

- 4 founder disciples, `RecruitOuterDisciple()` always adds an **OuterDisciple**.
- No `Sex`/`Gender` field exists on `DiscipleState`.
- The only sex signal is a **side effect** of the recruit name/avatar index:

```csharp
// SectStateProvider.cs:231  CreateStarterAvatar(int rosterIndex)
var a = new AvatarAppearance();
a.SetSlot(AvatarSlots.Head, (rosterIndex % 2 == 0) ? "head_male_01" : "head_female_01");
a.SetSlot(AvatarSlots.Hair, StarterHair[rosterIndex % StarterHair.Length]);
```

So sex = "whoever the avatar slots happen to be". You cannot ask "is disciple
X male?" without parsing their head/hair part IDs, and you cannot recruit a
specific sex. That's the gap this doc closes: **make sex a first-class, explicit
data attribute** on the disciple.

## 2. Design Decision (opinionated)

> **Recommend an explicit `DiscipleSex` field on `DiscipleState`: do NOT keep sex
> implicit in slot choice.**

Rationale: the wiki's guiding invariant is *"runtime state holds ids/flags, never
hidden derivations"* (state-management.md). Deriving sex from slot names is a
cryptic bug-wait; an explicit enum is directly queryable, serializable, and
becomes the source of truth when gender affects recruiting flavor, naming, or
future gender-gated content.

## 3. Data Model Change

`DiscipleState` (`SectEconomyState.cs:29`) gains one field, appended with the
MessagePack key discipline already used (`[Key(7)]`, after `Avatar`):

```csharp
[MessagePackObject]
public class DiscipleState
{
    // [Key(0)]-..[Key(6)] Avatar unchanged
    [Key(7)] public DiscipleSex Sex { get; set; } = DiscipleSex.Unspecified;
}
```

```csharp
public enum DiscipleSex { Unspecified, Male, Female }  // new
```

- `Unspecified` is the safe default for loaded/saved roster so a missing value
  doesn't break rendering.
- **Serialization safe**: `[Key(7)]` append reads backward compatibly on old
  saves (same discipline as adding `Avatar` at `[Key(6)]` per
  avatar-appearance.md §3–§4).

## 4. Runtime Wires

All mutation choke points (`SectStateProvider.cs`) + starterset + views.

### 4.1 Recruitment — the primary surface

`RecruitOuterDisciple()` (`SectStateProvider.cs:194`) currently:

```csharp
var disciple = new DiscipleState {
    DiscipleId = $"d{index + 1:000}",
    DisplayName = name,
    Rank = DiscipleRank.OuterDisciple,
    Wallet = new CurrencyWallet(),
    PersonalInventory = new List<InventoryItem>(),
    CurrentTask = task,                  // round-robin
};
```

Change to accept a sex (inject a `RecruitSexPicker` or pass `DiscipleSex?`).
Default keeps parity (even index → Male, odd → Female) to preserve current
"mixed roster" look, but callers/UI can force a sex:

```csharp
var disciple = new DiscipleState {
    ...
    CurrentTask = task,
    Sex = sex,   // <-- new
    Avatar = CreateStarterAvatar(index, sex),             // <-- new param
};
```

`CreateStarterAvatar(index, sex)` (`SectStateProvider.cs:231`) then derives its
default head/hair from the **sex enum** instead of the raw index, so the
avatar implicitly matches state.

### 4.2 Existing roster (`MockSectData.cs`)

Label each founder's sex explicitly in `Create()` so the enum is populated from
day one (currently implied by their head slots). Optional but makes the field
meaningful for the MVP roster.

### 4.3 Avatar defaults by sex (`AvatarPartPool`, authoring)

`AvatarPartPool` (`Data/AvatarPartPool.cs`) is a plain C# loader mirroring
`LubanEventPool` (NOT a ScriptableObject — see avata-appearance.md §6.2). Two
options:

- **A. Filter-by-sex**: `GetPartsForSlot(slot, sex)` returns only parts whose
  `sexTag` matches. Renders per-sex variant automatically.
- **B. Sex-gated default**: the recruit's `Avatar` seeding reads the pool's
  default-by-sex per slot.

Option A is cleaner (avatar variants fully separated from recruit sex) but
requires every `AvatarPart` Def to carry a `SexTag`. Option B is a smaller
sprint (recruit gets a coherent male/female starter) and can grow into A later.

### 4.4 Renderer / UI (no functional change, only data)

`AvatarAppearance` is slot-based with **no built-in sex** — so the renderer
doesn't need edits; it just reads whatever slots are set. The sex field only
becomes visible when **UI** exposes it (see §5).

## 5. UI Surface

New feature = the field must be **settable/displayable** somewhere. Two surfaces:

1. **Recruit confirmation** (`EventPopupPresenter` / `new_disciple_applicant`
   world event consequence) — add a sex selector next to "recruit this disciple".
   Trigger `EventPopup` with a `disciple: {id, sex}` payload.
2. **Avatar customization panel** (`AvatarCustomizationView`,
   `AvatarCustomizationPresenter`) — add a small `Sex` chip/toggle at the top of
   the panel so a player can flip a disciple Male↔Female live. This reuses the
   existing `ISectStateProvider` mutation path (`BuildSectEconomyState` + a new
   `TryChangeSex`).

Presenter pattern stays the same (VContainer Transient, enum-resolved deps).

## 6. Bridge / MCP Expose

`SectEconomyState` already serializes via MessagePack and is served wholesale by
`get_sect_state` — the new `[Key(7)]` enum field is included automatically, so
`get_sect_state` already returns it. If bridge should *edit* sex, follow the
`execute_decision` request/response pattern (NOT the broken TCP-subscribe path,
per game-design-doc patterns) — add `set_disciple_sex` later, day-N+.

## 7. Test / Risk

- Old save round-trip: serialize→deserialize a roster lacking `Sex` ⇒ defaults
  to `Unspecified`, renders fine (Key(7) back-compat).
- Regress: confirm even/odd recruit parity avatar look unchanged after refactor
  (head_male_01 / head_female_01).
- No gameplay effect on gathering/crafting/purchase — pure data + optional UI.
  **Low risk to economy loops.**

## 8. Rollout Plan (tasks, in order)

| # | Change | File | Risk |
|---|---|---|---|
| 1 | Add `DiscipleSex` enum + `[Key(7)] Sex` on `DiscipleState` | `SectEconomyState.cs` | none |
| 2 | Set `Sex` on `MockSectData` founders | `MockSectData.cs` | none |
| 3 | `RecruitOuterDisciple(index, sex)` + `CreateStarterAvatar(index, sex)` | `SectStateProvider.cs` | low |
| 4 | Recruit popup: add sex selector + sex payload | `EventPopup Presenter/View`, `GameMessages.cs` | medium (new UI) |
| 5 *(optional)* | `AvatarPartPool.GetPartsForSlot(slot, sex)` + `TryChangeSex` | `AvatarPartPool.cs`, `SectStateProvider.cs`, new View/Presenter | medium |
| 6 *(later)* | `set_disciple_sex` MCP write tool | `McpBridge/Program.cs` | depends on bridge scope |

## 9. First Change to Make — Answer to "which code first?"

**Start with `SectEconomyState.cs` (task 1 above):** add the `DiscipleSex` enum
and the `[Key(7)] public DiscipleSex Sex { get; set; }` field to `DiscipleState`.
It's a ~3-line, zero-risk, MessagePack-backward-compatible change that makes the
property exist in state before anything wires up around it. Then the recruit
path (`SectStateProvider.cs`) and UI follow in the table order.

## Related Pages

- [[entities/disciples]] — DiscipleState lives here; add the field
- [[concepts/state-management]] — `[Key(N)]` MessagePack append discipline
- [[concepts/avatar-appearance]] — sex-tagged avatar parts, render is slot-only
- [[concepts/mcp-bridge]] — get_sect_state already carries new field

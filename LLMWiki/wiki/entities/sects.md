---
title: Sects (Concept)
type: entity
sources:
  - ../../project_summary.md
related:
  - "[[entities/disciples]]"
  - "[[entities/resources]]"
  - "[[sources/open-questions]]"
created: 2026-08-31
updated: 2026-08-31
confidence: low
tags: [sect, faction, design, deferred]
---

# Sects (Concept)

> The player is the **Sect Master** of a single sect. The broader world has
> other sects but they're not yet implemented.

## Player's Sect

- **Leader**: Liu YiFeng (`d000`, `SectMaster`, "You")
- **Members**: Lin Feng, Su Yan, Elder Zhao + any recruited
- **Stockpile**: starts with 120/340/88/260 (herb/wood/ore/provisions)
- **Buildings**: none yet (BuildingSystem is a stub)

## Other Sects (Designed But Not Implemented)

From `project_summary.md` (game direction):

> **สิ่งปลูกสร้าง**: คลิกสร้างอย่างเดียว ไม่มีระบบวางตำแหน่งบนแมพ (เพื่อไม่ให้
> Agent loop ซับซ้อนเกินไปสำหรับ AI)

> **แผนที่**: มีเมือง/หมู่บ้าน/สำนัก แต่ละที่มีแหล่งเก็บเกี่ยวทรัพยากร

> **ระบบศิษย์**: สมัครเข้ามาเรื่อยๆ (รวมถึงผู้ชมที่กด join) → เริ่มเป็นศิษย์นอก →
> ผู้เล่น/AI แต่งตั้งตำแหน่งได้ จำนวนตำแหน่งปลดล็อกจากสิ่งปลูกสร้าง

## Sect War (⏳ WIP)

Mentioned in design but not implemented:

> **สงครามสำนัก [WIP]**

**Status**: explicitly deferred. No schema yet.

## Spirit Stone Robbery (❓ Open)

> กติกาการปล้นหินวิญญาณระหว่างศิษย์ ยังไม่ลง schema

Gameplay-relevant or world-building? If gameplay, how is it triggered
(random event? betrayal arc?).

## What's Not In This Page

- ❌ Sect economy simulation (rival sect AI)
- ❌ Sect diplomacy / reputation
- ❌ Sect wars
- ❌ Sect ranking / leaderboard
- ❌ Player-vs-player sect competition

All of these are future work. The current MVP is single-sect, single-player.

## Related Pages

- [[entities/disciples|Disciples]]
- [[entities/resources|Resources]]
- [[sources/game-design-doc|Game Design Document]]
- [[sources/open-questions|Open Questions]]

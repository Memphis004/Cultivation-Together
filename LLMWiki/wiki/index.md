---
title: Wiki Index
type: index
sources: []
related:
  - "[[overview]]"
  - "[[conventions]]"
created: 2026-08-31
updated: 2026-08-31
confidence: high
tags: [index, navigation]
---

# Wiki Index

> Master catalog for the Cultivation Together knowledge base.
> **Read this first** on every query — then drill into relevant pages.

## Project

- [[overview|Project Overview]] — high-level summary, tech stack, what's done
- [[conventions|Conventions]] — workflow rules and preferences

## Sources (Human-maintained)

Design documents and historical record:

- [[sources/game-design-doc|Game Design Document]] — vision, pillars, two-currency economy
- [[sources/architecture|Architecture]] — how the subsystems fit together
- [[sources/mechanics|Mechanics]] — per-system design (combat, gathering, crafting, ...)
- [[sources/avatar-appearance|Avatar Customization (Sprite Swap)]] — Heads / Hairs / Bodies / Accessories, sprite-slot data model
- [[sources/sex-gender-system|Sex / Gender for Disciples]] — implemented `[Key(7)] DiscipleSex.Sex` + sex-aware starter avatar (UI selector ยังไม่ทำ)
- [[sources/devlog-history|DevLog History]] — 13 lab rounds, 21-30 Aug 2026
- [[sources/bug-log|Bug Log]] — real bugs + fixes from all labs
- [[sources/open-questions|Open Questions]] — what's deliberately not decided
- [[sources/visual-demo-scene|Visual Demo Scene (DEV-ONLY)]] — DiscipleVisualSystem Phase 3 demo บน Spine example rig + DevSpineOverride seam + guards
- [[decisions/visual-overrides-straight-alpha|Visual Overrides — Straight-Alpha Conversion]] — ADR: atlas ของ rig เฉพาะตัวละคร (Q5) เป็น straight alpha บน Linear color space + วิธี re-export จาก Spine IDE + การเปิด Spine ถาวร (S4 activation — license ยืนยัน 2026-09-25, gate ตั้งที่ composition root)

## Code Snippets (Important Scripts)

- [[sources/code-snippets/GameLifetimeScope.cs.md|GameLifetimeScope.cs]] — composition root
- [[sources/code-snippets/SectStateProvider.cs.md|SectStateProvider.cs]] — state aggregator
- [[sources/code-snippets/WorldEventSystem.cs.md|WorldEventSystem.cs]] — event trigger
- [[sources/code-snippets/TimeSystem.cs.md|TimeSystem.cs]] — game clock + await
- [[sources/code-snippets/GameMessages.cs.md|GameMessages.cs]] — all message DTOs
- [[sources/code-snippets/UIPresenter-and-UIViewBase.cs.md|UIPresenter + UIViewBase]] — MVP base
- [[sources/code-snippets/EventPopupPresenter.cs.md|EventPopupPresenter.cs]] — choice click handler
- [[sources/code-snippets/LogWindowPresenter.cs.md|LogWindowPresenter.cs]] — event log presenter
- [[sources/code-snippets/LogWindowView.cs.md|LogWindowView.cs]] — scrolling TMP log view

## Concepts (AI-maintained)

System-level concepts:

- [[concepts/vcontainer-composition|VContainer Composition Root]]
- [[concepts/message-pipe-bus|MessagePipe Bus]]
- [[concepts/mcp-bridge|MCP Bridge]]
- [[concepts/decision-pipeline|Decision Pipeline]]
- [[concepts/state-management|State Management]]
- [[concepts/time-system|Time System]]
- [[concepts/world-events|World Events]]
- [[concepts/gathering-system|Gathering System]]
- [[concepts/crafting-system|Crafting System]]
- [[concepts/purchase-store|Purchase Store]]
- [[concepts/mvp-ui|MVP UI Pattern]]
- [[concepts/log-window|Log Window]]
- [[concepts/data-pipeline|Data Pipeline (Luban)]]

## Entities (AI-maintained)

Catalogs of game entities:

- [[entities/disciples|Disciples]] — characters
- [[entities/world-events|World Events]] — current event catalog
- [[entities/events|Events]] — event timeline + flow
- [[entities/resources|Resources]] — raw materials
- [[entities/items|Items]] — crafted goods
- [[entities/sects|Sects]] — faction concept (player + others)
- [[entities/avatar-appearance|Avatar Appearance]] — dictionary Parts/Colors schema, layered rendering, customization UI (implemented)

## Quick Reference — Common Questions

| Question | Start here |
|---|---|
| How does the AI play the game? | [[concepts/mcp-bridge]] |
| How do world events work? | [[concepts/world-events]] → [[entities/world-events]] |
| How is state stored? | [[concepts/state-management]] |
| How do decisions apply? | [[concepts/decision-pipeline]] |
| How do I add a new event? | [[concepts/data-pipeline]] → [[entities/world-events]] |
| How do I add a new UI panel? | [[concepts/mvp-ui]] |
| How does the event log work? | [[concepts/log-window]] |
| How do I add a new resource? | [[entities/resources]] |
| What bugs were hit during build? | [[sources/bug-log]] |
| What's not decided yet? | [[sources/open-questions]] |
| What was built in each lab? | [[sources/devlog-history]] |

## Maintenance

- Last index update: 2026-09-25
- Total source pages: 11 (+ 9 code snippets)
- Total concept pages: 14
- Total entity pages: 7
- Total bug log entries: 11 (across 13 labs)

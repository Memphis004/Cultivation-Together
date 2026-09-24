<!-- llm-wiki-log-header-start -->
# Wiki Operation Log

Every ingest, lint run, and maintenance operation is recorded here automatically. For a better experience, use the **Operation History** panel:
- Cmd+P → "View operation history"
- Or open from Settings → Auto Maintenance → Operation History

---
> Append-only chronological record of all wiki operations.
> Format: `## [YYYY-MM-DD] <operation> | <source/page> | <one-line summary>`
> Parseable with: `grep "^## \[" log.md`

## [2026-08-31] new-page | wiki/sources/avatar-appearance.md | GDD for Avatar Customization (Heads/Hairs/Bodies/Accessories) as Sprite Swap layers
## [2026-08-31] wire | wiki/index.md | Linked Avatar Appearance (sources + entities) into the master catalog
## [2026-08-31] wire | wiki/entities/avatar-appearance.md | New entity page documenting AvatarAppearance slot PartId model
## [2026-08-31] lint | wiki | Added avatar slot field to `entities/disciples.md` data model
## [2026-08-31] init | LLMWiki | Created LLM Wiki for Cultivation Together (Karpathy pattern)
## [2026-08-31] scan | UnityProject/ | Read full project structure, 13 lab rounds, all C# scripts
## [2026-08-31] scaffold | AGENTS.md | Wrote schema customized to this project (tech stack, conventions, code citation)
## [2026-08-31] scaffold | wiki/overview.md | Created project summary grounded in actual code
## [2026-08-31] scaffold | wiki/conventions.md | Documented workflow rules + open-questions tracker
## [2026-08-31] scaffold | wiki/sources/game-design-doc.md | Synthesized GDD from project_summary.md
## [2026-08-31] scaffold | wiki/sources/architecture.md | Wrote architecture page grounded in real code
## [2026-08-31] scaffold | wiki/sources/mechanics.md | Per-system design notes (8 systems)
## [2026-08-31] scaffold | wiki/sources/devlog-history.md | 13 lab rounds summarized
## [2026-08-31] scaffold | wiki/sources/bug-log.md | 11 real bugs with root causes + lessons
## [2026-08-31] scaffold | wiki/sources/open-questions.md | 14 deliberately-unresolved design questions
## [2026-08-31] scaffold | wiki/sources/code-snippets/ | 7 important scripts documented (GameLifetimeScope, SectStateProvider, WorldEventSystem, TimeSystem, GameMessages, UIPresenter, EventPopupPresenter)
## [2026-08-31] scaffold | wiki/concepts/ | 12 concept pages (composition, messagepipe, mcp, decision, state, time, world-events, gathering, crafting, purchase, mvp-ui, data-pipeline)
## [2026-08-31] scaffold | wiki/entities/ | 6 entity pages (disciples, world-events, events, resources, items, sects)
## [2026-08-31] scaffold | wiki/index.md | Master catalog with quick-reference table
## [2026-09-01] new-page | wiki/concepts/log-window.md | LogWindow architecture, data flow, and code snippets
## [2026-09-01] new-page | wiki/sources/code-snippets/LogWindowPresenter.cs.md | Presenter snippet for event log
## [2026-09-01] new-page | wiki/sources/code-snippets/LogWindowView.cs.md | View snippet for scrolling TMP log
## [2026-09-01] wire | wiki/concepts/mvp-ui.md | Added LogWindow to Implemented Panels (2→3)
## [2026-09-01] wire | wiki/sources/architecture.md | Added LogWindow to UI box in architecture diagram
## [2026-09-01] wire | wiki/index.md | Linked LogWindow concept + code snippets into master catalog
## [2026-09-01] new-page | wiki/sources/sex-gender-system.md | Drafted GDD for adding explicit Disciple Sex (Male/Female + RecruitSexPicker) to DiscipleState
## [2026-09-22] update | wiki/entities/avatar-appearance.md | [Key(8)] ChibiBackend เป็น render-active แล้ว (Phase 3 — VisualTierPolicy + TrySetChibiBackend; Spine ยัง INERT รอ S4 license)
## [2026-09-22] update | wiki/concepts/disciple-visual-system.md | §11 ทำเครื่องหมาย Phase 1/2/3 acceptance ☑ (Phase 3 ผ่าน verify 24/24 ด้วย example rig — Spine render ยัง INERT รอ license)
## [2026-09-22] update | wiki/concepts/disciple-visual-system.md | §11 ทำเครื่องหมาย Phase 4 ✅ (TaskActivityMapper + ChibiClickTarget + DiscipleDetail panel; verify 27/27)
## [2026-09-22] update | wiki/concepts/mvp-ui.md | เพิ่ม DiscipleDetail ใน Implemented Panels (4→5) + UIService mapping
## [2026-09-22] update | wiki/entities/disciples.md | CurrentTask ขับเคลื่อน chibi activity ในฉากแล้ว (TaskActivityMapper — derive-on-reconcile)
## [2026-09-22] update | wiki/concepts/disciple-visual-system.md | §11 ทำเครื่องหมาย Phase 5 ✅ (entitlement ชั้น 6 + UI lock + EntitlementRandom; verify 20/20) + §14 ปิด Q2 (Preset+Reroll = Randomize + filter)
## [2026-09-23] new-page | wiki/sources/visual-demo-scene.md | Visual Demo Scene (DEV-ONLY): วิธีเปิดเดโม, ปุ่ม→สิ่งที่พิสูจน์, mapping mix-and-match-pro, คำเตือน DevSpineOverride + R1/S4, guards T8
## [2026-09-23] wire | wiki/index.md | Linked visual-demo-scene ใน Sources (9→10 หน้า)
## [2026-09-23] update | wiki/concepts/disciple-visual-system.md | §11 Phase 3: เพิ่ม ground-truth demo render acceptance (4 chibi ครบ, d002 หายพัง, HUD 7 ปุ่ม, cam depth=10, 0 exception — demo_shot.png + visual_spike_result.txt)
## [2026-09-23] new-tool | scripts/analyze_demo_shot.py + hud_profile.py | ตัวช่วยวิเคราะห์ backbuffer capture (ASCII density map + HUD band profile) สำหรับยืนยัน demo รอบถัด ๆ ไป
## [2026-09-23] bugfix | ChibiSheetBaker.cs | smoke test หลัง commit จับได้ว่า chibi หายเมื่อเฟรม >= 1: painter ไม่เคยชดเชย x ตามคอลัมน์เฟรม (ทุกเฟรมวาดทับ x=0, คอลัมน์ 1-5 ว่าง) — เพิ่ม _xOffset + clip ใน Fill, re-bake แล้ว demo ผ่าน 4/4 chibi ทุกเฟรมมีเนื้อ (สคริปต์ check_sheet_frames.py ใช้ยืนยัน per-frame coverage)
## [2026-09-23] verify | smoke test 3 รอบติดกัน (6 captures: 2 จังหวะ/รอบ, boot ใหม่ทุกรอบ) | 4/4 chibi มองเห็นทุกภาพ (fg 30-41% sprite, 18% spine), dump ครบทั้ง 3 รอบ (10 sprites ไม่ NULL, spine attachments=32), console 0 errors — ความเสถียรยืนยันหลังแก้ baker
## [2026-09-24] update | wiki/concepts/disciple-visual-system.md | Track ขนาน Face split ✅ (Roadmap #1): 41 parts ครบ face slots + defaults, draw window 30–39, placeholder 256×384 bake ผ่าน bake_portrait, coverage rule ใหม่ (IsEmptyLayer ผ่านเสมอ / non-empty ต้องมี art ≥1 backend ที่เปิด), resolver ข้าม empty-layer เงียบ — ยอมรับผ่าน EditMode 44/44 + check_portrait_pngs.py 0 failures + demo_verify pass=33 fail=0 + console 0 errors
## [2026-09-24] update | wiki/sources/avatar-appearance.md | Draw stack ขยาย face window (head 30 → brows 31 → eyes 32 → nose 33 → mouth 34 → face_marking 35 → eyeshadow 36–39 → hair 40) + §3.3 ทำเครื่องหมาย drawOrder ซอยแล้ว + Roadmap #1 ✅ (placeholder รอ art จริง)
## [2026-09-24] new-tool | scripts/check_portrait_pngs.py + PortraitPlaceholderBaker.cs (Editor, command bake_portrait) | verifier PNG portrait (alpha/centroid/bbox, head-center 128,262 y-up) + baker แผ่น placeholder 36 ไฟล์ลง Assets/Resources/Avatar — ใช้ยืนยัน invariant ทุกครั้งที่ re-bake

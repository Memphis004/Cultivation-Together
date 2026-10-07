# Ink UI — Checkpoint B

## Scope
Code-only redesign. No diffusion generation or Phase 4. Existing public signatures, message schemas, Shared/Luban/MCP/interprocess unchanged. Legacy AgentScripts untouched. Backup: art/ui_v1_backup/ (90 files, including original UI sources/prefabs and meta files).

## Verification
- Unity compilation completed without errors after final production-source repairs.
- EditMode: 182 passed / 182 total, 0 failed, skipped or modified tests; final run 1.7 seconds.
- Live v2 verifier: 662 checks, 0 failures, 0 new UI errors, 3 open/close rounds at both 1920x1080 and 1498x685.
- Reports: art/ink_ui_verification.json (1920x1080), art/ink_ui_verification_1498x685.json.
- UI controls exercised: bottom menu, card Button, rail Button, tabs, X, list Esc callback, search filtering, sort, disabled-action pointer tooltip.
- Rail instance IDs stable across reopening and switching. Layered portrait pool expands on first use; subsequent switches stable. TMP-generated submesh children are excluded from the structural-child count, not from missing-script scans.
- Generated prefabs: List 19 Transform nodes, Detail 109 nodes; missing scripts 0 in both. Both generators run twice with no duplicate panel children.
- Missing-icon test: solid circular fallback, warning once for repeated path.
- Real recruit test (Play Mode only, discarded on exit): d005 Jiang Yu, List/Detail layered fallback 4 active sprite layers, rail solid fallback, no crash.
- Search graphic raycast resolves to TMP_InputField.
- HUD Canvas overrideSorting=true, order=20, no overlap with window at both tested sizes. Window remains local 1400x790 and scale=1 at 1920x1080; smaller/taller-aspect canvas scales the window down to reserve top/bottom bars.
- Console errors during passing live runs: 0. Editor retained one capture-command path-validation error afterward; earlier tool/generator errors were diagnosed and repaired. Do not describe the entire historical console buffer as error-free.
- git diff --check for edited C# sources passed. Global diff --check reports Unity-generated YAML trailing spaces and pre-existing/dynamic font changes; not reported as a pass.

## Rects
Coordinates below are (left, top, width, height) in Window-local top-left coordinates, canvas units.

List window 1400x790:
- TitlePlate (24,20,420,56)
- Search (780,24,320,48)
- SortButton (1120,24,184,48)
- CloseButton (1328,20,48,48)
- Viewport (70,96,1260,610)
- Footer (28,724,1344,46)
- Cards 236x316, 5 columns, spacing 20; card portrait 136x204 (2:3)

Detail window 1400x790:
- TabRail (24,20,1230,52)
- CloseButton (1328,20,48,48)
- PortraitColumn (24,90,408,602)
- Portrait within column (54,12,300,450), 2:3
- Actions within column (54,476,300,48), (54,538,300,48)
- Header (452,90,924,62)
- ContentHost (452,170,924,522)
- Status groups within host (0/312/624,52,300,240)
- RailViewport (24,712,720,64), icon mask 56x56, outer ring 60x60
- WalletFooter (766,712,610,64)
Main sibling blocks contained in window and pairwise non-overlapping by live rect checks.

## Palette
Paper #D6D2C8; secondary paper #BFBAB0; portrait #1E1C1A; ink #232020; primary text #2A2622; secondary text #4C473F; light text #E8E4DA; vermilion #B7642C; danger #A33228; jade #4E7A5E; blue #43637E; disabled #AAA59B; backdrop black alpha 0.6. White is used only as neutral sprite tint. Accent rank text uses contrast-safe ink; accents remain on small seals/dots/borders.
Contrast ratios: secondary/PaperDark 4.76; secondary/Paper 6.10; primary/PaperDark 7.77; light/Ink 12.73; primary/disabled 6.12. Original secondary #6B655C was not used because it failed 4.5:1.

## Original causes
- EventPopup: decision popup centered under common UIRoot; UIService appends/reopens it as last sibling. Fixed by pending UI queue and hidden/non-interactive existing popup while disciple modals are visible.
- Rail: HeadIcon mutated the 56x56 root with scale 4.5 and y=-350; no per-item mask; ring was a square sprite-less Image; runtime layer prototype was not stretch-configured. Replaced rail internals with masked baked Images and existing pool semantics.
- Header: rank rect and close rect overlapped 14x33 units. New top tab row and separate header rect do not overlap.
- HUD: earlier sibling than modal/backdrop, no separate sorting. Live non-16:9 canvas additionally put the top bar into window bounds. Added HUD nested sorting and modal responsive scaling, without changing root canvas settings.
- Card readability: rank/task #9E7A47 on tier-colored body, 24px text. New paper surface, 26px minimum body text, centralized contrast-safe text colors.

## Changed/created files
Relative to UnityProject:
- Assets/Editor/DiscipleListPanelGenerator.cs
- Assets/Editor/DiscipleDetailPanelGenerator.cs
- Assets/Prefabs/UI/DiscipleListPanel.prefab
- Assets/Prefabs/UI/DiscipleDetailPanel.prefab
- Assets/Scripts/UI/Views/DiscipleListView.cs
- Assets/Scripts/UI/Views/DiscipleDetailView.cs
- Assets/Scripts/UI/Views/AvatarRenderer.cs (reuse resolver; missing-sprite warnings once per path)
- Assets/Scripts/UI/Views/EventPopupView.cs
- Assets/Scripts/UI/Views/WalletHudView.cs
- Assets/Scripts/UI/Presenters/DiscipleListPresenter.cs
- Assets/Scripts/UI/Presenters/DiscipleDetailPresenter.cs
- Assets/Scripts/UI/Presenters/DiscipleDetailTabPresenters.cs
- Assets/Scripts/UI/Systems/WorldEventUISystem.cs
- Assets/Scripts/UI/Widgets/UiPalette.cs (+meta)
- Assets/Scripts/UI/Widgets/InkWidgets.cs (+meta)
- Assets/Scripts/UI/Widgets/InkTooltip.cs (+meta)
- Assets/Scripts/UI/Widgets/DiscipleModalScope.cs (+meta)
- Assets/Resources/ui/ink/circle.png, seal.png (+metas/folder meta): mathematical text-free primitives, not generated illustration.
Repository artifacts: Tools/art/verify_ink_ui.cs; art verification JSON reports; this report; art/ui_v1_backup/.
Catalog references stayed identical, so no catalog content change was needed. Unity refreshed csproj and dynamic TMP font assets as editor side effects; these were not intentionally edited as source changes and must not be reverted over pre-existing user changes.

## Limitations / user review required
- User must decide visual acceptance in Game View. Inline composited captures were shown; attempted saved screenshots did not include trustworthy overlay content and were removed.
- No ink-splatter artwork: code-only 2px ink outline currently used. Phase 4 remains unstarted and requires explicit instruction.
- No capacity/upkeep fields exist: footer explicitly says data unavailable, not invented 4/20 or monthly costs.
- Assignment/gift actions are disabled in this screen with exact tooltip; no new unlock rules or action workflow was invented.
- Current mock roster has one legacy layered placeholder appearance (d001); no replacement portrait was generated.
- Checks establish stable UI/rail child counts across representative switches, not a full memory-profiler leak proof.
- Portrait missing-sprite warning path was inspected and made warn-once; actual missing icon and no-override recruit were exercised. Destructive removal of shipped portrait assets was not performed.
- Stat tables are explicitly labelled examples and do not claim real attributes.

## Next step
Stop at Checkpoint B for user Game View review. Optional adjustments remain code-only unless user explicitly authorizes Phase 4 art kit.

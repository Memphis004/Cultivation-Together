# Track B — import investigation (read-only)

Scope: read-only. Nothing under UnityProject/ was written; no tests were run.
Facts checked against files on disk (2026-10-12). Paths are under `UnityProject/Assets/` unless noted.

## Verified data facts
- `Resources/Data/avatar_parts.json`: top-level `{"parts": [...]}`, **41 entries**. Entry keys:
  `id, slot, category, displayName, spritePath, spritePathBack, thumbPath, drawOrder, drawOrderBack, isDefault, tintable, poseId, sexTag, chibiSheetPath, chibiSheetPathBack, chibiOrder, chibiOrderBack`.
  `spineSkin` and `entitlement` are **absent** from the JSON for most entries (only `acc_jade_crown` has `entitlement: "owner"`).
- Defaults: `base_silhouette`, `body_robe_grey`, `head_male_01`, `hair_short`, `acc_none` have `isDefault: true`. `head_female_01` has `sexTag: "female"`, `isDefault: false`.
- `acc_none`: `spritePath: ""`, `chibiSheetPath` set, default for `accessory`.
- Body/head/hair/accessory entries all have `chibiSheetPath` set (except face parts, which are portrait-only).
- PNG files live at `Resources/Avatar/<id>.png` (256x384 for the existing art, not 1024x1536). Each has a `.meta`.

## Q1 — who writes/validates avatar_parts.json
- **No Editor generator writes `avatar_parts.json`.** A grep for `WriteAllText`/`File.Write` near avatar/part returns only `Editor/AvatarIconBaker.cs:71` (writes icon PNGs). Hand-editing the JSON will not be overwritten by a generator. Source of truth = the JSON file itself.
- `Scripts/Visual/Spikes/Editor/VisualCoverageValidator.cs`: an Editor tool. It checks (1) per-slot coverage, (2) duplicate `spineSkin`, (3) for parts **used by the live roster** only, that `chibiSheetPath` is non-empty (lines ~133-143). Findings are `Debug.LogError`; the tool does **not** fail tests. A new part not referenced by the roster produces no error. It is run from `VisualSpikeRemoteControl.cs:97`.
- EditMode: `Tests/EditMode/AvatarPartCoverageTests.cs` checks face parts are portrait-only and that `acc_none` passes coverage. It does not assert chibi coverage for body/head/hair/accessory. A grep for `Validate(` in `Tests/EditMode` found no call to `VisualCoverageValidator`.
- **Answer:** adding entries that have `chibiSheetPath = ""` will **not fail** an existing test. Only the editor tool would log an error if such a part were in the live roster. (Fact-check of "validator/coverage check ... ล้มหรือแค่รายงาน": it reports, it does not fail.)

## Q2 — callers of GetPartsForSlot, sex mapping, UI visibility
- `Scripts/Data/AvatarPartPool.cs:203` `GetPartsForSlot(slot)` (no filter). Callers: `AvatarCustomizationPresenter.cs:346` (grid), `Editor/...` tests.
- `AvatarPartPool.cs:217` `GetPartsForSlot(slot, poseId, sex)`. Caller: `AvatarCustomizationPresenter.cs:187` (randomize).
- Sex string (`AvatarCustomizationPresenter.cs:180-182`): `_discipleSex.ToString().ToLowerInvariant()` → `"male"` / `"female"`; `DiscipleSex.Unspecified` → **`""`**.
- **Blocker:** the filter at `AvatarPartPool.cs:217-230` is `if (!string.IsNullOrEmpty(p.sexTag) && p.sexTag != sex) continue;`. With `sex = ""`, any part with `sexTag = "female"` is **excluded**. So a disciple with `Unspecified` sex never sees female-tagged parts in randomize. (Existing female head is the same situation.)
- Grid (`GetPartsForSlot(slot)`) shows every part with no sex filter, so a new part appears there as soon as it is in the JSON.
- Entitlement: `AvatarCustomizationPresenter.cs:363-364` sets `IsLocked` from `_entitlementProvider.CanUse`. `Entitlement` is read in `EntitlementRandom.cs` and `SectStateProvider.cs:745-751`. Empty = always usable (`DefaultEntitlementProvider.cs:44`).
- `thumbPath`: only the field comment in `AvatarPartPool.cs:16` references it. No runtime reader was found.

## Q3 — blast radius of SpriteSheet/Spine and missing chibi
- SpriteSheet readers: `Scripts/Visual/Sprite/ChibiFrameBank.cs:113-115` (`chibiSheetPath`), `SpriteChibiVisual.cs:111` (`Supports(SpriteSheet)`), `AppearanceResolver.cs:180` (`case SpriteSheet: return def.chibiSheetPath`).
- Spine readers: `SpineChibiVisual.cs`, `AppearanceResolver.cs:152,159,167` (`spineSkin`). Production JSON has **no** `spineSkin`, so Spine layers are empty for all parts today.
- Resolver behaviour (`AppearanceResolver.cs:50-94`): if `def.IsEmptyLayer` → silent skip. If `!def.Supports(backend)` → `WarnOnce(slot, backend, def)` and **skip the slot**. There is **no fallback to the slot default** when the part exists but doesn't support the backend (the comment at line 28 says it does; the loop body does not do that).
- **Consequence:** a new part with empty `chibiSheetPath` selected by the player → on the SpriteSheet backend that slot is **not drawn** (the character loses that layer, e.g. no hair/body). Same for Spine with empty `spineSkin`. The Portrait backend is unaffected because `Supports(Portrait)` only needs `spritePath`.

## Q4 — who references part ids (look-alike risk)
- `Scripts/Shared/MockSectData.cs`: `hair_topknot_long` (line 48), `body_robe_grey` (67), `hair_short` (69), `head_female_01` (88). These are the only mock references found. None of them is a new id, so **new parts are not used by any disciple unless someone assigns them**.
- `Tests/EditMode/AppearanceResolverTests.cs`, `ChibiFrameBankTests.cs` and others use existing ids only.
- Default-slot changes would affect all disciples with empty slots: the new parts must have `isDefault = false`.

## Q5 — existing art (PIL read, 256x384 RGBA)
| file | size | alpha bbox (x, y) | centroid (x, y) | head |
|---|---|---|---|---|
| base_silhouette | 256x384 | x 80–175, y 70–323 | 127.5, 203.4 | not measured separately (no head sub-box) |
| head_male_01 | 256x384 | x 88–167, y 74–177 | 127.5, 124.3 | head 74–177 = **103 px** (canvas units ×4 = 412 px) |
| head_female_01 | 256x384 | x 88–167, y 74–177 | 127.5, 124.3 | same as male |
| body_robe_grey | 256x384 | x 58–197, y 170–317 | 127.5, 238.7 | shoulders at 170 |
| hair_topknot_long | 256x384 | x 86–169, y 38–143 | 127.5, 81.7 | top at 38 (above head) |
| hair_topknot_long_back | 256x384 | x 82–173, y 88–177 | 127.5, 132.5 | — |

- Head centre target (512,488) on 1024x1536 = (128,122) on 256x384. The existing head centre ≈ (127.5, 124.3) → **matches the target** within 2 px (existing heads are already on the target).
- Target head height 240 px on 1024x1536 = **60 px** on 256x384. The existing head box is ~103 px tall, so the existing art is **not** 240 px head-height; it is a different scale. A part drawn at the canvas scale of the new 1024x1536 layers will not line up with the old 256x384 layers unless both use the same canvas. Layer PNGs must use the same canvas size as the existing art; the new layers are 1024x1536 per the brief, which conflicts with the 256x384 convention. **This must be decided before Phase 2.**
- `base_silhouette.png`: bbox y 70–323 reaches the hem region. Robe `body_robe_grey` bbox 170–317 sits within it. Whether it shows under a full-length body cannot be judged from numbers alone (report only).

## Q6 — import settings
- Existing `.meta` (base_silhouette, head_female_01, hair_topknot_long): `textureType: 8` (Sprite), `spriteMode: 1` (Single), `alphaIsTransparency: 1`, `spritePixelsToUnits: 100`, `maxTextureSize: 2048`, compression quality 50.
- Mechanisms found: `Editor/PortraitOverrideImporter.cs` (folder `Assets/Resources/Avatar/Portraits` only, sets Sprite/Single/alpha/mipmap/Bilinear); `Editor/UiSpriteImporter.cs` (folder `Resources/ui/disciple_list`); `AvatarIconBaker.ConfigureImport` (icons under `Avatar/Icons`, Point filter).
- **No AssetPostprocessor covers `Resources/Avatar/<id>.png`** (the parts folder). New PNGs would get Unity defaults (Texture 2D, not Sprite) and `Resources.Load<Sprite>` returns null. The existing `.meta` values must be applied by a mechanism that does not exist in the repo yet (a new postprocessor, a preset, or a one-off Editor script). This is a **blocker**.

## Q7 — RectTransform / layerRoot
- `Scripts/UI/Views/AvatarRenderer.cs:55,77,97-99`: `layerRoot` is a serialized RectTransform; if null it uses `transform`. Framing (`line 119-120`) sets `layerRoot.localScale` and `localPosition`. Layer Images are created under `layerRoot` (`line 257`).
- Layer prefab: `Assets/Prefabs/Avatar/LayerImagePrefab.prefab` (from `Editor/DiscipleDetailPanelGenerator.cs:27`). Its RectTransform size was **not** inspected in this pass (report-only item left open).
- HeadIcon scale 4.5 and pivot come from the framing preset (AvatarRenderer lines ~29, 119-120). AvatarIconBaker uses the layered path via `AvatarIconCrop.ContainsHeadCenter` (`Editor/AvatarIconBaker.cs:120`).

## Q8 — hiding eyes/brows/nose/mouth over a painted head
- `AppearanceResolver.cs` has no slot-hiding logic for eyes/brows/nose/mouth. The presenter (`AvatarCustomizationPresenter.cs`) has no `Hide`/`SetActive` for those slots (grep returned nothing).
- **No way to hide these slots per head today.** Choosing `eyes_*` over a painted head will stack. A solution needs a rule in the resolver or data (e.g. a `hideSlots` field), which is a code change, not in scope for this pass.

## Q9 — part_prompts / check_part_prompts
- `.agents/skills/disciple-portrait-prompt/assets/part_prompts.json` and `scripts/check_part_prompts.py` are **missing** from the repo; `compose_portrait_prompt.py` loads `part_prompts.json` at import time and fails without it (observed). Their entry format cannot be verified here. Blocker for adding part ids to the prompt tooling; the isolate path does not need it.

## Q10 — acc_none warnings
- `acc_none` is the default for `accessory`, `spritePath = ""`, `chibiSheetPath` set. `IsEmptyLayer` is false (chibi is set), and `Supports(Portrait)` is false (no spritePath), so `AppearanceResolver.cs:82` calls `WarnOnce(accessory, Portrait)`: **one warning per process** already exists today.
- New parts (Phase 2) will not add a second warning for the same slot/backend pair, but a new non-default accessory without `spritePath` would add its own `WarnOnce` on Portrait. Keep `spritePath` filled for every new part so the warning stays limited to the existing `acc_none` case.

## Blockers (must be resolved before Phase 2)
1. **Canvas mismatch:** existing Resources/Avatar art is 256x384 with a ~103 px head; the new layers are planned at 1024x1536 with a 240 px head. One convention must be chosen.
2. **No importer for `Resources/Avatar/<id>.png`:** Sprite/Single/alpha/PPU settings need a mechanism (new postprocessor/preset) before Phase 2.
3. **Unspecified-sex filter:** `sexTag` parts are hidden from randomize for `Unspecified` disciples (`AvatarPartPool.cs:217-230`). Decide whether the new parts should use `sexTag` at all.
4. **Missing `part_prompts.json` / `check_part_prompts.py`:** prompt tooling cannot validate new ids.
5. **No eye/brow/nose/mouth hiding:** any part worn over a painted head will overlap.
6. **SpriteSheet/Spine fallback:** a part with empty chibi/spine is dropped on that backend without fallback (`AppearanceResolver.cs:82`). New parts must carry `chibiSheetPath` (and `spineSkin` later) or the chibi/spine view will lose the slot.

## Still unanswered (need user input or further reading)
- Actual Prefab RectTransform size of `LayerImagePrefab.prefab` and the framing preset numbers (Q7).
- Whether `part_prompts.json` exists anywhere else in the repo (not found under the skill folder).
- Which canvas convention is intended for layer PNGs (256x384 existing vs 1024x1536 planned).
- Whether `sexTag` should stay on new parts given the Unspecified-sex filter.
- Whether the Phase 2 import should set `chibiSheetPath` to an empty string (it will then be dropped on SpriteSheet/Spine backends).

## Carry-over for the next round (from the user, 2026-10-12)
- Phase 1 is on hold until the isolate prompt is redesigned. The head test failed because the output kept the full body.
- Remove "two clearly visible braids" from the baseline template prompt (`art/portraits_v2/workflows/d001_seed430101.json`).
- Disconnect node 11 (image_3) from the encoder in the new round's workflow, and make the prompt match whichever references remain.

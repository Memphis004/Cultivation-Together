# CHECKPOINT B — all 5 assets, 4 seeds each

Status: **waiting for you to name `asset_id -> seed` for every asset.**
Unity is still completely untouched. Nothing has been imported or wired.

## Look at these

| sheet | asset |
|---|---|
| `art/disciple_list/contact/scroll_paper.png` | scroll_paper (round 2 — the set you approved) |
| `art/disciple_list/contact/scroll_rod.png` | scroll_rod |
| `art/disciple_list/contact/close_medallion.png` | close_medallion |
| `art/disciple_list/contact/cloud_corner.png` | cloud_corner |
| `art/disciple_list/contact/title_plate.png` | title_plate |
| `art/disciple_list/contact/scroll_paper_round1_vs_round2.png` | scroll_paper round 1 vs round 2 (8 cells) |

Each sheet is a 2x2 grid on a checkerboard with the seed printed on every cell.
Individual sprites are in `art/disciple_list/final/`.

## Verified numerically

### Sizes, alpha and key safety — all 20 sprites

| file | final | target | w off | h off | near-magenta | upscaled |
|---|---|---|---|---|---|---|
| scroll_paper_seed101 | 460x558 | 460x560 | 0.0% | 0.4% | 0 px | no |
| scroll_paper_seed202 | 459x560 | 460x560 | 0.2% | 0.0% | 0 px | no |
| scroll_paper_seed303 | 458x560 | 460x560 | 0.4% | 0.0% | 0 px | no |
| scroll_paper_seed404 | 460x558 | 460x560 | 0.0% | 0.4% | 0 px | no |
| scroll_rod_seed111 | **463x40** | 560x40 | **17.3%** | 0.0% | 0 px | no |
| scroll_rod_seed222 | 546x40 | 560x40 | 2.5% | 0.0% | 0 px | no |
| scroll_rod_seed333 | 533x40 | 560x40 | 4.8% | 0.0% | 0 px | no |
| scroll_rod_seed444 | 530x40 | 560x40 | 5.4% | 0.0% | 0 px | no |
| close_medallion_seed121 | 118x240 | 120x240 | 1.7% | 0.0% | 0 px | no |
| close_medallion_seed232 | 120x235 | 120x240 | 0.0% | 2.1% | 0 px | no |
| close_medallion_seed343 | 120x225 | 120x240 | 0.0% | 6.2% | 0 px | no |
| close_medallion_seed454 | 120x239 | 120x240 | 0.0% | 0.4% | 0 px | no |
| cloud_corner_seed131 | **180x151** | 180x270 | 0.0% | **44.1%** | 0 px | no |
| cloud_corner_seed242 | 180x269 | 180x270 | 0.0% | 0.4% | 0 px | no |
| cloud_corner_seed353 | 180x270 | 180x270 | 0.0% | 0.0% | 0 px | no |
| cloud_corner_seed464 | 180x269 | 180x270 | 0.0% | 0.4% | 0 px | no |
| title_plate_seed141 | 319x54 | 320x54 | 0.3% | 0.0% | 0 px | no |
| title_plate_seed252 | 320x54 | 320x54 | 0.0% | 0.0% | 0 px | no |
| title_plate_seed363 | 319x54 | 320x54 | 0.3% | 0.0% | 0 px | no |
| title_plate_seed474 | 320x54 | 320x54 | 0.0% | 0.0% | 0 px | no |

- every file is RGBA with a real alpha channel
- **near-magenta = 0 px on all 20**, total and visible
- **nothing was upscaled**
- only **two** seeds are far off their target box: `cloud_corner_seed131` (44% short) and
  `scroll_rod_seed111` (17% short). Those two seeds drew a shape that did not fill the
  guide's proportions, so avoid them unless you like them anyway.

### 9-slice borders (measured on the final sprites)

| asset | per-seed left/right bands | proposed spriteBorder |
|---|---|---|
| scroll_paper | (38,36) (51,51) (40,34) (38,41) | `left=39, bottom=0, right=38, top=0` |
| title_plate | (15,13) (23,21) (17,14) (14,14) | `left=16, bottom=0, right=14, top=0` |

These are provisional medians. The committed border will be recomputed from the one
seed you pick per asset.

### Actual generated canvases — the budget is NOT a side length

| asset | actual canvas | megapixels | predicted |
|---|---|---|---|
| scroll_paper | 928x1120 | 1.039 MP | 928x1120 (correct) |
| scroll_rod | **3840x288** | 1.106 MP | 1344x96 (WRONG) |
| close_medallion | **736x1440** | 1.060 MP | 448x896 (WRONG) |
| cloud_corner | 800x1280 | 1.024 MP | 800x1280 (correct) |
| title_plate | **2496x416** | 1.038 MP | 2464x416 (near miss) |

All five land near the 1024x1024 budget in **total pixels**, at whatever canvas shape
their guide's aspect needs. My earlier per-asset predictions were wrong for the extreme
aspects; the runner records the true size from the output file, and the manifest now
carries the corrected numbers.

## The bug I found and fixed after the first Phase 4 run

The first Phase 4 pass produced sprites that were **20-41% short in one dimension**.
Root cause was mine, in the guide specs: I set each guide's **canvas** to the target
aspect but drew the **object** smaller inside it, so the object's bounding box had the
wrong aspect. `postprocess.py` fits the trimmed object into the target box preserving
aspect, so the shortfall went straight into the sprite.

| asset | guide object aspect (before) | target aspect | sprite came out |
|---|---|---|---|
| scroll_rod | 18.50 | 14.00 | 560x34 |
| close_medallion | 0.398 | 0.500 | 97x240 |
| cloud_corner | 0.939 | 0.667 | 180x190 |
| title_plate | 7.91 | 5.93 | 320x41 |

Fixed by reshaping each guide so the object bbox aspect equals the target aspect, then
re-generating those four assets. After the fix the object aspects are 13.875 / 0.5006 /
0.6667 / 5.9261 — within 0.9% of target, and the sprites land essentially exactly.

`scroll_paper` was 0.9% off (460x558 instead of 460x560, i.e. 2px) so I deliberately
left its guide alone rather than invalidate the round-1 vs round-2 comparison you
already reviewed. Say the word if you want it regenerated for those last 2px.

## What I cannot tell you

I cannot see the images. Only you can judge:

- which paper texture, brushwork, line quality and colour you prefer per asset
- whether the rod's wood grain and end caps read correctly
- whether the medallion's disc and tassel look right at 120x240
- whether the cloud reads as a ruyi cloud
- whether the title plate is clean enough behind the Thai title text
- whether `cloud_corner_seed131` / `scroll_rod_seed111` are acceptable despite their size

## What I need from you

A list of five picks, one per asset, in this shape:

```
scroll_paper -> 303
scroll_rod -> 222
close_medallion -> 454
cloud_corner -> 353
title_plate -> 252
```

Once I have that I will: regenerate the chosen scroll_paper with its 40-step pass only if
you ask, finalise each spriteBorder from the chosen seed, then move to Phase 5 — copy into
`Assets/Resources/ui/disciple_list/`, add the idempotent importer, add the
`import_disciple_list_art` remote command, wire `DiscipleListPanelGenerator` with a
colour fallback, regenerate the prefab, and verify the import settings by reading them
back. I will not claim the result "looks good" — I will report sizes, import settings and
console output, and you judge the Game View.

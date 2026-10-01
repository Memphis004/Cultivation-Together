# CHECKPOINT A — scroll_paper smoke test

Status: **waiting for you to pick a seed.** Nothing is fixed yet, Unity is untouched.

## What to look at

| file | what it is |
|---|---|
| `art/disciple_list/contact/scroll_paper.png` | **2x2 contact sheet, checkerboard, seed labels — look at this one** |
| `art/disciple_list/final/scroll_paper_seed101.png` | seed 101, 460x552 RGBA |
| `art/disciple_list/final/scroll_paper_seed202.png` | seed 202, 460x548 RGBA |
| `art/disciple_list/final/scroll_paper_seed303.png` | seed 303, 460x560 RGBA |
| `art/disciple_list/final/scroll_paper_seed404.png` | seed 404, 460x549 RGBA |
| `art/disciple_list/raw/scroll_paper/seed*.png` | the untouched 928x1120 model output, before keying |

## Verified numerically (these are measured, not opinions)

Every number below comes from reading the PNGs on disk.

### 1. Did the background survive as magenta? YES

| seed | near-magenta share of raw | subject colour (median RGB) |
|---|---|---|
| 101 | 9.4% | 248, 236, 207 |
| 202 | 8.7% | 245, 234, 205 |
| 303 | 7.5% | 248, 235, 201 |
| 404 | 8.5% | 247, 233, 201 |

The guide was 7.7% magenta, so the model kept the key colour. The subject colour is
parchment cream — the intended palette. **No seed produced a non-magenta background.**

### 2. Final sprite files meet the acceptance criteria

| seed | mode | size | transparent | opaque | semi | near-magenta | upscaled? |
|---|---|---|---|---|---|---|---|
| 101 | RGBA | 460x552 | 4.54% | 92.90% | 2.56% | **0 px** | no |
| 202 | RGBA | 460x548 | 4.77% | 92.61% | 2.63% | **0 px** | no |
| 303 | RGBA | 460x560 | 3.41% | 94.11% | 2.48% | **0 px** | no |
| 404 | RGBA | 460x549 | 3.35% | 94.07% | 2.58% | **0 px** | no |

- all RGBA with a real alpha channel
- **zero** pixels within the chroma-key threshold of `#FF00FF`, total and visible
- all downscaled from 928x1120 — nothing was upscaled
- semi-transparent edge pixels are 2.5-2.6%, comparable to the reference asset's 4.3%,
  so the edges are anti-aliased rather than hard-cut

### 3. Did the silhouette survive? YES for the outline, NO for the spine width

| | guide | 101 | 202 | 303 | 404 |
|---|---|---|---|---|---|
| object aspect (w/h) | 0.8290 | 0.8333 | 0.8394 | 0.8214 | 0.8379 |
| difference | — | +0.004 | +0.010 | -0.008 | +0.009 |
| left spine bleed % width | **2.82%** | 10.00% | 12.61% | 11.30% | 9.35% |
| right spine bleed % width | **3.00%** | 9.57% | 12.39% | 8.91% | 10.00% |

**Overall outline: excellent.** The object aspect is within 0.01 of the guide on every
seed — the model honoured the page proportions and the frame-filling layout.

**Rolled spine: the model widened it ~3.5-4x.** The guide asked for a thin 2.8%/3.0%
muted-grey roll; every seed produced a 9-13% roll. The spine is also genuinely darker
than the paper (luminance ~168-189 vs paper ~232-237), so it reads as a distinct
rolled edge — it is just much thicker than the reference asset's.

This is the one real deviation. It is a **proportion** deviation, not a broken
silhouette, and all four seeds deviate in the same direction, which means it is the
prompt/guide asking for less than the model wants to give rather than seed noise.

### 4. Derived 9-slice border (from the median of the 4 seeds, at 460 wide)

```
spriteBorder = (left=49, bottom=0, right=45, top=0)     horizontal slice only
```

This is provisional. It will be computed from the ONE seed you pick, since the spine
width varies from 43px to 58px across seeds.

## Costs and reproducibility

- 4 jobs, 25 steps, resolution budget 1024, cfg 1, euler/simple, denoise 1
- `control_after_generate = fixed` on every job; each seed is explicit
- actual output canvas: **928x1120** on all four (predicted 928x1120 — the /32 snap landed exactly)
- times: 58.7s (first job, includes the ~15.8 GB weight load), then 31.4 / 31.3 / 32.4s
- full prompt + seed + steps for each job is recorded in `art/disciple_list/run_log.json`
- re-running any seed reproduces it byte-for-byte: `python tools/art/run_job.py scroll_paper --seeds 101`

## What I cannot tell you

I cannot see the images. These are all fine, but **only you can judge them**:

- whether the paper looks like paper (texture, mottling, fibre)
- whether the hand-inked outline reads as ink rather than a drawn border
- whether the roll looks like rolled parchment or like a flat grey stripe
- which seed's brushwork and colour you prefer
- whether the thick spine (finding 3 above) is acceptable, given the reference is thin

## What I need from you

Answer with a **seed number (101 / 202 / 303 / 404)**, optionally with a tweak.

If you want a change, the two available levers are:

- **(a) prompt** — e.g. state the spine width explicitly ("the rolled edge is a thin
  band occupying about one thirty-fifth of the total width") and re-run. Cheap,
  keeps the guide as is.
- **(b) guide** — e.g. make the guide's spine bands *thinner* than the reference
  measurement, to compensate for a known thickening bias, and re-run.

If all four are wrong I will try at most one more round (2 rounds total, per the task
rules) before stopping to reconsider the approach with you.

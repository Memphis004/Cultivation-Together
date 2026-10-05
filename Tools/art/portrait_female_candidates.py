"""Approved female portrait continuation: six candidates, then CHECKPOINT A.

Uses the Portrait v2 API graphs unchanged except character brief, seed and
output prefix and the user-requested style reference for round 2.
Never selects, postprocesses or imports a portrait.
"""
import argparse
import hashlib
import json
import re
import subprocess
import sys
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
ART = ROOT / 'art/portraits_v2/female'
LOG = ART / 'generation_log.json'
SEEDS = {'d001': [440101, 440102, 440103], 'd002': [440201, 440202, 440203]}
# Round 5: d001 only - one new outfit + three hairstyle variants, each with its
# own image_3 from art/reference (user: agent picks the files).
ROUND5_VARIANTS = [
    ('outfit', 480101, 'female_hair_outfit_ref.jpg',
     'Design a BRAND-NEW outfit distinct from every previous attempt: an elegant layered silk hanfu with delicate shoulder cut-outs revealing bare shoulders, a deep V crossover bodice cinched by a wide ornamental waist band, sheer-free opaque flowing sleeves and a long high-slit skirt, subtle light-blue embroidery accents. Keep the SAME facial beauty and body proportions described above.'),
    ('hair1', 480102, 'female_hair_ref1.jpg',
     'Hairstyle variant A from image 3: an elaborate high loose bun with soft face-framing tendrils and a slender jade hairpin, elegant and youthful. Keep the SAME facial beauty and a simple pale hanfu so the hairstyle is the focus.'),
    ('hair2', 480103, 'female_hair_ref2.jpg',
     'Hairstyle variant B from image 3: long flowing half-up hair with a small top knot, cascading waves over the shoulders and a delicate ribbon tie. Keep the SAME facial beauty and a simple pale hanfu so the hairstyle is the focus.'),
    ('hair3', 480104, 'female_hair_ref3.jpg',
     'Hairstyle variant C from image 3: a side-swept low ponytail with braided strands and small pearl ornaments, graceful and mature. Keep the SAME facial beauty and a simple pale hanfu so the hairstyle is the focus.'),
]
# Round 6: user rejected braids entirely (loose flowing hair ONLY) and rejected
# the round-5 bare-shoulder outfit (fully new outfit, covered shoulders).
ROUND6_VARIANTS = [
    ('outfit', 490101, 'r6_outfit_ref.jpg',
     'Design a BRAND-NEW outfit completely different from any previous attempt AND different from the reference outfit: a fully covered-shoulder elegant hanfu jacket with a high mandarin collar, wide bishop sleeves, a fitted brocade waist sash and a flowing ankle-length skirt with subtle light-blue piping - refined, figure-flattering, no bare shoulders, no cut-outs, no deep V neckline. Keep the SAME facial beauty and body proportions described above.'),
    ('hair1', 490102, 'r6_hair_ref1.jpg',
     'Hairstyle variant A from image 3: long LOOSE flowing hair worn completely down, glossy waves falling freely over the shoulders and down the back, one small delicate hairpin accent. ABSOLUTELY NO BRAID, no plait, no cornrow, no woven strands. Keep the SAME facial beauty and the simple pale hanfu so the loose hair is the focus.'),
    ('hair2', 490103, 'r6_hair_ref2.jpg',
     'Hairstyle variant B from image 3: long loose straight hair with a soft centre part and side-swept face-framing locks, silk-like and free-flowing. ABSOLUTELY NO BRAID, no ponytail, no bun, no knot. Keep the SAME facial beauty and the simple pale hanfu so the loose hair is the focus.'),
    ('hair3', 490104, 'r6_hair_ref3.jpg',
     'Hairstyle variant C from image 3: long loose voluminous waves swept gently to one side, flowing freely past the shoulders with soft movement. ABSOLUTELY NO BRAID, no bun, no ponytail. Keep the SAME facial beauty and the simple pale hanfu so the loose hair is the focus.'),
]
# Round 7: root-cause fix for the braids that survived rounds 5 and 6.
# The baseline brief said "black hair in two clearly visible braids" while the
# variant text forbade braids. Qwen-Image-Edit obeys the affirmative, specific,
# countable instruction ("two clearly visible") and `negative_prompt` is inert at
# cfg 1, so a prohibition can never beat an affirmative clause in the same
# prompt. The affirmative hair clause must therefore be DELETED from the shared
# subject brief, never merely contradicted.
SUBJECT_BRIEF_HBAN_TAILS = (
    # The brief ends with a hair ban. It is a prohibition, not a cause of braids,
    # but keeping it would leave the word "topknot"/"braids" in the brief and make
    # the invariant unverifiable. The variant text owns the hairstyle instead.
    ('no sword, no gourd, no male features, no topknot', 'No sword, no gourd, no male features'),
    ('no sword, no male features, no twin braids or high topknot', 'No sword, no male features'),
    ('no sword, no gourd, no male features, no twin braids or high topknot', 'No sword, no gourd, no male features'),
)
SUBJECT_BRIEF_START = 'Draw this new subject:'
SUBJECT_BRIEF_END = 'Exactly one SMALL accent color:'
AFFIRMATIVE_HAIR_CLAUSES = (
    'black hair in two clearly visible braids',
    'black hair gathered into a low bun',
    'black hair in two braids',
    'black hair braided into two plaits',
)
FORBIDDEN_HAIR_RE = re.compile(r'braid|plait|cornrow|ponytail|topknot|braided|\bbun\b', re.I)
# Round 7: 4 images (d001 x3, d002 x1) on the look the user picked from
# female_round6/raw/d001/490103 (ff056ab1_000.png) with the braid removed.
# The brief already says only "long black hair" after the strip above, so every
# hairstyle here is stated purely AFFIRMATIVELY - describing the wanted hair is
# what works, listing the unwanted hair is what has failed six rounds running.
ROUND7_REFERENCE_IMAGE = 'r7_ref.jpg'
ROUND7_REFERENCE_SOURCE = 'art/reference/female character/6f05229589aa17eb7094c09b0212068a.jpg'
# The user chose this single reference for the outfit AND the hairstyle, so the
# subject brief must stop prescribing garments too - a brief that fights image 3
# is the same class of bug as the braid clause.
OUTFIT_CLAUSES = (
    'an elegant modest pale white female hanfu, tailored overlapping crossed collar and fitted opaque inner bodice, a defined high waist tied with a plain dark sash, softly layered skirt and long wide flowing sleeves, restrained grey ink folds, relaxed hands held together near the waist',
    'an elegant white and grey female alchemist hanfu, tailored overlapping crossed collar and fitted opaque inner bodice, a defined high waist tied with a black sash, softly layered skirt and long wide flowing sleeves, holding one SMALL plain gourd near the waist',
)
ROUND7_VARIANTS = [
    ('d001', 'hair1', 500101,
     'Her hair hangs completely loose and straight down, falling freely over both shoulders and down her back in long smooth silk-like strands, parted softly in the centre with the front hair swept back away from the face; the hair is loose everywhere and every single strand hangs free. She wears the outfit of image 3 exactly as shown. Keep the SAME face and the SAME body proportions as the approved portrait.'),
    ('d001', 'hair2', 500102,
     'Her hair hangs completely loose and wavy down, falling freely over both shoulders and down her back in soft glossy waves, parted softly in the centre; the hair is loose everywhere and every single strand hangs free. She wears the outfit of image 3 exactly as shown. Keep the SAME face and the SAME body proportions as the approved portrait.'),
    ('d001', 'hair3', 500103,
     'Her hair hangs completely loose and voluminous down, a soft curtain of black hair falling well past the shoulders onto the chest and back, a light centre part, natural soft movement; the hair is loose everywhere and every single strand hangs free. She wears the outfit of image 3 exactly as shown. Keep the SAME face and the SAME body proportions as the approved portrait.'),
    ('d002', 'hair1', 500201,
     'Her hair hangs completely loose and straight down, falling freely over both shoulders and down her back in long smooth strands, parted softly in the centre; the hair is loose everywhere and every single strand hangs free. She wears the outfit of image 3 exactly as shown. Keep the SAME calm composed adult face and the SAME body proportions.'),
]


def strip_affirmative_hair_clauses(prompt):
    """Leave the shared subject brief with no hairstyle or garment word of any kind."""
    for clause in AFFIRMATIVE_HAIR_CLAUSES:
        prompt = prompt.replace(clause + ',', 'long black hair,')
        prompt = prompt.replace(clause, 'long black hair')
    stripped_outfits = 0
    for clause in OUTFIT_CLAUSES:
        if clause in prompt:
            stripped_outfits += 1
            prompt = prompt.replace(clause, 'wearing the outfit of image 3, fully opaque and fully covering')
    if stripped_outfits != 1:
        raise RuntimeError(
            f'Expected exactly one garment clause in the brief, stripped {stripped_outfits}; '
            'refusing to submit a prompt whose garments the strip cannot account for.')
    for needle, replacement in SUBJECT_BRIEF_HBAN_TAILS:
        prompt = re.sub(re.escape(needle), replacement, prompt, flags=re.IGNORECASE)
    return prompt


def assert_subject_brief_has_no_forbidden_hair(prompt):
    """Fail loudly if the subject brief still dictates a banned hairstyle.

    Only the shared subject brief is checked; the per-variant hairstyle text
    lives before it and is deliberately allowed to describe hair.
    """
    start = prompt.find(SUBJECT_BRIEF_START)
    end = prompt.find(SUBJECT_BRIEF_END)
    if start < 0 or end < 0 or end < start:
        raise RuntimeError('Subject brief markers not found; refusing to submit')
    brief = prompt[start:end]
    found = sorted({m.group(0) for m in FORBIDDEN_HAIR_RE.finditer(brief)})
    if found:
        raise RuntimeError(
            f'Subject brief still dictates a banned hairstyle ({found}); '
            'a prohibition in the variant text cannot beat an affirmative clause '
            'in the subject brief. Add the clause to AFFIRMATIVE_HAIR_CLAUSES.')


def cli(*args):
    proc = subprocess.run(['comfy', *args], capture_output=True, text=True,
                          encoding='utf-8', errors='replace')
    print(proc.stdout, flush=True)
    if proc.returncode:
        raise RuntimeError(f'comfy {args}: {proc.stdout}\n{proc.stderr}')
    payload = json.loads(proc.stdout)
    if payload.get('type') == 'envelope':
        if not payload.get('ok'):
            raise RuntimeError(payload)
        return payload['data']
    return payload


def save(log):
    temporary = LOG.with_suffix('.tmp')
    temporary.write_text(json.dumps(log, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    temporary.replace(LOG)


def prepare(round_number=1, preview=False):
    ART.mkdir(parents=True, exist_ok=True)
    if LOG.exists():
        return json.loads(LOG.read_text(encoding='utf-8'))
    jobs = []
    for disciple, seeds in SEEDS.items():
        baseline = ROOT / f'art/portraits_v2/workflows/{disciple}_seed{430101 if disciple == "d001" else 430202}.json'
        original = json.loads(baseline.read_text(encoding='utf-8'))
        for seed in seeds:
            graph = json.loads(json.dumps(original))
            prompt = graph['6']['inputs']['prompt']
            prompt = prompt.replace('No sword, no gourd, no male features, no topknot.',
                'Clearly feminine facial features and adult female anatomy, not an androgynous or masculine face. Long flowing sleeves in the same ink medium as the style anchor. No sword, no gourd, no male features, no topknot.')
            prompt = prompt.replace('No sword, no male features, no twin braids or high topknot.',
                'Clearly feminine facial features and adult female anatomy, not an androgynous or masculine face. Long flowing sleeves in the same ink medium as the style anchor. No sword, no male features, no twin braids or high topknot.')
            if round_number >= 2:
                prompt = prompt.replace(
                    'Image 3 supplies additional ink-wash painting-style reference ONLY.',
                    'Image 3 is the user-requested female hanfu reference: use its clearly feminine adult silhouette, shaped bodice, layered skirt and expressive flowing cloth as design guidance ONLY. Keep the ink painting medium of image 2. Do not reproduce its white hair, weapon, pose, exact costume, background or UI.')
                prompt = prompt.replace('a young adult Chinese female outer disciple',
                                        'a clearly adult Chinese woman aged 22, an outer disciple')
                prompt = prompt.replace('an adult Chinese female inner disciple and calm alchemist',
                                        'a clearly adult Chinese woman aged 28, an inner disciple and calm alchemist')
                prompt = prompt.replace(
                    'simple pale white robe with restrained grey folds and a plain dark waist sash',
                    'an elegant modest pale white female hanfu, tailored overlapping crossed collar and fitted opaque inner bodice, a defined high waist tied with a plain dark sash, softly layered skirt and long wide flowing sleeves, restrained grey ink folds')
                prompt = prompt.replace(
                    'simple white and grey alchemist robe with a black sash',
                    'an elegant white and grey female alchemist hanfu, tailored overlapping crossed collar and fitted opaque inner bodice, a defined high waist tied with a black sash, softly layered skirt and long wide flowing sleeves')
                prompt = prompt.replace(
                    'Clearly feminine facial features and adult female anatomy, not an androgynous or masculine face.',
                    'Clearly feminine adult face and visibly womanly proportions: a naturally full bust with distinct rounded breast volume readable beneath the fully opaque fitted hanfu bodice, a defined waist and softly curved hips. Cloth folds follow the bust rather than hiding it beneath a straight rectangular unisex robe. Tasteful fully clothed adult character design, no nudity or transparent fabric. Not a flat-chested, masculine or androgynous torso.')
                graph['11']['inputs']['image'] = f'female_character_{disciple}.jpg'
                prompt = prompt.replace('Image 3 supplies additional ink-wash painting-style reference ONLY.',
                                        'Image 3 is the user-selected female character reference.')
            if round_number == 3:
                prompt = prompt.replace(
                    'Image 2 is the user-selected STYLE ANCHOR: match its black ink brush outlines, monochrome dry-brush cloth texture and softly shaded semi-realistic manga painting medium ONLY.',
                    'Image 2 is a MALE ink-technique anchor ONLY: borrow black brush outlines and dry-brush shading, NEVER its face, jaw, shoulders, body proportions or male robe design. Image 3 is the PRIMARY guidance for feminine face design and elegant female hanfu silhouette, not an exact character to copy.')
                prompt = prompt.replace(
                    'Clearly feminine adult face and visibly womanly proportions:',
                    'An original distinctly feminine adult face: soft oval face, gently tapered rounded chin, delicate narrow jaw, slim softly arched eyebrows, gentle almond eyes, small refined nose and softly shaped full lips. Smooth subtle face shading; avoid heavy angular jaw planes or harsh facial shadows. Slender neck and softly sloping shoulders. Visibly womanly proportions:')
                if disciple == 'd001':
                    prompt = prompt.replace(
                        'an elegant modest pale white female hanfu, tailored overlapping crossed collar and fitted opaque inner bodice, a defined high waist tied with a plain dark sash, softly layered skirt and long wide flowing sleeves, restrained grey ink folds',
                        'a NEW elegant pale white RUQUN female hanfu design: a short tailored cross-collar ru blouse fitted over the bust, a high-waisted softly pleated qun skirt, a narrow light-blue waist ribbon and delicate flowing long sleeves. An opaque fitted inner layer under a lightweight flowing outer sleeve layer, restrained grey ink folds; no broad black mens waist sash and no straight unisex scholar robe')
                else:
                    prompt = prompt.replace(
                        'an elegant white and grey female alchemist hanfu, tailored overlapping crossed collar and fitted opaque inner bodice, a defined high waist tied with a black sash, softly layered skirt and long wide flowing sleeves',
                        'a NEW elegant layered female hanfu for an alchemist: a fitted opaque white inner bodice with a softly curved neckline, a light grey open-front long-sleeved outer jacket, a narrow vermilion waist tie, a flowing layered skirt and graceful wide draping sleeves. Soft waist shaping and delicate layered fabric; no broad black mens waist sash and no straight unisex scholar robe')
            if round_number in (4, 5, 6):
                prompt = prompt.replace(
                    'Image 3 is the user-requested female hanfu reference: use its clearly feminine adult silhouette, shaped bodice, layered skirt and expressive flowing cloth as design guidance ONLY. Keep the ink painting medium of image 2. Do not reproduce its white hair, weapon, pose, exact costume, background or UI.',
                    'Image 3 is the user-selected female character reference for FACE, HAIR and OUTFIT direction: capture its SWEET youthful feminine beauty - soft oval face, large gentle almond eyes, delicate small nose, rosy full lips, smooth lightly blushed cheeks, slender graceful neck - but compose an original character. Design hairstyle, ornament and outfit together as ONE coordinated look sharing the same colour story and period styling; never mix unrelated hair and costume. Give a tasteful SEXY silhouette: elegant form-fitting hanfu with a cinched waist, graceful bust contour under fully opaque fabric, and a high thigh-revealing side slit over the long flowing skirt, confident alluring pose, still fully stylized fantasy clothing, no nudity and no transparent fabric. Keep the ink painting medium of image 2 and do not copy image 3 pixels, background or composition.')
            if round_number == 5:
                variant = next(v for v in ROUND5_VARIANTS if v[1] == seed)
                _kind, _seed, ref_image, variant_text = variant
                graph['11']['inputs']['image'] = ref_image
                prompt = prompt.replace(
                    'Image 3 is the user-selected female character reference for FACE, HAIR and OUTFIT direction: capture its SWEET youthful feminine beauty - soft oval face, large gentle almond eyes, delicate small nose, rosy full lips, smooth lightly blushed cheeks, slender graceful neck - but compose an original character. Design hairstyle, ornament and outfit together as ONE coordinated look sharing the same colour story and period styling; never mix unrelated hair and costume. Give a tasteful SEXY silhouette: elegant form-fitting hanfu with a cinched waist, graceful bust contour under fully opaque fabric, and a high thigh-revealing side slit over the long flowing skirt, confident alluring pose, still fully stylized fantasy clothing, no nudity and no transparent fabric. Keep the ink painting medium of image 2 and do not copy image 3 pixels, background or composition.',
                    'Image 3 is the user-selected reference for this variant. Preserve the SWEET youthful feminine beauty already approved - soft oval face, large gentle almond eyes, delicate small nose, rosy full lips, smooth lightly blushed cheeks, slender graceful neck, womanly bust and curved silhouette - and keep it IDENTICAL across all variants of this round except for the hair or outfit being varied. ' + variant_text + ' Keep the ink painting medium of image 2. Tasteful fully clothed fantasy clothing, no nudity, no transparent fabric. Do not copy image 3 pixels, background or composition; only one original character on the magenta canvas.')
            if round_number == 6:
                variant = next(v for v in ROUND6_VARIANTS if v[1] == seed)
                _kind, _seed, ref_image, variant_text = variant
                graph['11']['inputs']['image'] = ref_image
                prompt = prompt.replace(
                    'Image 3 is the user-selected female character reference for FACE, HAIR and OUTFIT direction: capture its SWEET youthful feminine beauty - soft oval face, large gentle almond eyes, delicate small nose, rosy full lips, smooth lightly blushed cheeks, slender graceful neck - but compose an original character. Design hairstyle, ornament and outfit together as ONE coordinated look sharing the same colour story and period styling; never mix unrelated hair and costume. Give a tasteful SEXY silhouette: elegant form-fitting hanfu with a cinched waist, graceful bust contour under fully opaque fabric, and a high thigh-revealing side slit over the long flowing skirt, confident alluring pose, still fully stylized fantasy clothing, no nudity and no transparent fabric. Keep the ink painting medium of image 2 and do not copy image 3 pixels, background or composition.',
                    'Image 3 is the user-selected reference for this variant. Preserve the SWEET youthful feminine beauty already approved - soft oval face, large gentle almond eyes, delicate small nose, rosy full lips, smooth lightly blushed cheeks, slender graceful neck, womanly bust and curved silhouette - and keep it IDENTICAL across all variants of this round except for the hair or outfit being varied. ' + variant_text + ' HAIR RULE FOR EVERY IMAGE IN THIS ROUND: hair must be long, loose and flowing freely down; any braid, plait, woven strand, ponytail or bun is strictly forbidden. Keep the ink painting medium of image 2. Tasteful fully clothed fantasy clothing, no nudity, no transparent fabric. Do not copy image 3 pixels, background or composition; only one original character on the magenta canvas.')
            if round_number == 7:
                variant = next(v for v in ROUND7_VARIANTS if v[2] == seed)
                _disciple, _kind, _seed, variant_text = variant
                graph['11']['inputs']['image'] = ROUND7_REFERENCE_IMAGE
                prompt = prompt.replace(
                    'Image 3 is the user-selected female character reference for FACE, HAIR and OUTFIT direction: capture its SWEET youthful feminine beauty - soft oval face, large gentle almond eyes, delicate small nose, rosy full lips, smooth lightly blushed cheeks, slender graceful neck - but compose an original character. Design hairstyle, ornament and outfit together as ONE coordinated look sharing the same colour story and period styling; never mix unrelated hair and costume. Give a tasteful SEXY silhouette: elegant form-fitting hanfu with a cinched waist, graceful bust contour under fully opaque fabric, and a high thigh-revealing side slit over the long flowing skirt, confident alluring pose, still fully stylized fantasy clothing, no nudity and no transparent fabric. Keep the ink painting medium of image 2 and do not copy image 3 pixels, background or composition.',
                    'Image 3 is the user-selected reference for this variant. Preserve the SWEET youthful feminine beauty already approved - soft oval face, large gentle almond eyes, delicate small nose, rosy full lips, smooth lightly blushed cheeks, slender graceful neck, womanly bust and curved silhouette - and keep it IDENTICAL across all variants of this round except for the hair or outfit being varied. HAIRSTYLE AND COSTUME OF THIS IMAGE ARE DECIDED ONLY BY THE SENTENCE BELOW AND BY IMAGE 3, NEVER BY THE SUBJECT BRIEF: ' + variant_text + ' Keep the ink painting medium of image 2. Tasteful fully clothed fantasy clothing, no nudity, no transparent fabric. Do not copy image 3 pixels, background or composition; only one original character on the magenta canvas.')
            if round_number >= 7:
                prompt = strip_affirmative_hair_clauses(prompt)
                assert_subject_brief_has_no_forbidden_hair(prompt)
            graph['6']['inputs']['prompt'] = prompt
            graph['8']['inputs']['seed'] = seed
            graph['10']['inputs']['filename_prefix'] = f'portrait_female_r{round_number}_{disciple}_seed{seed}'
            path = ART / f'workflows/{disciple}_seed{seed}.json'
            if not preview:
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(json.dumps(graph, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
            jobs.append({'disciple_id': disciple, 'seed': seed,
                         'workflow': path.relative_to(ROOT).as_posix(),
                         'prompt': prompt, 'status': 'prepared'})
    log = {'checkpoint_0': 'approved by user in current session',
           'checkpoint_a': 'not generated yet',
           'round': round_number, 'quota_max': 12 if round_number >= 2 else 8,
           'previous_round_images': 24 if round_number == 7 else (20 if round_number == 6 else (16 if round_number == 5 else (14 if round_number == 4 else (10 if round_number == 3 else (6 if round_number == 2 else 0))))),
           'quota_authorization': 'user approved per CHECKPOINT; see quota_audit' if round_number == 7 else ('user approved 4 images: 1 brand-new outfit + 3 loose-hair variants (no braids)' if round_number == 6 else ('user approved 4 images: 1 new outfit + 3 hairstyle variants (actual total tracked separately)' if round_number == 5 else ('user requested a fresh sexy redesign; actual quota tracked separately' if round_number == 4 else ('user approved six new images and expanded quota' if round_number == 2 else 'original quota')))),
           'generation_requests_submitted': 0, 'images_completed': 0,
           'execution': 'local 127.0.0.1:8188, sequential, no paid nodes',
           'anchor': {'seed': 420103, 'file': 'art/portraits_v2/anchor/1cf13e0c_000.png'},
           'style_reference': ROUND7_REFERENCE_SOURCE if round_number == 7 else ('art/reference/r6_*.jpg (copies of selected files from art/reference/female character/)' if round_number == 6 else ('art/reference/female_hair_outfit_ref.jpg + female_hair_ref1..3.jpg (copied from art/reference/female character/)' if round_number == 5 else (f'art/reference/female character/ (female_character_{"d001|d002"}.jpg copies)' if round_number == 4 else f'art/reference/portrait_style_ref-{3 if round_number >= 2 else 2}.png'))),
           'jobs': jobs}
    if preview:
        return log
    save(log)
    return log


def run(log):
    for job in log['jobs']:
        if (ART / 'pause_requested').exists():
            print('PAUSED: user requested a revised character design; no further jobs submitted.', flush=True)
            return
        if job['status'] == 'completed':
            continue
        workflow = ROOT / job['workflow']
        if not job.get('prompt_id'):
            verdict = cli('workflow', 'validate', '--workflow', str(workflow))
            if not verdict.get('valid') or verdict.get('spends_credits'):
                raise RuntimeError(f'Workflow not cleared for free local execution: {verdict}')
            # Persist intent before submit: never automatically resubmit an ambiguous request.
            if job['status'] == 'submitting':
                raise RuntimeError('Previous submit interrupted: reconcile queue before retrying')
            job['status'] = 'submitting'
            save(log)
            submitted = cli('run', '--workflow', str(workflow), '--where', 'local',
                            '--host', '127.0.0.1', '--port', '8188', '--no-notify', '--no-watch')
            job['prompt_id'] = submitted['prompt_id']
            job['status'] = 'submitted'
            log['generation_requests_submitted'] += 1
            save(log)
        pid = job['prompt_id']
        cli('jobs', 'wait', pid, '--host', '127.0.0.1', '--port', '8188',
            '--timeout', '3600', '--poll-interval', '15')
        status = cli('jobs', 'status', pid, '--host', '127.0.0.1', '--port', '8188')
        if status.get('status') != 'completed' or status.get('execution_error') or status.get('error'):
            raise RuntimeError(f'Job failed or incomplete: {status}')
        folder = ART / f"raw/{job['disciple_id']}/{job['seed']}"
        download = cli('download', pid, '--where', 'local', '-o', str(folder))
        files = download.get('files', [])
        if len(files) != 1:
            raise RuntimeError(f'Expected exactly one output: {download}')
        path = Path(files[0]['path'])
        with Image.open(path) as image:
            if image.size != (1024, 1536) or image.mode != 'RGBA':
                raise ValueError(f'Unexpected output {path}: {image.size} {image.mode}')
            rgb = np.asarray(image.convert('RGB')).astype(np.int16)
            distance = np.max(np.abs(rgb - [255, 0, 255]), axis=2)
            border = np.concatenate([distance[:32].ravel(), distance[-32:].ravel(),
                                     distance[:, :32].ravel(), distance[:, -32:].ravel()])
            job['metrics'] = {'size': list(image.size), 'mode': image.mode,
                              'bytes': path.stat().st_size,
                              'sha256': hashlib.sha256(path.read_bytes()).hexdigest(),
                              'near_magenta_fraction_tolerance_20': float((distance <= 20).mean()),
                              'border_near_magenta_fraction_tolerance_20': float((border <= 20).mean())}
        job.update(status='completed', file=path.relative_to(ROOT).as_posix())
        log['images_completed'] = sum(j['status'] == 'completed' for j in log['jobs'])
        save(log)
        print(f"SAVED {job['disciple_id']} seed {job['seed']}; {log['images_completed']}/{len(log['jobs'])}", flush=True)
        make_sheets(log)
    log['checkpoint_a'] = 'awaiting user choice per disciple; no postprocess/import'
    save(log)


def make_sheets(log):
    for disciple in SEEDS:
        jobs = [j for j in log['jobs'] if j['disciple_id'] == disciple and j['status'] == 'completed']
        if not jobs:
            continue
        output = ART / f'review/{disciple}_contact_sheet.png'
        subprocess.run([sys.executable, str(ROOT / 'Tools/art/portrait_contact_sheet.py'),
                        '--output', str(output),
                        *[f"{disciple}:{j['seed']}={ROOT / j['file']}" for j in jobs]], check=True)
        with Image.open(output) as sheet:
            if sheet.size != (384 * len(jobs), 616):
                raise ValueError(f'Incorrect contact sheet dimensions: {sheet.size}')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--prepare-only', action='store_true')
    parser.add_argument('--preview', action='store_true',
                        help='print the round-7 prompts; write and submit nothing')
    parser.add_argument('--round', type=int, choices=(1, 2, 3, 4, 5, 6, 7), default=1)
    args = parser.parse_args()
    if args.round == 2:
        ART = ROOT / 'art/portraits_v2/female_round2'
        LOG = ART / 'generation_log.json'
        SEEDS = {'d001': [450101, 450102, 450103], 'd002': [450201, 450202, 450203]}
    if args.round == 3:
        ART = ROOT / 'art/portraits_v2/female_round3'
        LOG = ART / 'generation_log.json'
        SEEDS = {'d001': [460101], 'd002': [460201]}
    if args.round == 4:
        ART = ROOT / 'art/portraits_v2/female_round4'
        LOG = ART / 'generation_log.json'
        SEEDS = {'d001': [470101], 'd002': [470201]}
    if args.round == 5:
        ART = ROOT / 'art/portraits_v2/female_round5'
        LOG = ART / 'generation_log.json'
        SEEDS = {'d001': [v[1] for v in ROUND5_VARIANTS]}
    if args.round == 6:
        ART = ROOT / 'art/portraits_v2/female_round6'
        LOG = ART / 'generation_log.json'
        SEEDS = {'d001': [v[1] for v in ROUND6_VARIANTS]}
    if args.round == 7:
        if not ROUND7_REFERENCE_IMAGE:
            raise SystemExit('ROUND7_REFERENCE_IMAGE is empty: the user must choose '
                             'the single reference file before any round-7 prompt.')
        ART = ROOT / 'art/portraits_v2/female_round7'
        LOG = ART / 'generation_log.json'
        SEEDS = {}
        for disciple_id, _kind, variant_seed, _text in ROUND7_VARIANTS:
            SEEDS.setdefault(disciple_id, []).append(variant_seed)
    log = prepare(args.round, preview=args.preview)
    if args.preview:
        for job in log['jobs']:
            brief = job['prompt'][job['prompt'].find(SUBJECT_BRIEF_START):job['prompt'].find(SUBJECT_BRIEF_END)]
            print('=' * 78)
            print(f"{job['disciple_id']} seed {job['seed']}")
            print(f"SUBJECT BRIEF (guard-verified, {len(brief)} chars):")
            print('  ' + brief.strip())
            print('HAIR/OUTFIT SENTENCE (the only place they are decided):')
            variant_text = next(t for _d, _k, s, t in ROUND7_VARIANTS if s == job['seed'])
            print('  ' + variant_text)
        print('=' * 78)
        print(f'preview only: {len(log["jobs"])} prompts, nothing written, nothing submitted')
        raise SystemExit(0)
    if not args.prepare_only:
        run(log)

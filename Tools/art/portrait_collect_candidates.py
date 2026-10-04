"""Resume collection of existing Phase 3 jobs; never generate or select art."""
import json
import subprocess
import sys
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
ART = ROOT / 'art/portraits_v2'
LOG = ART / 'generation_log.json'


def cli(*args):
    result = subprocess.run(['comfy', *args], capture_output=True, text=True,
                            encoding='utf-8', errors='replace')
    if result.returncode:
        raise RuntimeError(f'comfy {args}: {result.stdout}\n{result.stderr}')
    print(result.stdout, flush=True)
    payload = json.loads(result.stdout)
    if payload.get('type') == 'envelope':
        if not payload.get('ok'):
            raise RuntimeError(f'Comfy CLI error: {payload}')
        return payload['data']
    return payload


def main():
    data = json.loads(LOG.read_text(encoding='utf-8'))
    jobs = [job for job in data['jobs'] if job['phase'] == 3]
    if len(jobs) != 6:
        raise ValueError('Expected exactly six submitted Phase 3 jobs')
    candidates = []
    metrics = []
    for job in jobs:
        pid = job['prompt_id']
        cli('jobs', 'wait', pid, '--host', '127.0.0.1', '--port', '8188',
            '--timeout', '3600', '--poll-interval', '15')
        status = cli('jobs', 'status', pid, '--host', '127.0.0.1', '--port', '8188')
        if status.get('status') != 'completed' or status.get('error'):
            raise RuntimeError(f'Job not successfully completed: {status}')
        folder = ART / 'phase3' / job['disciple_id'] / str(job['seed'])
        downloaded = cli('download', pid, '--where', 'local', '-o', str(folder))
        files = downloaded.get('files', [])
        if len(files) != 1:
            raise RuntimeError(f'Expected one image: {downloaded}')
        path = Path(files[0]['path'])
        image = Image.open(path)
        if image.size != (1024, 1536) or image.mode != 'RGBA':
            raise ValueError(f'{path}: expected 1024x1536 RGBA; got {image.size} {image.mode}')
        rgb = np.asarray(image.convert('RGB')).astype(np.int16)
        distance = np.max(np.abs(rgb - [255, 0, 255]), axis=2)
        border = np.concatenate([distance[:32].ravel(), distance[-32:].ravel(),
                                 distance[:, :32].ravel(), distance[:, -32:].ravel()])
        relative = path.relative_to(ART).as_posix()
        job.update(status='completed', file=relative)
        metrics.append(dict(disciple_id=job['disciple_id'], seed=job['seed'], file=relative,
                            size=list(image.size), mode=image.mode, bytes=path.stat().st_size,
                            near_magenta_fraction_tolerance_20=float((distance <= 20).mean()),
                            border_near_magenta_fraction_tolerance_20=float((border <= 20).mean())))
        candidates.append(f"{job['disciple_id']}:{job['seed']}={path}")
        data['images_completed'] = sum(j.get('status') == 'completed' for j in data['jobs'])
        LOG.write_text(json.dumps(data, indent=2), encoding='utf-8')
        print(f"COLLECTED {job['disciple_id']} seed {job['seed']}: {path}", flush=True)
    sheet = ART / 'phase3/contact_sheet.png'
    subprocess.run([sys.executable, str(ROOT / 'tools/art/portrait_contact_sheet.py'),
                    '--columns', '2', '--output', str(sheet), *candidates], check=True)
    if Image.open(sheet).size != (768, 1848):
        raise ValueError('Unexpected contact sheet dimensions')
    for index, disciple in enumerate(('d001', 'd002', 'd003')):
        subprocess.run([sys.executable, str(ROOT / 'tools/art/portrait_contact_sheet.py'),
                        '--output', str(ART / f'phase3/{disciple}/contact_sheet.png'),
                        *candidates[index * 2:index * 2 + 2]], check=True)
    (ART / 'phase3/metrics.json').write_text(json.dumps(metrics, indent=2), encoding='utf-8')
    data['checkpoint_b'] = 'awaiting user discipleId -> seed selection; not postprocessed'
    LOG.write_text(json.dumps(data, indent=2), encoding='utf-8')
    print('COMPLETE: six candidates verified; Checkpoint B awaits user choice.', flush=True)


if __name__ == '__main__':
    main()

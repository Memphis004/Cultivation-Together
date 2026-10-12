# Retro-measure table (run of Tools/art/layers_trial_measure.py, unchanged)

verdict: PASS = size 1024x1536 AND |dx|,|dy| <= 5 AND mismatch <= 0.02 (the brief target). Chin y and face width are shown but do not decide the verdict; chin is not reliably measured.

| รอบ | seed | size | drift dx,dy | chin y | ความกว้างหน้า out/base | ผมดำ out/base (head box) | mismatch | pass/fail | keyed path |
|---|---|---|---|---|---|---|---|---|---|
| phase0 | 540101 | 1024x1536 | 0,0 | วัดไม่ได้ (detector คืน 659 ทุกภาพ) | 138/139 | 0.297/0.296 | 0.0195 | PASS | `art/portraits_v2/layers_trial/layers/head_seed540101.png` |
| phase0 | 540102 | 1024x1536 | -1,0 | วัดไม่ได้ (detector คืน 659 ทุกภาพ) | 138/139 | 0.296/0.296 | 0.0941 | FAIL | `art/portraits_v2/layers_trial/layers/head_seed540102.png` |
| phase0 | 540103 | 1024x1536 | 0,0 | วัดไม่ได้ (detector คืน 659 ทุกภาพ) | 139/139 | 0.298/0.296 | 0.0057 | PASS | `art/portraits_v2/layers_trial/layers/head_seed540103.png` |
| v2 | 540101 | 1024x1536 | -36,13 | วัดไม่ได้ (detector คืน 659 ทุกภาพ) | 319/139 | 0.319/0.296 | 0.5206 | FAIL | `art/portraits_v2/layers_trial/round_headv2/layers/head_seed540101.png` |
| v2 | 540102 | 1024x1536 | -9,13 | วัดไม่ได้ (detector คืน 659 ทุกภาพ) | 319/139 | 0.467/0.296 | 0.4943 | FAIL | `art/portraits_v2/layers_trial/round_headv2/layers/head_seed540102.png` |
| v2 | 540103 | 1024x1536 | 17,34 | วัดไม่ได้ (detector คืน 659 ทุกภาพ) | 105/139 | 0.664/0.296 | 0.6998 | FAIL | `art/portraits_v2/layers_trial/round_headv2/layers/head_seed540103.png` |
| v3 | 540101 | 1024x1536 | 0,0 | วัดไม่ได้ (detector คืน None ทุกภาพ) | วัดไม่ได้ (script คืน None) | 0.294/0.296 | 0.5663 | FAIL | `art/portraits_v2/layers_trial/round_headv3/layers/head_seed540101.png` |
| v3 | 540102 | 1024x1536 | 0,0 | วัดไม่ได้ (detector คืน 647 ทุกภาพ) | วัดไม่ได้ (script คืน None) | 0.301/0.296 | 0.5733 | FAIL | `art/portraits_v2/layers_trial/round_headv3/layers/head_seed540102.png` |
| v3 | 540103 | 1024x1536 | 0,0 | วัดไม่ได้ (detector คืน None ทุกภาพ) | วัดไม่ได้ (script คืน None) | 0.297/0.296 | 0.5333 | FAIL | `art/portraits_v2/layers_trial/round_headv3/layers/head_seed540103.png` |
| v4 | 540101 | 1024x1536 | 0,0 | วัดไม่ได้ (detector คืน 659 ทุกภาพ) | 139/139 | 0.376/0.296 | 0.2430 | FAIL | `art/portraits_v2/layers_trial/round_headv4/layers/head_seed540101.png` |
| v4 | 540102 | 1024x1536 | 0,-1 | วัดไม่ได้ (detector คืน 659 ทุกภาพ) | 137/139 | 0.399/0.296 | 0.2000 | FAIL | `art/portraits_v2/layers_trial/round_headv4/layers/head_seed540102.png` |
| v4 | 540103 | 1024x1536 | 0,0 | วัดไม่ได้ (detector คืน 659 ทุกภาพ) | 140/139 | 0.353/0.296 | 0.1726 | FAIL | `art/portraits_v2/layers_trial/round_headv4/layers/head_seed540103.png` |

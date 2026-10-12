## base reference
{"file": "base_norm.png", "chin_y_base_detector": 611, "chin_y_norm_measurements_json": 607.87, "chin_base_diff_vs_measurements_px": 3.13, "chin_base_within_5px_of_measurements": true, "face_width_eye_base_detector": 139, "face_width_eye_norm_measurements_json": 129.15, "foreground_px_base": 518816, "below_chin_px_base": 473480, "dark_frac_head_box_base": 0.296}

| รอบ | seed | drift dx,dy | chin out/base (ต่าง) | ความกว้างหน้า out/base (Δ%) | foreground_ratio | below_chin_px | ผมดำ out/base | mismatch | Phase0 verdict |
|---|---|---|---|---|---|---|---|---|---|
| phase0 | 540101 | 0,0 | 611/611 (+0) | 138/139 (-0.72%) | 1.0008 | 473908 | 0.297/0.296 | 0.0195 | PASS |
| phase0 | 540102 | -1,0 | 612/611 (+1) | 138/139 (-0.72%) | 1.0036 | 475234 | 0.296/0.296 | 0.0941 | PASS |
| phase0 | 540103 | 0,0 | 611/611 (+0) | 139/139 (+0.00%) | 1.0053 | 476060 | 0.298/0.296 | 0.0057 | PASS |
| v2 | 540101 | -36,13 | None | 319/139 (+129.50%) | 1.0896 | None | 0.319/0.296 | 0.5206 | FAIL |
| v2 | 540102 | -9,13 | None | 319/139 (+129.50%) | 0.915 | None | 0.467/0.296 | 0.4943 | FAIL |
| v2 | 540103 | 17,34 | 607/611 (-4) | 105/139 (-24.46%) | 1.1105 | 314776 | 0.664/0.296 | 0.6998 | FAIL |
| v3 | 540101 | 0,0 | None | None | 0.1177 | None | 0.294/0.296 | 0.5663 | UNMEASURABLE |
| v3 | 540102 | 0,0 | None | None | 0.1098 | None | 0.301/0.296 | 0.5733 | UNMEASURABLE |
| v3 | 540103 | 0,0 | None | None | 0.1287 | None | 0.297/0.296 | 0.5333 | UNMEASURABLE |
| v4 | 540101 | 0,0 | 610/611 (-1) | 139/139 (+0.00%) | 0.1769 | 46575 | 0.376/0.296 | 0.2430 | PASS |
| v4 | 540102 | 0,-1 | 607/611 (-4) | 137/139 (-1.44%) | 0.1629 | 39990 | 0.399/0.296 | 0.2000 | PASS |
| v4 | 540103 | 0,0 | 611/611 (+0) | 140/139 (+0.72%) | 0.1645 | 40344 | 0.353/0.296 | 0.1726 | PASS |

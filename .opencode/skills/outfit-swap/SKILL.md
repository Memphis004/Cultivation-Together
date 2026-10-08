---
name: outfit-swap
description: Prepare and verify an outfit swap on an existing portrait with the ComfyUI template comfy/qwen_reference_test.json. Use when the user gives a BASE portrait path and an outfit REF image path.
---

## Inputs
Ask the user for BASE (portrait to edit) and REF (outfit reference). Never guess paths.
This model cannot see images; work with file paths and numbers only.

## Steps (report after each step)
1. Check both files exist; print size and mode with PIL.
2. Find the ComfyUI input dir from GET http://127.0.0.1:8188/system_stats
   (argv --input-directory; same logic as server_dirs() in Tools/art/run_job.py).
   Copy BASE to outfit_base_<stem>.png and REF to outfit_ref_<stem>.<ext> there.
   Never overwrite an existing file; add a suffix instead.
3. Run `python Tools/art/gpu_mode.py image`. If it fails, stop and report.
4. Tell the user to open comfy/qwen_reference_test.json, set the two LoadImage
   nodes to the copied files, paste the prompt from this skill, and run ONE image.
   Do not run the workflow yourself. Wait for the user to say done.
5. Find the newest file with prefix Qwen_image_2.1 in the ComfyUI output dir.
   Copy it to art/portraits_v2/outfit_swap/ (no overwrite). Report: size, mode,
   fraction of pixels within colour distance 60 of (255,0,255).
   Never judge whether the art looks good; the user decides.

## Prompt
Replace the costume of the character in image 1 with the costume shown in image 2.
Keep the face, hairstyle, body proportions, pose and the flat solid magenta #FF00FF
background of image 1 exactly unchanged. Render the new costume in the same black
ink-wash grayscale brush style as image 1, with white and grey cloth and deep black
accents. Take only the garments from image 2.
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Xianxia.Sect.Visual.Spikes.EditorTools
{
    /// <summary>
    /// Marker-file remote control for agent-driven Phase-0 runs, because this
    /// session cannot pass parameters through the eval/menu-execute tools.
    /// An EditorUpdate loop watches Library/visual_spike_command.txt for:
    ///   play_s2 | play_s1 | play_s3 | verify | fetch_csv
    /// and acts exactly like the corresponding menu item. After a play command,
    /// it auto-exits play mode once a new CSV/PNG artifact appears in
    /// persistentDataPath/visual_spikes and copies artifacts into Library/ for
    /// the agent to read. Gameplay scenes never contain spike components and
    /// nothing auto-loads — this only reacts to an explicit flag file (C2).
    /// </summary>
    [InitializeOnLoad]
    public static class VisualSpikeRemoteControl
    {
        private const string FlagPath = "Library/visual_spike_command.txt";
        private const string ResultPath = "Library/visual_spike_result.txt";
        private const string MenuRoot = "Xianxia/Visual Spikes/Remote/";

        private static bool _watching;
        private static DateTime? _playStartUtc;

        [MenuItem(MenuRoot + "Start Remote Control Watch")]
        public static void StartWatch()
        {
            _watching = true;
            WriteResult("watching");
        }

        static VisualSpikeRemoteControl()
        {
            _watching = true; // auto-start; the menu item is a manual fallback
            EditorApplication.update += Poll;
        }

        private static void Poll()
        {
            if (!_watching) return;

            // Auto-exit: a measurement artifact newer than play start means done.
            if (_playStartUtc.HasValue && EditorApplication.isPlaying)
            {
                if (HasNewArtifact(_playStartUtc.Value))
                {
                    _playStartUtc = null;
                    EditorApplication.isPlaying = false;
                    WriteResult("measured\n" + FetchCsvIndex());
                    return;
                }
            }

            if (!File.Exists(FlagPath)) return;

            string command;
            try
            {
                command = File.ReadAllText(FlagPath).Trim();
                File.Delete(FlagPath);
            }
            catch (IOException)
            {
                return; // agent still writing; retry next tick
            }

            switch (command)
            {
                case "play_s2":
                    VisualSpikeScenes.OpenS2Scene(); // creates the scene on first run
                    _playStartUtc = DateTime.UtcNow;
                    EditorApplication.isPlaying = true;
                    WriteResult("playing_s2");
                    break;
                case "play_s1":
                    VisualSpikeScenes.OpenS1Scene();
                    _playStartUtc = DateTime.UtcNow;
                    EditorApplication.isPlaying = true;
                    WriteResult("playing_s1");
                    break;
                case "play_s3":
                    VisualSpikeScenes.OpenS3Scene();
                    _playStartUtc = DateTime.UtcNow;
                    EditorApplication.isPlaying = true;
                    WriteResult("playing_s3");
                    break;
                case "verify":
                    VisualSpikesEditor.VerifyConstraints();
                    WriteResult("verified");
                    break;
                case "validate":
                    Xianxia.Sect.EditorTools.VisualCoverageValidator.Validate();
                    WriteResult("validated");
                    break;
                case "bake_chibi":
                    Xianxia.Sect.EditorTools.ChibiSheetBaker.GenerateAll();
                    WriteResult("baked_chibi");
                    break;
                case "bake_portrait":
                    Xianxia.Sect.EditorTools.PortraitPlaceholderBaker.GenerateAll(); // face split: portrait placeholder sheets into Assets/Resources/Avatar
                    WriteResult("baked_portrait");
                    break;
                case "phase2_verify":
                    Phase2VerifyRunner.RunVerify(); // async: report lands in Library/phase2_verify_report.txt
                    WriteResult("phase2_verify_started");
                    break;
                case "phase3_verify":
                    Phase3VerifyRunner.Run(); // async: report lands in Library/phase3_verify_report.txt
                    WriteResult("phase3_verify_started");
                    break;
                case "phase4_verify":
                    Phase4VerifyRunner.Run(); // async: report lands in Library/phase4_verify_report.txt
                    WriteResult("phase4_verify_started");
                    break;
                case "phase5_verify":
                    Phase5VerifyRunner.Run(); // async: report lands in Library/phase5_verify_report.txt
                    WriteResult("phase5_verify_started");
                    break;
                case "visual_overrides_verify":
                    VisualOverridesVerifyRunner.Run(); // async: report lands in Library/visual_overrides_verify_report.txt
                    WriteResult("visual_overrides_verify_started");
                    break;
                case "visual_overrides_shot":
                    VisualOverridesShotRunner.Run(); // async: PNGs land in Library/visual_overrides_shot*.png
                    WriteResult("visual_overrides_shot_started");
                    break;
                case "play_demo":
                    VisualDemoSceneTool.PlayDemo(); // guards + scaffold + boot scene + play; loader drives the additive demo load
                    WriteResult("play_demo_started");
                    break;
                case "demo_verify":
                    VisualDemoSceneTool.DemoVerify(); // guards + scaffold + boot + play + runner — report lands in Library/demo_verify_report.txt
                    WriteResult("demo_verify_started");
                    break;
                case "fetch_csv":
                    WriteResult(FetchCsvIndex());
                    break;
                case "demo_shot":
                    DemoShot();
                    break;
                case "demo_probe":
                    DemoProbe();
                    break;
                case "restore":
                    // Reopen the first gameplay scene from Build Settings after spikes
                    // (agent cannot pass parameters through open_scene in this session).
                    if (EditorBuildSettings.scenes.Length > 0)
                    {
                        EditorSceneManager.OpenScene(EditorBuildSettings.scenes[0].path, OpenSceneMode.Single);
                        WriteResult("restored:" + EditorBuildSettings.scenes[0].path);
                    }
                    else
                    {
                        WriteResult("no_build_scenes");
                    }
                    break;
                default:
                    WriteResult("unknown_command:" + command);
                    break;
            }
        }

        /// <summary>
        /// Ground-truth probe for the Visual Demo camera question (dev-only):
        /// 1. Dumps EVERY camera (incl. inactive) — name/depth/enabled/active/tag/ortho —
        ///    into Library/visual_spike_result.txt (the MCP capture tool cannot take a
        ///    "source" parameter in this session, so its image may show Camera.main =
        ///    the boot camera, NOT the composited backbuffer).
        /// 2. Captures the REAL composited backbuffer via ScreenCapture (what the Game
        ///    View actually shows) to Library/demo_shot.png for the agent to read.
        /// </summary>
        private static void DemoShot()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("playing=" + EditorApplication.isPlaying);
            sb.AppendLine("allCamerasCount=" + Camera.allCamerasCount);
            var all = UnityEngine.Object.FindObjectsByType<Camera>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                var c = all[i];
                if (c == null) continue;
                // URP UniversalAdditionalCameraData deliberately NOT read here:
                // the editor asmdef has no URP reference (keep it that way) —
                // standard Camera fields are sufficient for the diagnosis.
                sb.AppendLine("cam[" + i + "]='" + c.name + "'" +
                              " depth=" + c.depth +
                              " enabled=" + c.enabled +
                              " activeInHierarchy=" + c.gameObject.activeInHierarchy +
                              " tag=" + c.tag +
                              " ortho=" + c.orthographic + " size=" + c.orthographicSize +
                              " pos=" + c.transform.position +
                              " viewport=" + c.rect +
                              " targetDisplay=" + c.targetDisplay +
                              " cullingMask=" + c.cullingMask);
            }
            // Renderer ground truth: where ARE the chibis actually rendering (if at all)?
            var demoCam = Camera.allCameras;
            Camera cam10 = null;
            for (int i = 0; i < demoCam.Length; i++) if (demoCam[i].depth == 10f) cam10 = demoCam[i];
            var frustum = cam10 != null ? GeometryUtility.CalculateFrustumPlanes(cam10) : null;
            var sprites = UnityEngine.Object.FindObjectsByType<SpriteRenderer>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            sb.AppendLine("spriteRenderers=" + sprites.Length);
            for (int i = 0; i < sprites.Length && i < 12; i++)
            {
                var s = sprites[i];
                if (s == null) continue;
                var tex = s.sprite != null && s.sprite.texture != null ? s.sprite.texture : null;
                bool inFrustum = frustum != null && s.sprite != null && GeometryUtility.TestPlanesAABB(frustum, s.bounds);
                sb.AppendLine("spr[" + i + "]='" + s.name + "'" +
                              " enabled=" + s.enabled +
                              " visible=" + s.isVisible +
                              " inFrustum=" + inFrustum +
                              " goLayer=" + LayerMask.LayerToName(s.gameObject.layer) +
                              " camMaskHasLayer=" + (cam10 != null && (cam10.cullingMask & (1 << s.gameObject.layer)) != 0) +
                              " pos=" + s.transform.position +
                              " scale=" + s.transform.lossyScale +
                              " sprite=" + (s.sprite == null ? "NULL" : s.sprite.name) +
                              " rect=" + (s.sprite == null ? "n/a" : s.sprite.rect) +
                              " tex=" + (tex == null ? "NULL" : tex.name + " " + tex.width + "x" + tex.height) +
                              " mat=" + (s.sharedMaterial == null ? "NULL" : s.sharedMaterial.name) +
                              " sort=(" + s.sortingLayerName + "," + s.sortingOrder + ")" +
                              " bounds=" + (s.sprite == null ? "n/a" : s.bounds.center + " sz " + s.bounds.size));
            }
            var meshes = UnityEngine.Object.FindObjectsByType<MeshRenderer>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            sb.AppendLine("meshRenderers=" + meshes.Length);
            for (int i = 0; i < meshes.Length && i < 12; i++)
            {
                var m = meshes[i];
                if (m == null) continue;
                sb.AppendLine("mesh[" + i + "]='" + m.name + "'" +
                              " enabled=" + m.enabled +
                              " active=" + m.gameObject.activeInHierarchy +
                              " pos=" + m.transform.position +
                              " scale=" + m.transform.lossyScale +
                              " bounds=" + m.bounds.center + " sz " + m.bounds.size +
                              " visible=" + m.isVisible +
                              " screen=" + (cam10 != null ? cam10.WorldToViewportPoint(m.bounds.center).ToString("F3") : "n/a") +
                              " mat=" + (m.sharedMaterial == null ? "NULL" : m.sharedMaterial.name) +
                              " shader=" + (m.sharedMaterial != null && m.sharedMaterial.shader != null ? m.sharedMaterial.shader.name : "NULL") +
                              " shaderSupported=" + (m.sharedMaterial != null && m.sharedMaterial.shader != null ? m.sharedMaterial.shader.isSupported.ToString() : "n/a"));
            }

            // Spine ground truth: per-instance skin + attachment census (editor asmdef
            // references spine-unity — read-only diagnostics, never a production path).
            var spines = Xianxia.Sect.Visual.SpineChibiVisual.Active;
            sb.AppendLine("spineVisuals=" + spines.Count);
            foreach (var kvp in spines)
            {
                var v = kvp.Value;
                if (v == null) { sb.AppendLine("spine[" + kvp.Key + "]=NULL"); continue; }
                var sk = v.Skeleton;
                if (sk == null) { sb.AppendLine("spine[" + kvp.Key + "] skeleton=NULL"); continue; }

                int attachments = 0;
                var slots = sk.Slots;
                for (int i = 0; i < slots.Count; i++)
                    if (slots.Items[i].Attachment != null) attachments++;

                sb.AppendLine("spine[" + kvp.Key + "] skin='" + (sk.Skin == null ? "null" : sk.Skin.Name) +
                              "' slots=" + slots.Count + " attachments=" + attachments +
                              " scaleX=" + sk.ScaleX +
                              " pos=" + v.Transform.position +
                              " scale=" + v.Transform.lossyScale +
                              " activity='" + v.CurrentActivity + "'");
            }

            WriteResult(sb.ToString());

            // Real backbuffer capture (absolute path — cwd of the editor is the project root).
            ScreenCapture.CaptureScreenshot(Path.GetFullPath("Library/demo_shot.png"));
        }

        private static bool HasNewArtifact(DateTime sinceUtc)
        {
            string dir = Path.Combine(Application.persistentDataPath, "visual_spikes");
            if (!Directory.Exists(dir)) return false;
            foreach (var f in Directory.GetFiles(dir))
            {
                if (File.GetLastWriteTimeUtc(f) > sinceUtc) return true;
            }
            return false;
        }

        /// <summary>
        /// Renders the depth-10 demo camera into a RenderTexture and samples actual
        /// rendered colors at every sprite/mesh position — ground truth for "did the
        /// camera REALLY draw this renderer" (isVisible can be true while the frame
        /// stays empty when a pipeline feature culls the draw).
        /// </summary>
        private static void DemoProbe()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("playing=" + EditorApplication.isPlaying);
            Camera cam = null;
            foreach (var c in Camera.allCameras) if (c.depth == 10f) cam = c;
            if (cam == null) { WriteResult("probe: no depth10 camera\n"); return; }

            int w = 256, h = 256;
            var rt = new RenderTexture(w, h, 24);
            var prevTarget = cam.targetTexture;
            var prevAspect = cam.aspect;
            cam.targetTexture = rt;
            cam.aspect = 1f;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = prevTarget;
            cam.aspect = prevAspect;

            var pixels = tex.GetPixels32();
            var bg = pixels[0];
            sb.AppendLine("bg=" + bg.r + "," + bg.g + "," + bg.b);

            System.Func<Bounds, string> sample = bounds =>
            {
                var min = cam.WorldToViewportPoint(bounds.min);
                var max = cam.WorldToViewportPoint(bounds.max);
                if (max.z < -1f || min.x > 1f || max.x < 0f || min.y > 1f || max.y < 0f) return "offscreen";
                int x0 = Mathf.Clamp((int)(min.x * w), 0, w - 1);
                int x1 = Mathf.Clamp((int)(max.x * w), 0, w - 1);
                int y0 = Mathf.Clamp((int)(min.y * h), 0, h - 1);
                int y1 = Mathf.Clamp((int)(max.y * h), 0, h - 1);
                int drawn = 0; var first = new Color32();
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        var c = pixels[y * w + x];
                        if (Mathf.Abs(c.r - bg.r) + Mathf.Abs(c.g - bg.g) + Mathf.Abs(c.b - bg.b) > 12)
                        {
                            if (drawn == 0) first = c;
                            drawn++;
                        }
                    }
                return "rect=" + x0 + ".." + x1 + "," + y0 + ".." + y1 +
                       " drawnPx=" + drawn + (drawn > 0 ? " firstRGB=" + first.r + "," + first.g + "," + first.b + " DRAWN" : " == BG");
            };

            foreach (var s in UnityEngine.Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (s.sprite != null)
                {
                    // in-memory texture ground truth: sample the sprite's own rect region
                    var t = s.sprite.texture;
                    string texInfo;
                    try
                    {
                        var r = s.sprite.textureRect;
                        var cs = t.GetPixels32();
                        int cx0 = Mathf.Clamp((int)r.x, 0, t.width - 1);
                        int cy0 = Mathf.Clamp((int)r.y, 0, t.height - 1);
                        int cw = Mathf.Clamp((int)r.width, 1, t.width - cx0);
                        int ch = Mathf.Clamp((int)r.height, 1, t.height - cy0);
                        int opaque = 0; Color32 first = new Color32();
                        for (int y = cy0; y < cy0 + ch; y += 4)
                            for (int x = cx0; x < cx0 + cw; x += 4)
                            {
                                var c = cs[y * t.width + x];
                                if (c.a > 32) { if (opaque == 0) first = c; opaque++; }
                            }
                        texInfo = "texOpaqueSamples=" + opaque + " firstA=" + first.a + " firstRGB=" + first.r + "," + first.g + "," + first.b;
                    }
                    catch (System.Exception ex) { texInfo = "texReadFail:" + ex.Message; }

                    sb.AppendLine("spr '" + s.sprite.name + "' @" + s.transform.position +
                                  " tex=" + t.name + " id=" + t.GetInstanceID() + " " + texInfo +
                                  " -> " + sample(s.bounds));
                }
            foreach (var m in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                sb.AppendLine("mesh '" + m.name + "' @" + m.bounds.center + " -> " + sample(m.bounds));

            // ---- isolated Sprite.Create experiment: does a NON-ZERO rect render? ----
            // Three quads side by side at vp y=0.75: A = rect x0 (frame 0), B = rect x96
            // (frame 1), C = the IMPORTED sub-sprite (atlas path). Pure test — no pool/clock.
            var sheet = Resources.Load<Texture2D>("Data/Arts/Avatar/Chibi/body_body_robe_white");
            if (sheet != null)
            {
                var testSprites = new[]
                {
                    Sprite.Create(sheet, new Rect(0f, 96f, 96f, 96f), new Vector2(0.5f, 0f), 96f),
                    Sprite.Create(sheet, new Rect(96f, 96f, 96f, 96f), new Vector2(0.5f, 0f), 96f),
                    Resources.LoadAll<Sprite>("Data/Arts/Avatar/Chibi/body_body_robe_white")[0],
                };
                var testGos = new GameObject[3];
                var vxs = new[] { 0.30f, 0.50f, 0.70f };
                for (int i = 0; i < 3; i++)
                {
                    var go = new GameObject("probe_quad_" + i);
                    go.AddComponent<SpriteRenderer>().sprite = testSprites[i];
                    go.transform.position = cam.ViewportToWorldPoint(new Vector3(vxs[i], 0.75f, 10f));
                    testGos[i] = go;
                }
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                pixels = tex.GetPixels32();
                for (int i = 0; i < 3; i++)
                {
                    var vp = cam.WorldToViewportPoint(testGos[i].transform.position);
                    int px = Mathf.Clamp((int)(vp.x * w), 0, w - 1);
                    int py = Mathf.Clamp((int)(vp.y * h), 0, h - 1);
                    int drawn = 0; Color32 first = new Color32();
                    for (int y = Mathf.Max(0, py - 40); y < Mathf.Min(h, py + 40); y++)
                        for (int x = Mathf.Max(0, px - 40); x < Mathf.Min(w, px + 40); x++)
                        {
                            var c = pixels[y * w + x];
                            if (Mathf.Abs(c.r - bg.r) + Mathf.Abs(c.g - bg.g) + Mathf.Abs(c.b - bg.b) > 12)
                            { if (drawn == 0) first = c; drawn++; }
                        }
                    sb.AppendLine("quad[" + i + "] sprite='" + testSprites[i].name + "' rect=" + testSprites[i].rect +
                                  " tex=" + testSprites[i].texture.name + " " + testSprites[i].texture.width + "x" + testSprites[i].texture.height +
                                  " drawnPx=" + drawn + (drawn > 0 ? " DRAWN" : " == BG"));
                    UnityEngine.Object.DestroyImmediate(testGos[i]);
                    if (i < 2) UnityEngine.Object.DestroyImmediate(testSprites[i]);
                }
            }
            else sb.AppendLine("quad test skipped: white sheet not found");

            UnityEngine.Object.DestroyImmediate(tex);
            UnityEngine.Object.DestroyImmediate(rt);
            WriteResult(sb.ToString());
        }

        private static string FetchCsvIndex()
        {
            string dir = Path.Combine(Application.persistentDataPath, "visual_spikes");
            if (!Directory.Exists(dir)) return "no_csv_dir";
            var files = Directory.GetFiles(dir, "*.csv");
            var pngs = Directory.GetFiles(dir, "*.png");
            var lines = new System.Collections.Generic.List<string>
            {
                "csv_count=" + files.Length,
                "png_count=" + pngs.Length
            };
            foreach (var f in files) lines.Add(Path.GetFileName(f) + "|" + new FileInfo(f).Length + "B");
            foreach (var f in pngs) lines.Add(Path.GetFileName(f) + "|" + new FileInfo(f).Length + "B");

            string dest = Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                "Library", "visual_spike_artifacts");
            Directory.CreateDirectory(dest);
            foreach (var f in files) File.Copy(f, Path.Combine(dest, Path.GetFileName(f)), true);
            foreach (var f in pngs) File.Copy(f, Path.Combine(dest, Path.GetFileName(f)), true);
            lines.Add("copied_to=" + dest);
            return string.Join("\n", lines.ToArray());
        }

        private static void WriteResult(string text)
        {
            try { File.WriteAllText(ResultPath, text); } catch (IOException) { }
        }
    }
}

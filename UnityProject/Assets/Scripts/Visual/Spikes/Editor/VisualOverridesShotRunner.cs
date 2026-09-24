#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using VContainer;
using Xianxia.Sect.Visual;
using Spine.Unity;

namespace Xianxia.Sect.Visual.Spikes.EditorTools
{
    /// <summary>
    /// DEV-ONLY spot-check (marker-file remote control: "visual_overrides_shot" in
    /// Library/visual_spike_command.txt): plays, loads TestGameplayScene with the Q5
    /// override hooks live (mirrors VisualSpineBootstrap minus the gate — the S4
    /// license gate itself stays untouched), then renders the game camera to a
    /// RenderTexture and saves:
    ///   Library/visual_overrides_shot.png            — full frame
    ///   Library/visual_overrides_shot_d000.png       — crop around the male rig
    ///   Library/visual_overrides_shot_d002.png       — crop around the female rig
    ///   Library/visual_overrides_shot_info.txt       — camera/rect metadata
    /// Purpose: visually + numerically inspect edge colors / semi-transparency after
    /// the straight-alpha conversion (PM backup lives in Library/pm_backup/).
    /// </summary>
    [InitializeOnLoad]
    public static class VisualOverridesShotRunner
    {
        private const string InfoPath = "Library/visual_overrides_shot_info.txt";
        private const string SessionMarker = "Library/visual_overrides_shot_live.txt";
        private const string SceneA = "Assets/Scenes/TestGameplayScene.unity";
        private const int W = 2560, H = 1440;

        static VisualOverridesShotRunner() { EditorApplication.update += SessionUpdate; }

        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.isPlaying = false;
                return;
            }
            if (VisualRuntimeConfig.Instance.SpineActivationRequested)
            {
                Debug.LogError("[VisualOverridesShot] SpineActivationRequested is TRUE — production gate must stay untouched");
                return;
            }
            try { File.WriteAllText(SessionMarker, "requested"); } catch (IOException) { }
            EditorApplication.isPlaying = true;
            Debug.Log("[VisualOverridesShot] entering play mode…");
        }

        private static bool _session;
        private static int _step;
        private static double _stepUntil;
        private static IObjectResolver _injector;

        private static void SessionUpdate()
        {
            bool marker = File.Exists(SessionMarker);
            if (!_session && marker && EditorApplication.isPlaying) { StartSession(); return; }
            if (_session)
            {
                if (!EditorApplication.isPlaying) { Finish("play mode ended"); return; }
                if (EditorApplication.timeSinceStartup < _stepUntil) return;
                StepMachine();
            }
            else if (!marker)
            {
                EditorApplication.update -= SessionUpdate;
            }
        }

        private static void StartSession()
        {
            _session = true;
            _step = 0;
            _stepUntil = EditorApplication.timeSinceStartup + 1.0;
            _injector = GameLifetimeScope.Injector;
            if (_injector == null) throw new InvalidOperationException("no GameLifetimeScope.Injector");

            // PRODUCTION PATH (post-S4): VisualSpineBootstrap already registered the
            // shared-rig factory AND the Q5 override hooks during play boot — the
            // runner registers nothing and captures whatever production renders.
            if (!VisualRuntimeConfig.Instance.SpineActivationRequested)
                throw new InvalidOperationException(
                    "SpineActivationRequested=false — S4 decision missing in GameLifetimeScope");
            if (DiscipleVisualSystem.SpineOverrideVisualFactory == null ||
                DiscipleVisualSystem.SpineVisualFactory == null)
                throw new InvalidOperationException(
                    "production Spine hooks not registered — bootstrap did not activate");
            Debug.Log("[VisualOverridesShot] session started — production hooks live (bootstrap)");
        }

        private static void StepMachine()
        {
            _stepUntil = EditorApplication.timeSinceStartup + 0.5;
            try
            {
                switch (_step)
                {
                    case 0: // load gameplay scene → spawn d000/d002 on their own rigs
                        _injector.Resolve<SceneLoader>().LoadGameplayScene(SceneA);
                        _stepUntil += 3.0;
                        _step++;
                        break;

                    case 1: // frame + render + save crops
                        Capture();
                        _step++;
                        break;

                    case 2:
                        Finish(null);
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[VisualOverridesShot] " + ex);
                Finish(ex.Message);
            }
        }

        private static void Capture()
        {
            if (!SpineChibiVisual.Active.TryGetValue("d000", out var d000) ||
                !SpineChibiVisual.Active.TryGetValue("d002", out var d002))
                throw new InvalidOperationException("d000/d002 not spawned: d000=" +
                    SpineChibiVisual.Active.ContainsKey("d000") + " d002=" + SpineChibiVisual.Active.ContainsKey("d002"));
            SpineChibiVisual.Active.TryGetValue("d003", out var d003); // shared-rig render — may be absent if budget-degraded

            Camera cam = null;
            foreach (var c in Camera.allCameras)
            {
                var vp = c.WorldToViewportPoint(d000.Transform.position);
                if (vp.z > 0 && vp.x > -1f && vp.x < 2f && vp.y > -1f && vp.y < 2f) { cam = c; break; }
            }
            if (cam == null) cam = Camera.main;
            if (cam == null) throw new InvalidOperationException("no camera");

            var rt = new RenderTexture(W, H, 24);
            var prevTarget = cam.targetTexture;
            var prevAspect = cam.aspect;
            // Set the RENDER aspect FIRST — every viewport computation below
            // (centering + crop rects) must use the same aspect as the actual
            // render, or the crops land off-target (the bug that emptied d002/d003).
            cam.aspect = (float)W / H;

            // center the midpoint of the two visuals so both land in frame
            var mid = (d000.Transform.position + d002.Transform.position) * 0.5f;
            var vpMid = cam.WorldToViewportPoint(mid);
            if (vpMid.z > 0)
            {
                var worldCenter = cam.ViewportToWorldPoint(new Vector3(0.5f, 0.5f, vpMid.z));
                cam.transform.position += worldCenter - mid;
            }

            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = null;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("camera=" + cam.name + " pos=" + cam.transform.position +
                          " ortho=" + cam.orthographic + " size=" + cam.orthographicSize +
                          " fov=" + cam.fieldOfView + " frame=" + W + "x" + H);
            // crop math runs BEFORE restoring the aspect — same projection as the render
            SaveVisualCrop(sb, tex, cam, "d000", d000);
            SaveVisualCrop(sb, tex, cam, "d002", d002);
            if (d003 != null) SaveVisualCrop(sb, tex, cam, "d003", d003);
            else sb.AppendLine("d003: not on Spine this session (budget-degraded?)");
            File.WriteAllText(InfoPath, sb.ToString());

            File.WriteAllBytes("Library/visual_overrides_shot.png", tex.EncodeToPNG());
            Debug.Log("[VisualOverridesShot] captured Library/visual_overrides_shot.png");

            UnityEngine.Object.Destroy(tex);
            UnityEngine.Object.Destroy(rt);
            cam.targetTexture = prevTarget;
            cam.aspect = prevAspect;
        }

        private static void SaveVisualCrop(System.Text.StringBuilder sb, Texture2D tex, Camera cam,
                                           string id, SpineChibiVisual v)
        {
            var bounds = new Bounds(v.Transform.position, Vector3.zero);
            var mr = v.Transform.GetComponentInChildren<MeshRenderer>();
            if (mr != null) bounds = mr.bounds;

            var pMin = cam.WorldToViewportPoint(bounds.min);
            var pMax = cam.WorldToViewportPoint(bounds.max);
            int x0 = Mathf.Clamp((int)(Mathf.Min(pMin.x, pMax.x) * W) - 8, 0, W - 1);
            int x1 = Mathf.Clamp((int)(Mathf.Max(pMin.x, pMax.x) * W) + 8, 0, W - 1);
            int y0 = Mathf.Clamp((int)(Mathf.Min(pMin.y, pMax.y) * H) - 8, 0, H - 1);
            int y1 = Mathf.Clamp((int)(Mathf.Max(pMin.y, pMax.y) * H) + 8, 0, H - 1);
            int cw = x1 - x0 + 1, ch = y1 - y0 + 1;
            if (cw <= 4 || ch <= 4)
            {
                sb.AppendLine(id + ": OFFSCREEN rect=" + x0 + ".." + x1 + "," + y0 + ".." + y1);
                return;
            }
            var crop = tex.GetPixels(x0, y0, cw, ch);
            var cropTex = new Texture2D(cw, ch, TextureFormat.RGBA32, false);
            cropTex.SetPixels(crop);
            cropTex.Apply();
            File.WriteAllBytes("Library/visual_overrides_shot_" + id + ".png", cropTex.EncodeToPNG());
            UnityEngine.Object.Destroy(cropTex);
            sb.AppendLine(id + " rect_px=" + x0 + ".." + x1 + "," + y0 + ".." + y1 +
                          " size=" + cw + "x" + ch +
                          " activity=" + v.CurrentActivity +
                          " scaleX=" + (v.Skeleton != null ? v.Skeleton.ScaleX.ToString() : "null"));
        }

        private static void Finish(string note)
        {
            if (!_session) return;
            _session = false;
            // No static cleanup: the runner owns nothing — production bootstrap set up
            // every hook, and the play-exit domain unload restores all statics anyway.
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            try
            {
                File.AppendAllText(InfoPath, "shot done " + DateTime.UtcNow.ToString("o") +
                                             (string.IsNullOrEmpty(note) ? "" : " note=" + note) + "\n");
            }
            catch { /* ignore */ }
            Debug.Log("[VisualOverridesShot] done " + (note ?? ""));
        }
    }
}
#endif

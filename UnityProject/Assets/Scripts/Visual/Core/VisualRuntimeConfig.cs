using UnityEngine;

namespace Xianxia.Sect.Visual
{
    /// <summary>
    /// Phase 1 runtime config (plain C# singleton, registered in GameLifetimeScope).
    ///
    /// S4 (Spine license) ยังไม่ตัดสิน (phase0-results §4) — SpineEnabled คง false ตาม C1
    /// SpriteSheetEnabled = true ตั้งแต่ Phase 2: chibi sheets ถูก generate ครบทุก part
    /// ที่ MockSectData ใช้จริง และ VisualCoverageValidator รายงาน 0 error (C6)
    /// </summary>
    public sealed class VisualRuntimeConfig
    {
        public static readonly VisualRuntimeConfig Instance = new VisualRuntimeConfig();

        /// <summary>SpriteSheet chibi backend — เปิดแล้วใน Phase 2 (sheet assets จริง + atlas + validator ผ่าน 0 error ตาม C6)</summary>
        public bool SpriteSheetEnabled { get { return _spriteSheetEnabled; } set { _spriteSheetEnabled = value; } }
        private volatile bool _spriteSheetEnabled = true;

        /// <summary>Spine chibi backend — false จนกว่า S4 license ตัดสิน + มี rig จริง (Phase 3)</summary>
        public bool SpineEnabled { get { return _spineEnabled; } set { _spineEnabled = value; } }
        private volatile bool _spineEnabled;

        /// <summary>
        /// S4 LICENSE GATE (L12 — การตัดสินใจของมนุษย์เท่านั้น):
        /// true = มนุษย์ยืนยันแล้วว่า Spine Editor/runtime license ถูกซื้อครบตาม
        /// phase0-results §4 checklist — จึงอนุญาตให้ VisualSpineBootstrap ผูก
        /// SpineVisualFactory และ flip SpineEnabled = true ตอนรัน
        /// false (default, production) = bootstrap เป็น no-op — Spine path ปิดสนิท
        /// ห้าม set true ในโค้ด — ตั้งผ่าน bootstrap config เมื่อ license ผ่านเท่านั้น
        /// </summary>
        public bool SpineActivationRequested { get { return _spineActivationRequested; } set { _spineActivationRequested = value; } }
        private volatile bool _spineActivationRequested;

        /// <summary>
        /// DEV-ONLY DEMO SEAM (R1/S4 — see LLMWiki/wiki/sources/visual-demo-scene.md):
        /// tier policy treats Spine as allowed when (SpineEnabled || DevSpineOverride).
        /// Default false — the ship configuration NEVER sets this; only the demo
        /// scene's VisualDemoSpineEnabler flips it true in Awake and back to false in
        /// OnDestroy, so it lives exactly as long as the demo scene does. This is NOT
        /// the S4 license gate (SpineActivationRequested) — that human decision stays
        /// untouched and production bootstrap remains gated by it.
        /// </summary>
        public bool DevSpineOverride { get { return _devSpineOverride; } set { _devSpineOverride = value; } }
        private volatile bool _devSpineOverride;

        /// <summary>
        /// Resources path ของ shared SkeletonDataAsset (rig กลาง 1 ตัว — Q4)
        /// ยังไม่มี rig จริงในโปรเจกต์ — bootstrap จะ null-check + warn แล้วคง Spine
        /// ปิดไว้ ไม่ hard-crash ฉาก (ตาม spec Phase 3)
        /// </summary>
        public string SpineSkeletonResourcePath { get { return _spineSkeletonResourcePath; } set { _spineSkeletonResourcePath = value; } }
        private string _spineSkeletonResourcePath = string.Empty;

        /// <summary>
        /// จำนวน Spine instance สูงสุดที่ runtime ยอม spawn ก่อน degrade เป็น SpriteSheet (แผน §7)
        /// ค่า 20 มาจาก Q6 ของ LLMWiki/wiki/sources/disciple-visual-system-phase0-results.md §5:
        /// S1 วัด 20 instances = 1.648 ms/frame (gate 2.0 ms → headroom 2.4×)
        /// ⚠️ วัดจาก Spine example asset (mix-and-match-pro) ไม่ใช่ chibi rig จริง —
        /// อาจต้องลดลงเมื่อมี rig จริงใน Phase 3 (L8)
        /// </summary>
        public int SpineBudget { get { return _spineBudget; } set { _spineBudget = value; } }
        private int _spineBudget = 20;

        /// <summary>
        /// Q5 stub (Phase 3+) — Resources path of the future story-character rig
        /// override table (discipleId → skeletonDataId). Loader/resolution logic is
        /// NOT built yet; only the config slot exists so Visual.Spine can read the
        /// path once that work lands. Empty = no overrides.
        /// </summary>
        public string VisualOverridesPath { get { return _visualOverridesPath; } set { _visualOverridesPath = value; } }
        private string _visualOverridesPath = string.Empty;
    }
}

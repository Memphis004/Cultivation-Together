using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xianxia.Sect.Visual
{
    /// <summary>
    /// Portrait v2 (แผน §Phase 5): maps discipleId → Resources path ของ portrait
    /// (ภาพวาดเดี่ยว 1024×1536 บน canvas convention เดียวกับ placeholder —
    /// head-center 50% x / 31.7708% from top) สำหรับศิษย์ที่มีภาพเฉพาะตัว
    ///
    /// คู่กับ VisualOverrideMap (คนละตาราง — ห้ามแตะตาราง Spine):
    /// - ตารางนี้: <c>Resources/Data/portrait_overrides</c> — { overrides: [{ discipleId, portraitResourcePath }] }
    /// - ศิษย์ที่ไม่มีในตาราง (รวม recruit ใหม่ทุกคน) → caller ใช้ layered path เดิม 100%
    ///
    /// Production loader — JsonUtility + Resources.Load, lazy, L4 (never throws).
    /// </summary>
    [Serializable]
    public class PortraitOverrideMap
    {
        [Serializable]
        public class Entry
        {
            public string discipleId;              // DiscipleState.DiscipleId เช่น "d000"
            public string portraitResourcePath;    // Resources path ของ Sprite เช่น "Avatar/Portraits/portrait_d000"
        }

        [Serializable]
        private class Root
        {
            public Entry[] overrides = new Entry[0];
        }

        private readonly Dictionary<string, string> _map =
            new Dictionary<string, string>(8, StringComparer.Ordinal);
        private readonly string _resourcePath;
        private bool _loaded;

        public PortraitOverrideMap(string resourcePath = "Data/portrait_overrides")
        {
            _resourcePath = resourcePath;
        }

        /// <summary>
        /// Parse + install entries จาก JSON string (test seam ของ EnsureLoaded —
        /// โหลดซ้ำได้ deterministic: ล้างตารางเก่าก่อนเสมอ)
        /// </summary>
        public void LoadFromJson(string json)
        {
            _loaded = true;
            _map.Clear();

            if (string.IsNullOrEmpty(json)) return;

            Root root;
            try
            {
                root = JsonUtility.FromJson<Root>(json);
            }
            catch (Exception)
            {
                return; // L4: table พัง = ไม่มี override ไม่ใช่ crash
            }

            if (root == null || root.overrides == null) return;

            for (int i = 0; i < root.overrides.Length; i++)
            {
                var e = root.overrides[i];
                if (e == null || string.IsNullOrEmpty(e.discipleId) ||
                    string.IsNullOrEmpty(e.portraitResourcePath)) continue;
                _map[e.discipleId] = e.portraitResourcePath;
            }
        }

        /// <summary>Lazy-load once — construct ได้ตั้งแต่ต้น แตะ disk ครั้งแรกที่ใช้</summary>
        private void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;

            var text = Resources.Load<TextAsset>(_resourcePath);
            if (text == null)
            {
                Debug.LogWarning("[PortraitOverrideMap] Resources/" + _resourcePath +
                                 " not found — ทุกศิษย์ใช้ layered portrait เดิม");
                return;
            }

            LoadFromJson(text.text);
            if (_map.Count == 0)
            {
                Debug.LogWarning("[PortraitOverrideMap] " + _resourcePath +
                                 " ไม่มี entry ที่ใช้ได้ — ทุกศิษย์ใช้ layered portrait เดิม");
            }
        }

        /// <summary>
        /// Resources path ของ portrait สำหรับศิษย์ — empty string เมื่อไม่มี override
        /// (caller fallback สู่ layered path เดิม)
        /// </summary>
        public string ResolveOverride(string discipleId)
        {
            if (string.IsNullOrEmpty(discipleId)) return string.Empty;
            EnsureLoaded();
            string path;
            return _map.TryGetValue(discipleId, out path) ? path : string.Empty;
        }

        /// <summary>True เมื่อศิษย์คนนี้มี portrait override</summary>
        public bool HasOverride(string discipleId)
        {
            return !string.IsNullOrEmpty(ResolveOverride(discipleId));
        }

        /// <summary>True เมื่อตารางมี entry อย่างน้อย 1 รายการ</summary>
        public bool HasAny()
        {
            EnsureLoaded();
            return _map.Count > 0;
        }

        /// <summary>จำนวน entry ที่ resolve ได้ (diagnostics)</summary>
        public int Count
        {
            get { EnsureLoaded(); return _map.Count; }
        }
    }
}

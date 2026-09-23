using System.Collections.Generic;
using UnityEngine;

namespace Xianxia.Sect.Visual
{
    /// <summary>
    /// แกน resolve ของ visual system (D4 — resolve ครั้งเดียวส่งผลให้ทุก backend)
    /// 3 ค่า: Portrait (UI เดิม), SpriteSheet, Spine (chibi ในฉาก)
    /// แยกจาก ChibiBackend (2 ค่า entitlement บน state) — ดู L1/L3
    /// </summary>
    public enum VisualBackend { Portrait, SpriteSheet, Spine }

    /// <summary>ผลลัพธ์ 1 layer จาก resolver — เรียงตาม Order แล้วส่งให้ backend render</summary>
    public struct ResolvedLayer
    {
        public string Slot;
        public string PartId;
        public string Asset;      // spritePath / chibiSheetPath / (Spine ไม่ใช้ — ดู SpineSkin)
        public string SpineSkin;  // เฉพาะ Spine
        public int Order;
        public bool IsBack;
        public Color Tint;        // white ถ้าไม่ tintable
    }

    /// <summary>
    /// AppearanceResolver (แผน §4.4) — แปลง AvatarAppearance + VisualBackend → List&lt;ResolvedLayer&gt;
    ///
    /// Fallback chain (L2): partId → def → ถ้า !def.Supports(backend) → default ของ slot
    /// → ถ้า default ก็ไม่รองรับ → ข้าม slot + LogWarning ครั้งเดียวต่อ (slot, backend)
    /// </summary>
    public sealed class AppearanceResolver
    {
        private readonly AvatarPartPool _pool;

        // (slot, backend) ที่เตือนไปแล้ว — LogWarning ครั้งเดียวต่อ key
        private readonly HashSet<string> _warned = new HashSet<string>();

        public AppearanceResolver(AvatarPartPool pool)
        {
            _pool = pool;
        }

        /// <summary>
        /// Resolve ทุก slot เป็น layer list เรียงตาม Order (น้อย→มาก)
        /// รวม base layer เสมอ (Portrait เหมือนเดิม; Sprite/Spine รวมอยู่ใน body/rig)
        /// </summary>
        public List<ResolvedLayer> Resolve(AvatarAppearance a, VisualBackend backend)
        {
            var result = new List<ResolvedLayer>(16);

            // Base layer — Portrait วาดเองเหมือนเดิม (คงพฤติกรรม Rebuild เดิมให้เป๊ะ)
            if (backend == VisualBackend.Portrait)
            {
                var baseDef = _pool.Resolve(AvatarSlots.Base, string.Empty);
                if (baseDef != null && baseDef.Supports(backend))
                {
                    result.Add(MakeLayer(baseDef, false, backend));
                }
            }

            if (a == null || a.Parts == null) return result;

            // เดินตาม Equippable ตามลำดับคงที่ (ผลลัพธ์ deterministic แม้ dict ไม่ sort)
            foreach (var slot in AvatarSlots.Equippable)
            {
                string partId;
                if (!a.Parts.TryGetValue(slot, out partId)) partId = string.Empty;

                var def = _pool.Resolve(slot, partId);
                if (def == null)
                {
                    // Fallback: default ของ slot ก็ไม่รองรับ backend นี้ → ข้าม slot + เตือนครั้งเดียว
                    WarnOnce(slot, backend, def);
                    continue;
                }
                if (def.IsEmptyLayer)
                {
                    // Empty layer (เช่น *_none defaults) = เจตนา "ไม่มีอะไรจะวาด" —
                    // ข้ามเงียบ ๆ ทุก backend ไม่ใช่ความผิดพลาดของ data
                    continue;
                }
                if (!def.Supports(backend))
                {
                    WarnOnce(slot, backend, def);
                    continue;
                }

                AddSlotLayers(result, def, backend);
            }

            result.Sort((x, y) => x.Order.CompareTo(y.Order));
            return result;
        }

        /// <summary>
        /// Signature guard รวม backend — ใช้แทน BuildSignature เดิม
        /// เดินตาม Equippable + Parts (ทั้งสองแหล่ง) ให้ครอบคลุมเท่าเดิม
        /// </summary>
        public string Signature(AvatarAppearance a, VisualBackend backend)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("B=").Append((int)backend);

            if (a == null) return sb.ToString();

            sb.Append(";P=").Append(a.PoseId ?? "");

            // ครอบคลุมทั้ง Equippable (slot ที่ประกาศ) และ Keys ที่อยู่นอก list (กัน data แปลกปลอม)
            var seen = new HashSet<string>();
            AppendParts(sb, a, AvatarSlots.Equippable, seen);

            var keys = new List<string>(a.Parts.Keys);
            keys.Sort();
            for (int i = 0; i < keys.Count; i++)
            {
                if (!seen.Add(keys[i])) continue;
                sb.Append("|").Append(keys[i]).Append("=").Append(a.Parts[keys[i]]);
            }

            return sb.ToString();
        }

        private static void AppendParts(System.Text.StringBuilder sb, AvatarAppearance a,
                                        IEnumerable<string> slots, HashSet<string> seen)
        {
            foreach (var slot in slots)
            {
                if (!seen.Add(slot)) continue;
                string v;
                sb.Append("|").Append(slot).Append("=")
                  .Append(a.Parts.TryGetValue(slot, out v) ? v : "");
            }
        }

        private void AddSlotLayers(List<ResolvedLayer> result, AvatarPartDef def, VisualBackend backend)
        {
            var tint = def.tintable ? Color.white : Color.white; // tint resolution เป็นของ Phase 2/5 — Phase 1 ยังไม่ apply สี
            var partId = def.id;

            // Back layer ก่อน (order น้อยกว่า) — ต้องไม่หาย (test case 2)
            string backPath = def.BackPathFor(backend);
            if (!string.IsNullOrEmpty(backPath))
            {
                int backOrder = def.BackOrderFor(backend) > 0
                    ? def.BackOrderFor(backend)
                    : def.OrderFor(backend) - 30; // fallback เดิมของ Portrait (drawOrder-30)
                result.Add(new ResolvedLayer
                {
                    Slot = def.slot,
                    PartId = partId,
                    Asset = backPath,
                    SpineSkin = backend == VisualBackend.Spine ? def.spineSkin : string.Empty,
                    Order = backOrder,
                    IsBack = true,
                    Tint = tint,
                });
            }

            string mainPath = backend == VisualBackend.Spine ? def.spineSkin : MainAssetFor(def, backend);
            if (!string.IsNullOrEmpty(mainPath))
            {
                result.Add(new ResolvedLayer
                {
                    Slot = def.slot,
                    PartId = partId,
                    Asset = backend == VisualBackend.Spine ? string.Empty : mainPath,
                    SpineSkin = backend == VisualBackend.Spine ? def.spineSkin : string.Empty,
                    Order = def.OrderFor(backend),
                    IsBack = false,
                    Tint = tint,
                });
            }
        }

        private static string MainAssetFor(AvatarPartDef def, VisualBackend backend)
        {
            switch (backend)
            {
                case VisualBackend.Portrait:    return def.spritePath;
                case VisualBackend.SpriteSheet: return def.chibiSheetPath;
                default: return string.Empty;
            }
        }

        private ResolvedLayer MakeLayer(AvatarPartDef def, bool isBack, VisualBackend backend)
        {
            // base layer — main path เท่านั้น (isBack เผื่ออนาคต: base ไม่มี back layer)
            return new ResolvedLayer
            {
                Slot = def.slot,
                PartId = def.id,
                Asset = MainAssetFor(def, backend),
                SpineSkin = string.Empty,
                Order = def.OrderFor(backend),
                IsBack = isBack,
                Tint = Color.white,
            };
        }

        private void WarnOnce(string slot, VisualBackend backend, AvatarPartDef def)
        {
            string key = slot + ":" + backend;
            if (!_warned.Add(key)) return;

            if (def == null)
            {
                Debug.LogWarning("[AppearanceResolver] no def for slot '" + slot + "' on backend " + backend + " — skipping slot");
            }
            else
            {
                Debug.LogWarning("[AppearanceResolver] part '" + def.id + "' (slot '" + slot +
                                 "') does not support backend " + backend +
                                 " and slot default does not either — skipping slot");
            }
        }
    }
}

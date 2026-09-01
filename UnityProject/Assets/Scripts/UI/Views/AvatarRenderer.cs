using System.Collections.Generic;
using Xianxia.Sect;
using UnityEngine;
using UnityEngine.UI;

namespace Xianxia.Sect.UI
{
    /// <summary>
    /// Avatar framing presets — 3 บริบทการแสดงผล ใช้ art ชุดเดียวกัน
    /// FullBody = ตัวเต็ม (Character Creation)
    /// Bust     = ครึ่งตัว (Dialogue portrait)
    /// HeadIcon = แค่หัว (HUD icon วงกลมเล็ก)
    /// 
    /// ทุก part ต้องวาดบน canvas ขนาดเดียวกัน 1024×1536
    /// จุดกึ่งกลางหัวต้องอยู่พิกัดเดียวกันทุกไฟล์
    /// </summary>
    public enum AvatarFraming
    {
        FullBody,
        Bust,
        HeadIcon
    }

    [System.Serializable]
    public struct AvatarFramingPreset
    {
        public AvatarFraming mode;
        public Vector2 pivotOffset;   // เลื่อน layerRoot ขึ้นเพื่อ "ซูม" ไปที่หัว
        public float scale;         // FullBody=1, Bust≈2.2, HeadIcon≈4.5
    }

    /// <summary>
    /// Renders a character as layered sprites from AvatarAppearance.
    /// 
    /// สำคัญ: ไม่ hardcode 4 slot อีกต่อไป — loop ตาม Parts.Keys ทั้งหมด
    /// Hair ถูก split เป็น 2 layer (hair_back + hair_front) จาก spritePathBack
    /// ของ AvatarPartDef เดียวกัน ทำให้ผมหลังอยู่ใต้ตัว ผมหน้าอยู่บนหน้า
    /// 
    /// AvatarFraming ควบคุม scale + offset ของ layerRoot ใน Awake
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class AvatarRenderer : MonoBehaviour
    {
        private const float RefreshInterval = 0.1f;

        // framing presets
        private static readonly AvatarFramingPreset[] FramingPresets = new AvatarFramingPreset[]
        {
            new AvatarFramingPreset { mode = AvatarFraming.FullBody, pivotOffset = Vector2.zero,       scale = 1f   },
            new AvatarFramingPreset { mode = AvatarFraming.Bust,     pivotOffset = new Vector2(0, -200), scale = 2.2f },
            new AvatarFramingPreset { mode = AvatarFraming.HeadIcon, pivotOffset = new Vector2(0, -350), scale = 4.5f },
        };

        [SerializeField] private RectTransform layerRoot;     // parent of all layer Images
        [SerializeField] private Image layerPrefab;   // blank Image stretched to fill parent
        [SerializeField] private AvatarFraming framing = AvatarFraming.FullBody;

        private AvatarPartPool _pool;                         // injected via Initialize()
        private CanvasGroup _canvasGroup;

        private readonly List<Image> _activeLayers = new List<Image>();
        private readonly Stack<Image> _layerPool = new Stack<Image>();      // pooling to avoid GC
        private readonly Dictionary<string, Sprite> _spriteCache =
            new Dictionary<string, Sprite>();

        private AvatarAppearance _appearance;
        private string _lastSignature = null;   // prevents rebuild when data hasn't changed
        private float _timer;

        private void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
            if (layerRoot == null) layerRoot = (RectTransform)transform;

            ApplyFraming();
        }

        /// <summary>Called from the Presenter that owns this card (passes the pool resolved from VContainer)</summary>
        public void Initialize(AvatarPartPool pool)
        {
            _pool = pool;
        }

        /// <summary>เปลี่ยน framing mode แล้ว apply preset ทันที</summary>
        public void SetFraming(AvatarFraming mode)
        {
            framing = mode;
            ApplyFraming();
        }

        private void ApplyFraming()
        {
            for (int i = 0; i < FramingPresets.Length; i++)
            {
                if (FramingPresets[i].mode == framing)
                {
                    var preset = FramingPresets[i];
                    layerRoot.localPosition = new Vector3(preset.pivotOffset.x, preset.pivotOffset.y, 0);
                    layerRoot.localScale = new Vector3(preset.scale, preset.scale, 1f);
                    return;
                }
            }
        }

        public void SetAppearance(AvatarAppearance appearance)
        {
            _appearance = appearance;
            _timer = RefreshInterval;   // trigger rebuild in the next frame
        }

        private void Update()
        {
            if (_appearance == null || _pool == null) return;
            if (!IsVisible()) return;                       // save cost when hidden/scrolled off-screen

            _timer += Time.unscaledDeltaTime;               // unscaled because UI must tick during pause
            if (_timer < RefreshInterval) return;
            _timer = 0f;

            var signature = BuildSignature(_appearance);
            if (signature == _lastSignature) return;        // no change = don't touch hierarchy
            _lastSignature = signature;

            Rebuild();
        }

        private bool IsVisible()
        {
            return isActiveAndEnabled && _canvasGroup.alpha > 0.01f;
        }

        private static string BuildSignature(AvatarAppearance a)
        {
            // Build from dictionary — sorted by key for stable comparison
            var keys = new List<string>(a.Parts.Keys);
            keys.Sort();
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < keys.Count; i++)
            {
                if (i > 0) sb.Append("|");
                sb.Append(keys[i]);
                sb.Append("=");
                sb.Append(a.Parts[keys[i]]);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Rebuild layer stack from AvatarAppearance.Parts dictionary.
        /// 
        /// Layout (drawOrder):
        ///   0  base_silhouette  — โครงคงที่
        ///   10 hair_back        — ผมหลัง อยู่ใต้ตัว (จาก spritePathBack)
        ///   20 body             — ชุด/ลำตัว
        ///   30 head             — หน้า+ผิว
        ///   34 face_marking     — ลายหน้า
        ///   36 eyes/brows/mouth/nose — ใบหน้าย่อย
        ///   38 eyeshadow        — อายแชโดว์
        ///   40 hair_front       — ผมหน้า อยู่บนหน้า
        ///   50 accessory        — ปิ่น/มงกุฎ
        /// </summary>
        private void Rebuild()
        {
            ReleaseAllLayers();

            // 1) สร้าง list ของ (DrawOrder, SpritePath) ทั้งหมดที่จะวาด
            //    แทนที่จะ sort แค่ def แล้ว spawn ตามนั้น
            var layersToDraw = new List<(int order, string path)>(16);

            // Base layer (คงที่)
            var baseDef = _pool.Resolve(AvatarSlots.Base, string.Empty);
            if (baseDef != null) layersToDraw.Add((baseDef.drawOrder, baseDef.spritePath));

            // Loop ผ่าน Parts ของ Avatar
            foreach (var kvp in _appearance.Parts)
            {
                var def = _pool.Resolve(kvp.Key, kvp.Value);
                if (def == null) continue;

                // ถ้ามีผมหลัง (spritePathBack ไม่ว่าง) -> เพิ่มเข้า list ด้วย drawOrderBack
                if (!string.IsNullOrEmpty(def.spritePathBack))
                {
                    // ใช้ drawOrderBack ถ้ามีค่า (>0) ไม่งั้นใช้ drawOrder - 30 (fallback)
                    int backOrder = def.drawOrderBack > 0 ? def.drawOrderBack : (def.drawOrder - 30);
                    layersToDraw.Add((backOrder, def.spritePathBack));
                }

                // ผมหน้า / ชิ้นส่วนปกติ -> ใช้ drawOrder ปกติ
                if (!string.IsNullOrEmpty(def.spritePath))
                {
                    layersToDraw.Add((def.drawOrder, def.spritePath));
                }
            }

            // 2) Sort ทุก layer ตาม DrawOrder (จากน้อยไปมาก = ล่างไปบน)
            layersToDraw.Sort((a, b) => a.order.CompareTo(b.order));

            // 3) Spawn Image ตามลำดับที่ sort แล้ว
            for (int i = 0; i < layersToDraw.Count; i++)
            {
                var layer = layersToDraw[i];
                var sprite = LoadSprite(layer.path);
                if (sprite == null) continue;

                var img = RentLayer();
                img.sprite = sprite;
                img.enabled = true;
                // Sibling index จะเรียงตามลำดับที่ sort แล้ว -> ถูกต้อง!
                // hair_back (10) จะถูก spawn ก่อน body (20) -> อยู่ข้างหลัง
                // hair_front (40) จะถูก spawn หลัง head (30) -> อยู่ข้างหน้า
            }
        }

        private void AddResolved(List<AvatarPartDef> target, string slot, string partId)
        {
            var def = _pool.Resolve(slot, partId);
            if (def != null) target.Add(def);
        }

        private Sprite LoadSprite(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            Sprite cached;
            if (_spriteCache.TryGetValue(path, out cached)) return cached;

            var sprite = Resources.Load<Sprite>(path);
            if (sprite == null)
                Debug.LogWarning($"[AvatarRenderer] Sprite not found: Resources/{path}");

            _spriteCache[path] = sprite;    // cache even null to avoid Load every 0.1s
            return sprite;
        }

        private Image RentLayer()
        {
            Image img = _layerPool.Count > 0 ? _layerPool.Pop()
                                             : Instantiate(layerPrefab, layerRoot);
            img.gameObject.SetActive(true);
            _activeLayers.Add(img);
            return img;
        }

        private void ReleaseAllLayers()
        {
            for (int i = 0; i < _activeLayers.Count; i++)
            {
                var img = _activeLayers[i];
                img.sprite = null;
                img.gameObject.SetActive(false);
                _layerPool.Push(img);
            }
            _activeLayers.Clear();
        }

        private void OnDestroy()
        {
            _spriteCache.Clear();
        }
    }
}

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Xianxia.Sect.UI
{
    public static class InkWidgets
    {
        private static readonly HashSet<string> Warned = new HashSet<string>();
        private static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();
        private static Sprite circle;
        public static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }
        public static void Stretch(RectTransform rt, float inset = 0)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset); rt.offsetMax = new Vector2(-inset, -inset);
        }
        public static Image Fill(RectTransform rt, Color color, bool raycast = false)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color; img.raycastTarget = raycast;
            return img;
        }
        public static RectTransform InkPanel(Transform parent, string name, float x, float y, float w, float h, Color? paper = null)
        {
            var rt = Rect(parent, name, x, y, w, h);
            Fill(rt, UiPalette.Ink);
            var inner = Rect(rt, "Paper", 2, 2, w - 4, h - 4);
            Fill(inner, paper ?? UiPalette.Paper);
            return rt;
        }
        public static TMP_Text Text(Transform parent, string name, string value, TMP_FontAsset font,
            float size, float x, float y, float w, float h, Color? color = null, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            var rt = Rect(parent, name, x, y, w, h);
            var text = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) text.font = font;
            text.text = value; text.fontSize = size; text.color = color ?? UiPalette.Text;
            text.alignment = align; text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            return text;
        }
        public static RectTransform InkPlate(Transform parent, string name, string label, TMP_FontAsset font,
            float x, float y, float w, float h)
        {
            var rt = Rect(parent, name, x, y, w, h); Fill(rt, UiPalette.Ink);
            Text(rt, "Label", label, font, 34, 14, 0, w - 28, h, UiPalette.LightText);
            return rt;
        }
        public static Button OutlineButton(Transform parent, string name, string label, TMP_FontAsset font,
            float x, float y, float w, float h, bool enabled = true)
        {
            var rt = InkPanel(parent, name, x, y, w, h);
            var image = rt.Find("Paper").GetComponent<Image>(); image.raycastTarget = true;
            var button = rt.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            button.transition = Selectable.Transition.None; button.interactable = enabled;
            Text(rt, "Label", label, font, 28, 6, 0, w - 12, h, UiPalette.Text, TextAlignmentOptions.Center);
            Select(button, false);
            if (!enabled)
            {
                image.color = UiPalette.Disabled;
                var tip = Rect(rt, "Tooltip", 0, 0, w, h);
                Fill(tip, UiPalette.Ink);
                Text(tip, "Label", "ยังไม่เปิดใช้งาน", font, 26, 0, 0, w, h, UiPalette.LightText, TextAlignmentOptions.Center);
                tip.gameObject.SetActive(false);
                rt.gameObject.AddComponent<InkTooltip>().Configure(tip.gameObject);
            }
            return button;
        }
        public static Button CloseButton(Transform parent, TMP_FontAsset font, float x, float y)
        {
            var rt = Rect(parent, "CloseButton", x, y, 48, 48);
            var ring = Fill(rt, UiPalette.Ink, true); ring.sprite = Circle;
            var inner = Rect(rt, "Paper", 2, 2, 44, 44); var image = Fill(inner, UiPalette.Paper); image.sprite = Circle;
            Text(rt, "Label", "X", font, 28, 0, 0, 48, 48, UiPalette.Text, TextAlignmentOptions.Center);
            var button = rt.gameObject.AddComponent<Button>(); button.targetGraphic = ring; button.transition = Selectable.Transition.None;
            return button;
        }
        public static void Select(Button button, bool selected)
        {
            var paper = button.transform.Find("Paper")?.GetComponent<Image>();
            if (paper != null) paper.color = selected ? UiPalette.Ink : UiPalette.Paper;
            var text = button.transform.Find("Label")?.GetComponent<TMP_Text>();
            if (text != null) text.color = selected ? UiPalette.LightText : UiPalette.Text;
        }
        public static TMP_Text StatRow(Transform parent, string name, string label, string value, TMP_FontAsset font,
            float x, float y, float w, Color accent)
        {
            var rt = Rect(parent, name, x, y, w, 48); Fill(rt, UiPalette.PaperDark);
            var dot = Rect(rt, "Dot", 10, 19, 10, 10); Fill(dot, accent);
            Text(rt, "Label", label, font, 26, 28, 0, w * .58f - 28, 48);
            return Text(rt, "Value", value, font, 26, w * .58f, 0, w * .42f - 10, 48, UiPalette.Text, TextAlignmentOptions.MidlineRight);
        }
        public static Image Seal(Transform parent, string name, float x, float y, Color accent)
        {
            var rt = Rect(parent, name, x, y, 22, 22);
            var image = Fill(rt, accent); image.sprite = Resources.Load<Sprite>("ui/ink/seal"); return image;
        }
        public static Sprite Load(string path)
        {
            if (!Sprites.TryGetValue(path, out var sprite))
            {
                sprite = Resources.Load<Sprite>(path); Sprites[path] = sprite;
                if (sprite == null && Warned.Add(path)) Debug.LogWarning("[InkUI] Missing Resources/" + path + "; using solid fallback.");
            }
            return sprite;
        }
        // Procedural, text-free primitive; generated once per domain, no external art or paid generation.
        public static Sprite Circle
        {
            get
            {
                if (circle != null) return circle;
                circle = Resources.Load<Sprite>("ui/ink/circle");
                if (circle != null) return circle;
                const int size = 64;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                var px = new Color32[size * size];
                for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(32, 32));
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(32 - distance) * 255));
                }
                tex.SetPixels32(px); tex.Apply();
                circle = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(.5f, .5f));
                circle.hideFlags = HideFlags.HideAndDontSave;
                return circle;
            }
        }
    }
}

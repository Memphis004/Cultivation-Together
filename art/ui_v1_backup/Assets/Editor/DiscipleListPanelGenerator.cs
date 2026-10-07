#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Xianxia.Sect.UI;

namespace Xianxia.Sect.EditorTools
{
    /// <summary>
    /// DiscipleList panel — builds the prefab from code + appends it to the
    /// MainPanelCatalog (idempotent). Same "UI prefab from code" convention as
    /// DiscipleDetailPanelGenerator / AvatarPrefabGenerator.
    ///
    /// Layout pass (scroll-window redesign, matches the reference art):
    /// HORIZONTAL window 1400x790 with the paper filling the middle, wooden
    /// scroll posts OUTSIDE the content area, clouds in the bottom corners,
    /// title plate centred on top, close medallion hanging from the top-LEFT
    /// post (outside the paper edge, on top of the wood). All numbers below are
    /// measured from the shipped sprites (Tools/art/measure_disciple_list_sprites.py),
    /// not eyeballed:
    ///
    /// - Window 1400x790 = 72.9% x 73.1% of the 1920x1080 reference canvas
    ///   (Canvas.prefab, ScaleWithScreenSize, match=height — confirmed from
    ///   Scenes/SampleScene.unity → Prefabs/Canvas.prefab). Fixed canvas units,
    ///   not proportional anchors, because the scroll_paper 9-slice borders are
    ///   fixed sprite pixels: the insets only line up at one window size.
    /// - scroll_paper 460x558, sliced with border (45, 22, 42, 20) l/b/r/t —
    ///   the bright usable paper starts at those insets (old manifest border
    ///   38/0/36/0 let the top ink line and bottom roll edge smear).
    /// - Rods: scroll_rod 546x40 sliced (20,0,20,0) → drawn as 72x850 vertical
    ///   posts (rotated 90°), centred at x=±690 so the posts cover 654..726,
    ///   i.e. fully OUTSIDE the card grid (max card x = ±614).
    /// - Clouds: cloud_corner 180x270 native in the bottom corners, 36 px below
    ///   the window edge like the reference.
    /// - Title plate: title_plate 320x54 sliced (26,0,24,0) → 460x66, title
    ///   text centred ON the plate (inset 26/24 to clear the end caps).
    /// - Close medallion: close_medallion 120x239 shown at 72x143 (native
    ///   aspect), solid disc face = alpha>200 bbox x 1..118, y 1..139 (measured
    ///   from the PNG) → rendered local y ≈ −12..+71, centre ≈ +30 above the
    ///   rect centre; the cord hangs below. The medallion ITSELF is the Button
    ///   (targetGraphic = the coin image) plus a transparent margin hit-area
    ///   child, and a "ปิด" label sits on the disc face.
    /// - Content inset = paper bright box + 24 px margin: L=69, T=120 (also
    ///   clears the title plate), R=66, B=46.
    ///
    /// เหมือนเดิม: โครงหลัก (title / close / scroll) ถูก serialize ครบหลัง
    /// import แต่ "ใบการ์ด" ไม่อยู่ใน prefab — DiscipleListView.CreateCard
    /// สร้างที่ runtime และ GridLayoutGroup ของ content ถูกสร้างทั้งที่นี่และ
    /// (defensively) ใน DiscipleListView.EnsureCardsLayout
    ///
    /// titleText ใช้ THSarabunPSK SDF (Thai-capable) — การ์ด inherit ฟอนต์นี้ผ่าน
    /// titleText.font เหมือน ResourcePopupView.CreateText (LiberationSans ไม่มีไทย)
    /// </summary>
    public static class DiscipleListPanelGenerator
    {
        private const string RootFolder = "Assets/Prefabs/UI";
        private const string CatalogPath = "Assets/Panel Catalog/MainPanelCatalog.asset";
        private const string ThaiFontPath = "Assets/Resources/Fonts/THSarabunPSK SDF.asset";

        /// <summary>Imported window-kit art. See Assets/Editor/UiSpriteImporter.cs.</summary>
        private const string ArtFolder = "Assets/Resources/ui/disciple_list";

        // ── layout constants (sources in the class header) ───────────────────
        private const float WindowW = 1400f; // 72.9% of the 1920 canvas width
        private const float WindowH = 790f;  // 73.1% of the 1080 canvas height
        private const float PaperInsetLeft = 69f;   // 45 (bright-box, measured) + 24 margin
        private const float PaperInsetRight = 66f;  // 42 (measured) + 24 margin
        private const float PaperInsetTop = 120f;   // 20 (measured) + 24 margin + title band
        private const float PaperInsetBottom = 46f; // 22 (measured) + 24 margin
        private const float PostWidth = 72f;        // scroll_rod native height 40 → 72 scaled
        private const float PostLength = 850f;      // window 790 + 60: tips poke past the paper
        private const float PostCenterX = 690f;     // inner edge 654 > card grid max 614
        private const float MedallionW = 72f;       // native 120x239 aspect kept
        private const float MedallionH = 143f;
        private const float MedallionX = -PostCenterX; // hangs ON the left post
        private const float MedallionY = 130f;         // disc just below the top edge

        /// <summary>The window's pre-art colour, kept as the fallback.</summary>
        private static readonly Color FallbackPaperColor = new Color(0.93f, 0.88f, 0.78f, 1f);

        // "log warning ครั้งเดียว": one warning per missing sprite name per editor
        // session, so re-running the generator never spams the console.
        private static readonly HashSet<string> WarnedMissingArt = new HashSet<string>();

        private static Sprite LoadArt(string name)
        {
            string path = ArtFolder + "/" + name + ".png";
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null && WarnedMissingArt.Add(name))
            {
                Debug.LogWarning("[DiscipleListPanelGenerator] art sprite missing: " + path +
                                 " — falling back (the panel still builds, just plainer).");
            }
            return sprite;
        }

        [MenuItem("Xianxia/Generate DiscipleList Panel")]
        public static void Generate()
        {
            EnsureFolder(RootFolder);

            var prefab = BuildPanel();
            UpdateCatalog(prefab);
            AssetDatabase.SaveAssets();
            Debug.Log("[DiscipleListPanelGenerator] DiscipleListPanel prefab + catalog entry ready at " + RootFolder);
        }

        private static GameObject BuildPanel()
        {
            // ── root ── (no ContentSizeFitter at root — AvatarCustomization lesson)
            var root = new GameObject("DiscipleListPanel", typeof(RectTransform), typeof(CanvasGroup));
            var rootRt = root.GetComponent<RectTransform>();
            StretchFull(rootRt);

            var backdrop = CreateChild(root.transform, "Backdrop", typeof(Image));
            StretchFull(backdrop.GetComponent<RectTransform>());
            backdrop.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

            var window = CreateChild(root.transform, "Window", typeof(Image));
            var windowRt = window.GetComponent<RectTransform>();
            windowRt.anchorMin = windowRt.anchorMax = new Vector2(0.5f, 0.5f);
            windowRt.pivot = new Vector2(0.5f, 0.5f);
            windowRt.sizeDelta = new Vector2(WindowW, WindowH);
            windowRt.anchoredPosition = Vector2.zero;

            // Paper fills the whole window as a sliced sprite. Sliced border
            // (45, 22, 42, 20) comes from the measured bright-paper insets —
            // the rolled ink edges stay fixed while the middle stretches.
            // If the sprite is missing we keep the flat colour, so the panel is
            // always usable (UiSpriteImporter can be re-run to fix it).
            var windowImage = window.GetComponent<Image>();
            var paperSprite = LoadArt("scroll_paper");
            if (paperSprite != null)
            {
                windowImage.sprite = paperSprite;
                windowImage.type = Image.Type.Sliced;
                windowImage.color = Color.white;
            }
            else
            {
                windowImage.color = FallbackPaperColor;
            }

            // Thai font — การ์ด inherit ผ่าน titleText.font (ResourcePopupView pattern)
            var thaiFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(ThaiFontPath);
            if (thaiFont == null)
                Debug.LogWarning("[DiscipleListPanelGenerator] Thai font not found at " + ThaiFontPath +
                                 " — cards would fall back to the default font (no Thai glyphs)");

            // ── decorations ──────────────────────────────────────────────────
            // Sibling order is deliberate: rods/clouds/plate are created BEFORE
            // the ScrollView (so cards draw on top of them) and the close
            // medallion is created AFTER (so it hangs visibly on the post).
            // Everything decorative is raycastTarget=false — only the close
            // button catches clicks.

            // Wooden scroll posts. The rod sprite is authored horizontal (546x40)
            // and rotated 90° here. GOTCHA (caught by the layout verify): the rect
            // must stay (length, thickness) = (850, 72) — after the 90° rotation
            // the WIDTH becomes the vertical extent, so (72, 850) renders as a
            // horizontal bar poking 349 px into the content. Sliced with border
            // (20,0,20,0) (measured end fittings) so the stretch bends nothing.
            // Length 850 = window 790 + 60 so the tips poke past the paper's
            // top/bottom edges like the reference art. Centres at x=±690: the
            // posts cover 654..726, entirely outside the card grid (±614).
            CreateSlicedArt(window.transform, "RodLeft", LoadArt("scroll_rod"),
                new Vector2(PostLength, PostWidth), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(-PostCenterX, 0f), 90f);
            CreateSlicedArt(window.transform, "RodRight", LoadArt("scroll_rod"),
                new Vector2(PostLength, PostWidth), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(PostCenterX, 0f), -90f);

            // Auspicious clouds in the two bottom corners at native size, hanging
            // 36 px below the window edge. The right one is mirrored with
            // scale.x = -1 so the pair reads as one motif.
            var cloudSize = new Vector2(180f, 270f); // cloud_corner native size
            CreateArt(window.transform, "CloudLeft", LoadArt("cloud_corner"),
                cloudSize, Vector2.zero, Vector2.zero,
                Vector2.zero, new Vector2(24f, -36f), 0f);
            // Mirroring gotcha (measured in the previous pass, same formula): with
            // pivot.x = 1 a negative localScale.x flips the rect about the pivot,
            // so the offset must be -(inset + width) to land the mirrored body
            // back inside the window hugging the right edge.
            var cloudRight = CreateArt(window.transform, "CloudRight", LoadArt("cloud_corner"),
                cloudSize, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(1f, 0f), new Vector2(-24f - cloudSize.x, -36f), 0f);
            cloudRight.rectTransform.localScale = new Vector3(-1f, 1f, 1f);

            // Letterless plaque centred on top, 460x66 (320x54 sliced, 23/21 px
            // end caps kept whole by the new (26,0,24,0) border).
            var titlePlate = CreateSlicedArt(window.transform, "TitlePlate", LoadArt("title_plate"),
                new Vector2(460f, 66f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -20f), 0f);

            // Title text ON the plate, centred (reference art: "รายการศิษย์" sits
            // mid-plate). Anchored to the plate's inner box — offsets 26/24 clear
            // the measured end caps, so the glyphs never ride on the curls.
            var title = CreateText(titlePlate.transform, "TitleText", "ศิษย์สำนัก", thaiFont);
            var titleRt = title.rectTransform;
            titleRt.anchorMin = Vector2.zero;
            titleRt.anchorMax = Vector2.one;
            titleRt.offsetMin = new Vector2(26f, 4f);   // left cap 26 (measured)
            titleRt.offsetMax = new Vector2(-24f, -4f); // right cap 24 (measured)
            title.fontSize = 40;
            title.alignment = TextAlignmentOptions.Center;

            // Content area = paper bright box + 24 px breathing margin, plus the
            // title band reserved at the top. ScrollView stretches to fill it.
            var contentArea = CreateChild(window.transform, "ContentArea");
            var areaRt = contentArea.GetComponent<RectTransform>();
            areaRt.anchorMin = Vector2.zero;
            areaRt.anchorMax = Vector2.one;
            areaRt.offsetMin = new Vector2(PaperInsetLeft, PaperInsetBottom);
            areaRt.offsetMax = new Vector2(-PaperInsetRight, -PaperInsetTop);

            // Scroll view (fills the content area)
            var scrollView = CreateChild(contentArea.transform, "ScrollView",
                typeof(Image), typeof(Mask), typeof(ScrollRect));
            var scrollRt = scrollView.GetComponent<RectTransform>();
            StretchFull(scrollRt);
            scrollView.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.04f);
            scrollView.GetComponent<Mask>().showMaskGraphic = false;
            var scrollRect = scrollView.GetComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;

            var viewport = CreateChild(scrollView.transform, "Viewport", typeof(Image), typeof(Mask));
            StretchFull(viewport.GetComponent<RectTransform>());
            viewport.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.01f);
            viewport.GetComponent<Mask>().showMaskGraphic = false;
            scrollRect.viewport = viewport.GetComponent<RectTransform>();

            // Content = cardsRoot — FitWidthGridLayoutGroup จะถูกเพิ่มซ้ำโดย
            // EnsureCardsLayout ถ้าหายตอน import (defensive, เหมือน ResourcePopup).
            // Grid params mirror DiscipleListView.GridColumns / GridSpacing /
            // CardAspect so the fallback can never disagree with the prefab.
            // Expected cell math (recorded, not hand-tuned per frame):
            // viewport 1265 → (1265 − 5×16) / 6 = 197 px cells, 253 tall at the
            // 290:225 card aspect — the group recomputes this itself every pass.
            var content = CreateChild(viewport.transform, "Content",
                typeof(FitWidthGridLayoutGroup), typeof(ContentSizeFitter));
            var contentRt = content.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = new Vector2(0f, 0f);

            var grid = content.GetComponent<FitWidthGridLayoutGroup>();
            grid.childAlignment = TextAnchor.UpperCenter;
            grid.padding = new RectOffset(0, 0, 0, 0);

            var fitter = content.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize; // ← ตัวเดียวที่อนุญาตใน prefab

            scrollRect.content = contentRt;

            // Close medallion, top-left, hanging ON the left post (centre
            // x = -690 = the post's centre; the medallion is created AFTER the
            // rods so it draws over the wood, like the reference). Its solid
            // disc face is the sprite's upper half (measured: alpha>200 bbox
            // x 1..118, y 1..139 of 120x239 → rendered local y ≈ −12..+71).
            var medallion = CreateArt(window.transform, "CloseMedallion", LoadArt("close_medallion"),
                new Vector2(MedallionW, MedallionH), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(MedallionX, MedallionY), 0f);

            // Task B — the COIN ITSELF is the button. Old build put an invisible
            // 110x110 hit area 57.2 px ABOVE centre, so clicks on the visible
            // disc landed on the Window and did nothing. Now: Button on the
            // medallion GameObject with targetGraphic = the coin image (checks
            // a), raycastTarget=true so every click on the art fires. The
            // transparent CloseHitArea child stays on top purely to widen the
            // hit box by a margin, centred on the art (fixes d): a click on it
            // bubbles up the hierarchy to the medallion's Button
            // (ExecuteEvents.GetEventHandler walks parents), so the effective
            // hit box = coin + margin, no dead zone above it.
            medallion.raycastTarget = true;
            var button = medallion.gameObject.AddComponent<Button>();
            button.targetGraphic = medallion;
            button.transition = Selectable.Transition.ColorTint; // hover/press feedback
            button.colors = new ColorBlock
            {
                normalColor = Color.white,
                highlightedColor = new Color(1f, 0.92f, 0.8f),
                pressedColor = new Color(0.85f, 0.8f, 0.7f),
                selectedColor = Color.white,
                disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f),
                colorMultiplier = 1f,
                fadeDuration = 0.08f,
            };

            var closeHit = CreateChild(medallion.transform, "CloseHitArea", typeof(Image));
            var closeRt = closeHit.GetComponent<RectTransform>();
            closeRt.anchorMin = closeRt.anchorMax = new Vector2(0.5f, 0.5f);
            closeRt.pivot = new Vector2(0.5f, 0.5f);
            closeRt.sizeDelta = new Vector2(96f, 160f); // coin 72x143 + ~12 px margin
            closeRt.anchoredPosition = Vector2.zero;
            var closeImg = closeHit.GetComponent<Image>();
            closeImg.color = new Color(1f, 1f, 1f, 0f); // invisible BUT raycastable
            closeImg.raycastTarget = true;

            // "ปิด" label on the disc face (disc centre ≈ +30 above the rect
            // centre, measured): cream on the rust-red coin so the affordance
            // is explicit. raycastTarget=false — clicks pass through to the art.
            var closeLabel = CreateText(medallion.transform, "CloseLabel", "ปิด", thaiFont);
            var labelRt = closeLabel.rectTransform;
            labelRt.anchorMin = labelRt.anchorMax = new Vector2(0.5f, 0.5f);
            labelRt.pivot = new Vector2(0.5f, 0.5f);
            labelRt.sizeDelta = new Vector2(64f, 36f);
            labelRt.anchoredPosition = new Vector2(0f, 30f);
            closeLabel.fontSize = 25;
            closeLabel.color = new Color(0.98f, 0.95f, 0.88f);

            // View component + serialized refs (title/close/scroll only —
            // cards are runtime-built by CreateCard, ResourcePopupView pattern)
            var view = root.AddComponent<DiscipleListView>();
            var so = new SerializedObject(view);
            so.FindProperty("titleText").objectReferenceValue = title;
            so.FindProperty("closeButton").objectReferenceValue = button; // the Button ON CloseMedallion
            so.FindProperty("cardsRoot").objectReferenceValue = contentRt;
            so.FindProperty("scrollRect").objectReferenceValue = scrollRect;
            so.ApplyModifiedPropertiesWithoutUndo();

            return SavePrefab(root, "DiscipleListPanel");
        }

        private static void UpdateCatalog(GameObject prefab)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<UIPanelCatalog>(CatalogPath);
            if (catalog == null)
            {
                Debug.LogError("[DiscipleListPanelGenerator] catalog asset not found: " + CatalogPath);
                return;
            }
            var so = new SerializedObject(catalog);
            var panels = so.FindProperty("panels");
            for (int i = 0; i < panels.arraySize; i++)
            {
                if (panels.GetArrayElementAtIndex(i).FindPropertyRelative("PanelId").stringValue == "DiscipleList")
                {
                    panels.DeleteArrayElementAtIndex(i); // refresh the entry (idempotent re-run)
                    break;
                }
            }
            panels.arraySize++;
            var el = panels.GetArrayElementAtIndex(panels.arraySize - 1);
            el.FindPropertyRelative("PanelId").stringValue = "DiscipleList";
            el.FindPropertyRelative("Prefab").objectReferenceValue = prefab;
            el.FindPropertyRelative("PresenterKind").enumValueIndex = (int)UIPresenterKind.DiscipleList;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static GameObject CreateChild(Transform parent, string name, params System.Type[] components)
        {
            var allComponents = new System.Type[components.Length + 1];
            allComponents[0] = typeof(RectTransform);
            components.CopyTo(allComponents, 1);
            var go = new GameObject(name, allComponents);
            go.transform.SetParent(parent, false);
            return go;
        }

        /// <summary>
        /// Creates an art Image as a pure decoration: never a raycast target, and
        /// disabled outright when the sprite is missing so a missing asset leaves no
        /// blank block on the panel.
        /// </summary>
        private static Image CreateArt(Transform parent, string name, Sprite sprite, Vector2 size,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition,
            float rotationZ)
        {
            var go = CreateChild(parent, name, typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPosition;
            if (rotationZ != 0f) rt.localRotation = Quaternion.Euler(0f, 0f, rotationZ);

            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
            img.enabled = sprite != null;
            return img;
        }

        /// <summary>
        /// Same as CreateArt but 9-slice: preserveAspect MUST be false (a sliced
        /// image with preserveAspect renders at native size) and the sliced type
        /// only makes sense when the sprite actually has borders — falls back to
        /// a plain stretch otherwise.
        /// </summary>
        private static Image CreateSlicedArt(Transform parent, string name, Sprite sprite, Vector2 size,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition,
            float rotationZ)
        {
            var img = CreateArt(parent, name, sprite, size,
                anchorMin, anchorMax, pivot, anchoredPosition, rotationZ);
            img.preserveAspect = false;
            if (sprite != null)
            {
                img.type = Image.Type.Sliced;
            }
            return img;
        }

        private static void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static TextMeshProUGUI CreateText(Transform parent, string name, string content, TMP_FontAsset font)
        {
            var go = CreateChild(parent, name, typeof(TextMeshProUGUI));
            var text = go.GetComponent<TextMeshProUGUI>();
            text.text = content;
            if (font != null) text.font = font;
            text.color = Color.black;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            return text;
        }

        private static GameObject SavePrefab(GameObject go, string prefabName)
        {
            string path = Path.Combine(RootFolder, prefabName + ".prefab").Replace("\\", "/");
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace("\\", "/");
            string folderName = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folderName);
        }
    }
}
#endif

---
title: Avatar Appearance
type: entity
sources:
  - UnityProject/Assets/Scripts/Shared/SectEconomyState.cs
  - UnityProject/Assets/Scripts/Shared/GameMessages.cs
  - UnityProject/Assets/Scripts/Data/AvatarPartPool.cs
  - UnityProject/Assets/Scripts/UI/Views/AvatarRenderer.cs
  - UnityProject/Assets/Scripts/UI/Presenters/AvatarCustomizationPresenter.cs
  - UnityProject/Assets/Scripts/UI/Views/AvatarCustomizationView.cs
  - UnityProject/Assets/Scripts/UI/Views/AvatarCustomizationViewData.cs
  - UnityProject/Assets/Scripts/UI/Views/AvatarOptionButton.cs
  - UnityProject/Assets/Scripts/Systems/SectStateProvider.cs
  - UnityProject/Assets/Scripts/Core/GameLifetimeScope.cs
  - UnityProject/Assets/Scripts/UI/Systems/UIBootstrap.cs
  - UnityProject/Assets/Scripts/Core/ChangeAvatarPartHandler.cs
related:
  - "[[entities/disciples|Disciples]]"
  - "[[concepts/mvp-ui|MVP UI Pattern]]"
  - "[[concepts/message-pipe-bus|MessagePipe Bus]]"
created: 2026-08-31
updated: 2026-09-02
confidence: high
tags: [avatar, parts, sprite-swap, head, hair, body, accessory, ugui, pooling, framing, draft-pattern]
---

# Avatar Appearance

> Per-disciple sprite customization via a **dictionary-based** structure. Which part each disciple wears is stored on `DiscipleState.Avatar` as an **AvatarAppearance** object containing `Parts` and `Colors` dictionaries (slot → id).
> Status: `implemented — v2 portrait-swap (dictionary schema)`

ทั้งระบบประกอบด้วย 4 ชั้น ทุกชั้น verify จากโค้ดจริงแล้ว (scan วันที่ 2026-09-01):

1. **State schema** — `AvatarAppearance` (dictionary) บน `DiscipleState.Avatar` + `AvatarSlots` constants/categories
2. **Data pool** — `AvatarPartPool` โหลด def-table จาก `Resources/Data/avatar_parts.json` (plain C# JSON, mirror แพทเทิร์นของ `LubanEventPool`)
3. **Rendering** — `AvatarRenderer` (UGUI, layered sprites, hair 2-layer, framing presets, polling ทุก 0.1s พร้อม signature guard + sprite cache + layer pooling)
4. **Customization UI** — `AvatarCustomizationPresenter` (draft/diff-commit/rollback/external-sync) + `AvatarCustomizationView` (category/slot tabs, option grid pooling) + `AvatarOptionButton`

Mutation ผ่าน choke point เดียวคือ `SectStateProvider.TryChangeAvatarPart()` แล้ว broadcast `AvatarEquipmentChangedMessage` — และเปิด write path ให้ AI Agent ผ่าน request/response `ChangeAvatarPartRequest/Response` ฝั่ง interprocess

## Data Schema

The Avatar is defined using a dictionary mapping slots to part IDs. รองรับ slot จำนวนเท่าไหร่ก็ได้ ไม่ต้องแก้ schema เมื่อเพิ่ม slot ใหม่

```csharp
[MessagePackObject]
public sealed class AvatarAppearance
{
    [Key(0)] public Dictionary<string, string> Parts { get; set; } = new Dictionary<string, string>();
    [Key(1)] public Dictionary<string, string> Colors { get; set; } = new Dictionary<string, string>();

    public string GetSlot(string slot)
    {
        string v;
        return Parts.TryGetValue(slot, out v) ? v : string.Empty;
    }

    public bool SetSlot(string slot, string partId)
    {
        if (string.IsNullOrEmpty(partId)) { Parts.Remove(slot); return true; }
        Parts[slot] = partId;
        return true;
    }
    // GetColor/SetColor/Clone/FromSlots ...
}
```
> 📎 Source: Assets/Scripts/Shared/SectEconomyState.cs

- `Parts`: slot → partId — **ค่าว่าง (`""`/ไม่มี key) = ใช้ default ของ slot นั้น** (`SetSlot` ด้วย partId ว่างจะ `Remove` key ออกจาก dict เลย)
- `Colors`: slot → colorId — มีเฉพาะ slot ที่ `tintable` (ยังไม่มี UI ให้แก้ แต่ schema พร้อม)
- `Clone()` ใช้สองที่: draft pattern ฝั่ง Presenter และการคืน copy จาก `TryChangeAvatarPart` (ไม่ปล่อย reference ของ live state หลุดออกไป)

สร้างครั้งเดียวหลาย slot ได้จาก factory `FromSlots` + struct `SlotPart` (เลี่ยง Value Tuple เพื่อความเข้ากันได้กับ Unity ทุกเวอร์ชัน):

```csharp
public struct SlotPart
{
    public string Slot;
    public string PartId;
    public SlotPart(string slot, string partId)
    {
        Slot = slot;
        PartId = partId;
    }
}

// usage ใน MockSectData:
Avatar = AvatarAppearance.FromSlots(
    new SlotPart(AvatarSlots.Body, "body_robe_azure"),
    new SlotPart(AvatarSlots.Head, "head_male_01"),
    new SlotPart(AvatarSlots.Hair, "hair_topknot_long"),
    new SlotPart(AvatarSlots.Accessory, "acc_jade_crown")
)
```
> 📎 Source: Assets/Scripts/Shared/SectEconomyState.cs, Assets/Scripts/Shared/MockSectData.cs

### Slot Constants & Category Tabs

ชื่อ slot เป็น const string ทั้งหมด + ตาราง map เป็นหมวดหมู่ (ภาษาไทย) สำหรับ tab ของ UI — Presenter/Renderer ไม่มีสตริง slot hardcode เองเลย

```csharp
public static class AvatarSlots
{
    public const string Base = "base";
    public const string Body = "body";
    public const string Head = "head";
    public const string Eyes = "eyes";
    public const string Brows = "brows";
    public const string Mouth = "mouth";
    public const string Nose = "nose";
    public const string Hair = "hair";
    public const string FaceMarking = "face_marking";
    public const string Eyeshadow = "eyeshadow";
    public const string Accessory = "accessory";

    public static readonly string[] Equippable =
    {
        Body, Head, Eyes, Brows, Mouth, Nose,
        Hair, FaceMarking, Eyeshadow, Accessory
    };

    public static readonly IReadOnlyDictionary<string, string[]> Categories =
        new Dictionary<string, string[]>
        {
            { "ใบหน้า", new[] { Head, Eyes, Brows, Mouth, Nose } }, // Head รวมอยู่ในใบหน้าด้วย
            { "ลักษณะ", new[] { Hair, FaceMarking, Eyeshadow } },
            { "ร่างกาย", new[] { Body, Accessory } },
        };
    // Labels: body="เสื้อผ้า", hair="ทรงผม", ...
}
```
> 📎 Source: Assets/Scripts/Shared/SectEconomyState.cs

`Equippable` (10 slots, ไม่รวม `base`) ใช้เป็นตัวขับใน diff-commit และ randomize; `base` เป็นโครงคงที่ที่ Renderer resolve เองเสมอ

## Data Pool

Avatar definitions are loaded from a JSON file into `AvatarPartPool`, which is registered as a Singleton in `GameLifetimeScope`. It handles loading and resolving missing parts to default parts — แพทเทิร์นเดียวกับ `LubanEventPool` (plain C# class, ไม่ใช่ ScriptableObject)

```csharp
public class AvatarPartPool
{
    private const string ResourcePath = "Data/avatar_parts";

    private readonly Dictionary<string, AvatarPartDef> _byId =
        new Dictionary<string, AvatarPartDef>();
    private readonly Dictionary<string, List<AvatarPartDef>> _bySlot =
        new Dictionary<string, List<AvatarPartDef>>();
    private readonly Dictionary<string, AvatarPartDef> _defaultBySlot =
        new Dictionary<string, AvatarPartDef>();

    public bool IsLoaded { get; private set; }

    public AvatarPartPool()
    {
        Load();
    }

    private void Load()
    {
        var asset = Resources.Load<TextAsset>(ResourcePath);
        // ... LogError ถ้าหาไม่เจอ ...
        AvatarPartTable table;
        try
        {
            table = JsonUtility.FromJson<AvatarPartTable>(asset.text);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[AvatarPartPool] JSON parse failed: {ex.Message}");
            return;
        }
        // index เป็น 3 dict: _byId / _bySlot / _defaultBySlot
        // duplicate partId → LogWarning + skip, isDefault แรกของแต่ละ slot ชนะ
        IsLoaded = true;
    }
}
```
> 📎 Source: Assets/Scripts/Data/AvatarPartPool.cs

Resolve มี fallback ติดกันสองชั้น — partId ว่างหรือหาไม่เจอจะตกไปที่ default ของ slot นั้น:

```csharp
/// <summary>Real resolve: partId empty or not found -> fallback to slot default</summary>
public AvatarPartDef Resolve(string slot, string partId)
{
    var def = GetById(partId);
    if (def != null && def.slot == slot) return def;
    return GetDefaultForSlot(slot);
}

/// <summary>Used when validating ChangeAvatarPartRequest</summary>
public bool IsValidForSlot(string slot, string partId)
{
    if (string.IsNullOrEmpty(partId)) return true;   // "" = default, always valid
    var def = GetById(partId);
    return def != null && def.slot == slot;
}
```
> 📎 Source: Assets/Scripts/Data/AvatarPartPool.cs

### Part Definition Fields

```csharp
[System.Serializable]
public class AvatarPartDef
{
    public string id;
    public string slot;
    public string category;       // "ใบหน้า" / "ลักษณะ" / "ร่างกาย"
    public string displayName;
    public string spritePath;     // path under Resources/ ; "" = empty layer
    public string spritePathBack; // ผมหลัง — ว่าง = ชิ้นนี้ไม่มี back layer (เช่นผมสั้น)
    public string thumbPath;      // thumbnail สำหรับกริด — ว่าง = ใช้ spritePath หลัก
    public int    drawOrder;
    public bool   isDefault;
    public bool   tintable;       // true = slot นี้รองรับ color selection
    public int drawOrderBack;  // 0 = ใช้ drawOrder (ผมหลังใช้ 10)
}
```
> 📎 Source: Assets/Scripts/Data/AvatarPartPool.cs

JSON: ทุกทรงผม `drawOrder: 40, drawOrderBack: 10` → stack ตรงสเปก ACPart4 เป๊ะ:
`base 0 → hair_back 10 → body 20 → head 30 → face_marking 34 → hair_front 40 → accessory 50`

## Rendering

`AvatarRenderer` เป็น UGUI `MonoBehaviour` (`RequireComponent(typeof(CanvasGroup))`) วาดตัวละครเป็น layered sprites — **ไม่ hardcode slot อีกต่อไป** loop ตาม `Parts.Keys` ทั้งหมด

### Framing Presets

art ชุดเดียว (canvas 1024×1536, จุดกึ่งกลางหัวต้องอยู่พิกัดเดียวกันทุกไฟล์) ใช้ได้ 3 บริบท — ควบคุมด้วย scale + offset ของ `layerRoot`:

```csharp
public enum AvatarFraming
{
    FullBody,   // ตัวเต็ม (Character Creation)
    Bust,       // ครึ่งตัว (Dialogue portrait)
    HeadIcon    // แค่หัว (HUD icon วงกลมเล็ก)
}

private static readonly AvatarFramingPreset[] FramingPresets = new AvatarFramingPreset[]
{
    new AvatarFramingPreset { mode = AvatarFraming.FullBody, pivotOffset = Vector2.zero,       scale = 1f   },
    new AvatarFramingPreset { mode = AvatarFraming.Bust,     pivotOffset = new Vector2(0, -200), scale = 2.2f },
    new AvatarFramingPreset { mode = AvatarFraming.HeadIcon, pivotOffset = new Vector2(0, -350), scale = 4.5f },
};

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
```
> 📎 Source: Assets/Scripts/UI/Views/AvatarRenderer.cs

เปลี่ยนโหมดระหว่าง runtime ด้วย `SetFraming(mode)` — apply preset ทันที ไม่ต้อง rebuild layers

### 0.1s Refresh Loop + Signature Guard

Renderer เป็น polling ไม่ใช่ event-driven (เพราะเป็น MonoBehaviour บน prefab ที่ Presenter จ่ายข้อมูลให้) — แต่กันค่าใช้จ่ายด้วย signature string:

```csharp
private const float RefreshInterval = 0.1f;

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

private static string BuildSignature(AvatarAppearance a)
{
    // Build from dictionary — sorted by key for stable comparison
    var keys = new List<string>(a.Parts.Keys);
    keys.Sort();
    // "body=body_robe_grey|hair=hair_short|head=head_male_01"
    // ...
}
```
> 📎 Source: Assets/Scripts/UI/Views/AvatarRenderer.cs

- `SetAppearance()` ตั้ง `_timer = RefreshInterval` เพื่อ trigger rebuild ใน frame ถัดไปทันที (ไม่รอ 0.1s)
- Sprite cache **cache แม้ผลเป็น null** เพื่อไม่ให้ `Resources.Load` รัวทุก 0.1s ตอน path พัง

### Rebuild — Layer Sorting + hair_back/hair_front Split

```csharp
private void Rebuild()
{
    ReleaseAllLayers();

    // 1) สร้าง list ของ (DrawOrder, SpritePath) ทั้งหมดที่จะวาด
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

    // 3) Spawn Image ตามลำดับที่ sort แล้ว — sibling index กำหนด z-order
    //    hair_back (10) spawn ก่อน body (20) -> อยู่ข้างหลัง
    //    hair_front (40) spawn หลัง head (30) -> อยู่ข้างหน้า
    for (int i = 0; i < layersToDraw.Count; i++)
    {
        var layer = layersToDraw[i];
        var sprite = LoadSprite(layer.path);
        if (sprite == null) continue;

        var img = RentLayer();
        img.sprite = sprite;
        img.enabled = true;
    }
}
```
> 📎 Source: Assets/Scripts/UI/Views/AvatarRenderer.cs

เลเยอร์ทั้งหมดถูก rent จาก `Stack<Image>` pool (`RentLayer`/`ReleaseAllLayers`) — ไม่มี Instantiate/Destroy ระหว่าง runtime หลัง warm-up:

```csharp
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
```
> 📎 Source: Assets/Scripts/UI/Views/AvatarRenderer.cs

`AvatarPartPool` ถูกจ่ายเข้ามาทาง `Initialize(pool)` (Renderer เป็น MonoBehaviour บน prefab resolve DI เองไม่ได้) — Presenter เป็นคนเรียกให้ตอน `OnOpen`

## Messages & Interprocess Wire Types

ทั้งหมดอยู่ใน `GameMessages.cs` (sync ตรงกันทั้งฝั่ง Unity และ McpBridge — เช็คด้วย diff แล้วเหมือนกันทุกไฟล์):

```csharp
// ---------- Avatar equipment change ----------
// Broadcast after a successful avatar part change (pub/sub, goes to UI + MCP client)
[MessagePackObject]
public class AvatarEquipmentChangedMessage
{
    [Key(0)] public string DiscipleId { get; set; } = string.Empty;
    [Key(1)] public string Slot { get; set; } = string.Empty;   // AvatarSlots.*
    [Key(2)] public string OldPartId { get; set; } = string.Empty;
    [Key(3)] public string NewPartId { get; set; } = string.Empty;
}

// Request/response pair for changing an avatar part.
// Request-response (not fire-and-forget) because the bridge needs
// to know immediately whether the PartId is valid or the disciple
// id is wrong - same reasoning as PurchaseItemRequest.
[MessagePackObject]
public class ChangeAvatarPartRequest
{
    [Key(0)] public string DiscipleId { get; set; } = string.Empty;
    [Key(1)] public string Slot { get; set; } = string.Empty;
    [Key(2)] public string PartId { get; set; } = string.Empty;   // "" = back to default
}

[MessagePackObject]
public class ChangeAvatarPartResponse
{
    [Key(0)] public bool Success { get; set; }
    [Key(1)] public string FailReason { get; set; } = string.Empty;
    [Key(2)] public AvatarAppearance ResultAvatar { get; set; }
}
```
> 📎 Source: Assets/Scripts/Shared/GameMessages.cs

- Topic ของ pub/sub channel: `InterprocessTopics.AvatarEquipmentChanged = "sect.avatar_equipment_changed"`
- ปลายทางของ request ฝั่ง Unity คือ `ChangeAvatarPartHandler`:

```csharp
public class ChangeAvatarPartHandler : IAsyncRequestHandler<ChangeAvatarPartRequest, ChangeAvatarPartResponse>
{
    private readonly ISectStateProvider _stateProvider;

    public UniTask<ChangeAvatarPartResponse> InvokeAsync(ChangeAvatarPartRequest request, CancellationToken cancellationToken = default)
    {
        var success = _stateProvider.TryChangeAvatarPart(
            request.DiscipleId, request.Slot, request.PartId,
            out var failReason, out var resultAvatar);

        return UniTask.FromResult(new ChangeAvatarPartResponse
        {
            Success = success,
            FailReason = failReason,
            ResultAvatar = resultAvatar
        });
    }
}
```
> 📎 Source: Assets/Scripts/Core/ChangeAvatarPartHandler.cs

**ข้อจำกัดปัจจุบัน (บันทึกตามจริง)**: ฝั่ง Unity register wire เรียบร้อย (ดู DI block ด้านล่าง) แต่ `McpBridge/Program.cs` **ยังไม่มี MCP tool** `change_avatar_part` — เครื่องมือที่ bridge เปิดให้ AI GM ตอนนี้คือ `get_sect_state`, `await_next_world_event`, `execute_decision`, `purchase_item` เท่านั้น แต่ `AvatarEquipmentChangedMessage` เดินสาย interprocess แล้ว และ snapshot ของ `get_sect_state` รวม `Avatar` (dictionary) ของทุกศิษย์มาให้ AI อ่านอยู่แล้ว

## Customization Flow (MVP UI)

### Payload & View DTOs

```csharp
/// <summary>payload ที่ส่งเข้า UIService.Open("AvatarCustomization", payload)</summary>
public sealed class AvatarCustomizationPayload
{
    public string DiscipleId;

    public AvatarCustomizationPayload(string discipleId)
    {
        DiscipleId = discipleId;
    }
}

/// <summary>ข้อมูลปุ่มแท็บ category (ใบหน้า / ลักษณะ / ร่างกาย)</summary>
public struct AvatarCategoryTabViewData
{
    public string Category;      // "ใบหน้า", "ลักษณะ", "ร่างกาย"
    public string DisplayName;
    public bool   IsActive;
}

/// <summary>ข้อมูลปุ่มแท็บ slot (body / head / hair / accessory)</summary>
public struct AvatarSlotTabViewData
{
    public string Slot;          // AvatarSlots.*
    public string DisplayName;   // "เสื้อผ้า", "ใบหน้า", ...
    public bool   IsActive;
}

/// <summary>ข้อมูลปุ่มชิ้นส่วน 1 ชิ้นในกริดด้านขวา</summary>
public struct AvatarPartOptionViewData
{
    public string PartId;        // "" = ถอด/ใช้ default
    public string DisplayName;
    public string SpritePath;    // path ใต้ Resources/ สำหรับ thumbnail
    public bool   IsSelected;
    public bool   IsLocked;      // เผื่ออนาคต: ปลดล็อกด้วย contribution
}
```
> 📎 Source: Assets/Scripts/UI/Views/AvatarCustomizationViewData.cs

### Presenter — Draft / Diff-Commit / Rollback / External Sync

`AvatarCustomizationPresenter : UIPresenter<AvatarCustomizationView>` เป็น plain C# class ลงทะเบียนแบบ **Transient** — instance ใหม่ทุกครั้งที่เปิด panel, ห้ามมี `Update()` ของตัวเอง, mutation เรียก provider ตรง ๆ ไม่อ้อม pub/sub

ตอน `OnOpen` — clone appearance จริงเป็น `_original` (snapshot) + `_draft` (ร่างที่ผู้เล่นแก้) แล้ว subscribe message เพื่อรอ external sync:

```csharp
public override void OnOpen(object args)
{
    var p = args as AvatarCustomizationPayload;
    if (p == null || string.IsNullOrEmpty(p.DiscipleId))
    {
        Debug.LogError("[AvatarCustomizationPresenter] payload ว่างหรือไม่มี DiscipleId");
        return;
    }
    _discipleId = p.DiscipleId;

    var state = _stateProvider.BuildSectEconomyState();
    var disciple = state.Disciples.Find(d => d.DiscipleId == _discipleId);
    // ... ไม่เจอศิษย์ -> LogError + return ...

    if (disciple.Avatar == null) disciple.Avatar = new AvatarAppearance();
    _original = disciple.Avatar.Clone();
    _draft    = disciple.Avatar.Clone();

    // renderer ใน preview ต้องได้ pool เพราะมัน resolve DI เองไม่ได้ (เป็น MonoBehaviour บน prefab)
    if (View.PreviewRenderer != null)
        View.PreviewRenderer.Initialize(_partPool);

    _subscription = _avatarChangedSub.Subscribe(OnAvatarChangedExternally);

    View.SetHeader("ปรับแต่งรูปลักษณ์", disciple.DisplayName);
    View.ShowError(null);
    RefreshAll();
}
```
> 📎 Source: Assets/Scripts/UI/Presenters/AvatarCustomizationPresenter.cs

The `OnConfirm` diff-commit + rollback pattern — commit **เฉพาะ slot ที่เปลี่ยน** เทียบ draft vs original ทีละ slot; ถ้า slot ไหน fail กลางทาง ต้องย้อน slot ที่ผ่านไปแล้วทั้งหมด ไม่งั้นตัวละครกลายเป็นลูกผสมครึ่ง ๆ:

```csharp
private void OnConfirm()
{
    if (!IsDirty()) { RequestClose(); return; }

    _isCommitting = true;
    var applied = new List<string>();   // slot ที่ commit ผ่านแล้ว (ไว้ rollback)
    string failReason = string.Empty;
    bool ok = true;

    try
    {
        for (int i = 0; i < AvatarSlots.Equippable.Length; i++)
        {
            string slot   = AvatarSlots.Equippable[i];
            string newVal = _draft.GetSlot(slot);
            if (newVal == _original.GetSlot(slot)) continue;   // ไม่เปลี่ยน = ข้าม

            AvatarAppearance result;
            string reason;
            if (_stateProvider.TryChangeAvatarPart(_discipleId, slot, newVal,
                                                   out reason, out result))
            {
                applied.Add(slot);
            }
            else
            {
                ok = false;
                failReason = reason;
                break;
            }
        }

        if (!ok)
        {
            // rollback slot ที่ผ่านไปแล้ว ไม่งั้นตัวละครจะกลายเป็นลูกผสมครึ่งๆ
            for (int i = 0; i < applied.Count; i++)
            {
                AvatarAppearance dummy; string dummyReason;
                _stateProvider.TryChangeAvatarPart(
                    _discipleId, applied[i], _original.GetSlot(applied[i]),
                    out dummyReason, out dummy);
            }
            _draft = _original.Clone();
            View.RenderPreview(_draft);
            RefreshOptions();
            View.SetDirty(false);
            View.ShowError("บันทึกไม่สำเร็จ: " + failReason);
            return;
        }

        _original = _draft.Clone();
    }
    finally
    {
        _isCommitting = false;
    }

    RequestClose();
}
```
> 📎 Source: Assets/Scripts/UI/Presenters/AvatarCustomizationPresenter.cs

External sync — ถ้า AI Agent เปลี่ยนหน้าตาผ่าน MCP ขณะ panel เปิดอยู่ ให้ sync ตามโดยไม่ทับงานที่ผู้เล่นแก้ค้างไว้ (`_isCommitting` เป็น guard กัน echo ของ commit ตัวเอง):

```csharp
private void OnAvatarChangedExternally(AvatarEquipmentChangedMessage msg)
{
    if (_isCommitting) return;                 // echo ของตัวเอง
    if (msg.DiscipleId != _discipleId) return; // คนละคน

    var state = _stateProvider.BuildSectEconomyState();
    var disciple = state.Disciples.Find(d => d.DiscipleId == _discipleId);
    if (disciple == null || disciple.Avatar == null) return;

    _original = disciple.Avatar.Clone();

    // ถ้าผู้เล่นยังไม่ได้แก้อะไรค้างไว้ → sync ตามเลย
    // ถ้าแก้ค้างอยู่ → sync เฉพาะ slot ที่ผู้เล่นไม่ได้แตะ (ไม่ทับงานผู้เล่น)
    if (_draft.GetSlot(msg.Slot) == msg.OldPartId)
        _draft.SetSlot(msg.Slot, msg.NewPartId);

    View.RenderPreview(_draft);
    RefreshOptions();
    View.SetDirty(IsDirty());
}
```
> 📎 Source: Assets/Scripts/UI/Presenters/AvatarCustomizationPresenter.cs

- `OnCancel`: ไม่ต้อง rollback อะไรเลย เพราะ draft pattern ไม่เคยแตะ state จริงจนกว่าจะกด confirm — แค่ `_draft = _original.Clone()` แล้วปิด panel
- `OnRandomize`: สุ่มทุก slot ใน `AvatarSlots.Equippable` จาก `_partPool.GetPartsForSlot(slot)`
- `OnPartOptionClicked`: แก้ `_draft` เท่านั้น → `RenderPreview` ทันที (ไม่รอ commit) → ย้ายกรอบ selected → `SetDirty(IsDirty())` เปิดปุ่ม confirm
- Category tab → default `_activeSlot` เป็น slot แรกของ category ใหม่ (`CategoryOrder = { "ใบหน้า", "ลักษณะ", "ร่างกาย" }`)
- `RefreshOptions` ให้ `IsSelected = (d.id == selected) || (string.IsNullOrEmpty(selected) && d.isDefault)` — ค่าว่างใน draft = default ก็ติดกรอบ selected ด้วย; `IsLocked = false` เป็น hook ระบบปลดล็อกอนาคต
- Thumbnail resolve ผ่าน `_thumbCache` ที่ Presenter (cache แม้ null) — View ไม่โหลด Resources เอง

### View — Category/Slot Tabs + Option Grid Pooling

`AvatarCustomizationView : UIViewBase` — View layer only: วาด / รับคลิก / ยิง event ออก (`CategoryTabClicked`, `SlotTabClicked`, `PartOptionClicked`, `ConfirmClicked`, `CancelClicked`, `RandomizeClicked`), ไม่มี if ทาง business logic ใช้ Object Pooling กับปุ่มในกริดตั้งแต่แรก (บทเรียนจาก LogWindowView — สลับแท็บเร็ว ๆ ถ้า Instantiate/Destroy รัว ๆ GC จะกระตุก):

```csharp
/// <summary>spriteResolver = ให้ Presenter/Pool เป็นคนแปลง path → Sprite (View ไม่โหลดเอง)</summary>
public void RenderOptions(IList<AvatarPartOptionViewData> options,
                          Func<string, Sprite> spriteResolver)
{
    ReleaseOptions();
    for (int i = 0; i < options.Count; i++)
    {
        var data   = options[i];
        var sprite = spriteResolver != null ? spriteResolver(data.SpritePath) : null;
        var btn    = RentOption();
        btn.Bind(data, sprite, HandleOptionClicked);
    }
}

private AvatarOptionButton RentOption()
{
    AvatarOptionButton b = _optionPool.Count > 0
        ? _optionPool.Pop()
        : Instantiate(optionButtonPrefab, optionGridRoot);
    b.transform.SetAsLastSibling();          // รักษาลำดับตาม def-table
    b.gameObject.SetActive(true);
    _activeOptions.Add(b);
    return b;
}
```
> 📎 Source: Assets/Scripts/UI/Views/AvatarCustomizationView.cs

Pool เดียวกันมี 3 ชุด: `_optionPool` (AvatarOptionButton), `_slotTabPool` + `_catTabPool` (Button) — ทุกอันเรียก `onClick.RemoveAllListeners()` ตอน release กัน closure ค้าง และ `OnDestroy` เคลียร์ event ทั้งหมดกัน presenter เก่า (Transient) ถูกอ้างค้าง ปุ่ม tab ที่เลือกใช้สีทอง `new Color(1f, 0.85f, 0.45f)`

Closure safety ทำที่ปุ่มแต่ละตัว — capture เป็น local variable ก่อนยิง event กลับ:

```csharp
public void Bind(AvatarPartOptionViewData data, Sprite sprite, Action<string> onClick)
{
    _partId  = data.PartId;
    _onClick = onClick;

    if (label != null) label.text = data.DisplayName;

    if (thumbnail != null)
    {
        thumbnail.sprite  = sprite;
        thumbnail.enabled = sprite != null;   // "ไม่สวม" ไม่มีรูป → ซ่อน Image
    }

    if (selectedFrame != null) selectedFrame.SetActive(data.IsSelected);
    if (lockedOverlay != null) lockedOverlay.SetActive(data.IsLocked);

    button.interactable = !data.IsLocked;
}

private void OnDestroy()
{
    button.onClick.RemoveListener(HandleClick);
    _onClick = null;                       // กัน presenter เก่าค้างใน closure
}
```
> 📎 Source: Assets/Scripts/UI/Views/AvatarOptionButton.cs

Slot tab ของ `AvatarCustomizationView` ใช้แพทเทิร์นเดียวกัน (`string captured = data.Slot;` ก่อน closure) — ดู `RenderCategoryTabs`/`RenderSlotTabs` ในไฟล์เดียวกัน

## State Mutation Choke Point

`TryChangeAvatarPart` safely validates and persists avatar changes, broadcasting `AvatarEquipmentChangedMessage` upon success. Signature อยู่บน `ISectStateProvider` (TimeSystem.cs) เพื่อให้ UI, handler และ test ผูกกับ interface:

```csharp
// ISectStateProvider
bool TryChangeAvatarPart(string discipleId, string slot, string partId,
                        out string failReason, out AvatarAppearance result);
```
> 📎 Source: Assets/Scripts/Core/TimeSystem.cs

```csharp
public bool TryChangeAvatarPart(string discipleId, string slot, string partId,
                                out string failReason, out AvatarAppearance result)
{
    failReason = string.Empty;
    result = null;

    var disciple = _state.Disciples.FirstOrDefault(d => d.DiscipleId == discipleId);
    if (disciple == null) { failReason = $"No disciple with id: {discipleId}"; return false; }

    if (System.Array.IndexOf(AvatarSlots.Equippable, slot) < 0)
    { failReason = $"Invalid slot: {slot}"; return false; }

    if (!_avatarPartPool.IsValidForSlot(slot, partId))
    { failReason = $"PartId '{partId}' is not valid for slot '{slot}'"; return false; }

    if (disciple.Avatar == null) disciple.Avatar = new AvatarAppearance();

    var oldPart = disciple.Avatar.GetSlot(slot);
    disciple.Avatar.SetSlot(slot, partId);

    _avatarChangedPublisher.Publish(new AvatarEquipmentChangedMessage
    {
        DiscipleId = discipleId,
        Slot       = slot,
        OldPartId  = oldPart,
        NewPartId  = partId
    });

    result = disciple.Avatar.Clone();   // return copy, not reference to live state
    return true;
}
```
> 📎 Source: Assets/Scripts/Systems/SectStateProvider.cs

Validation ลำดับเดียวกัน 3 ชั้น: มีศิษย์ → slot อยู่ใน `Equippable` → partId valid สำหรับ slot (ผ่าน `AvatarPartPool.IsValidForSlot`; `""` = default เสมอ)

ศิษย์ใหม่ที่ recruit ได้ starter avatar ทันที (สุ่มจาก roster index — ผลลัพธ์ deterministic ต่อลำดับการรับ):

```csharp
private AvatarAppearance CreateStarterAvatar(int rosterIndex)
{
    var a = new AvatarAppearance();
    a.SetSlot(AvatarSlots.Body, "body_robe_grey");
    a.SetSlot(AvatarSlots.Head, (rosterIndex % 2 == 0) ? "head_male_01" : "head_female_01");
    a.SetSlot(AvatarSlots.Hair, StarterHair[rosterIndex % StarterHair.Length]);
    return a;
}
```
> 📎 Source: Assets/Scripts/Systems/SectStateProvider.cs

## DI Registration

ใน `GameLifetimeScope.Configure` — pool เป็น Singleton, presenter เป็น Transient, และ wire ครบทั้ง request/response handler + pub/sub broker บนสาย interprocess:

```csharp
// Avatar part definitions loaded from Resources/Data/avatar_parts.json
builder.Register<AvatarPartPool>(Lifetime.Singleton);

// --- UI (Xianxia.UI.MVP Lite) ---
builder.Register<Xianxia.Sect.UI.LogWindowPresenter>(Lifetime.Transient);
builder.Register<Xianxia.Sect.UI.AvatarCustomizationPresenter>(Lifetime.Transient);
builder.RegisterEntryPoint<Xianxia.Sect.UI.WorldEventUISystem>(Lifetime.Singleton);
builder.RegisterEntryPoint<Xianxia.Sect.UI.UIBootstrap>(Lifetime.Singleton);
// ... MessagePipe setup ...
// Request/response: changing a disciple's avatar part
messagePipeBuilder.RegisterTcpRemoteRequestHandler<ChangeAvatarPartRequest, ChangeAvatarPartResponse>(interprocess);
builder.RegisterAsyncRequestHandler<ChangeAvatarPartRequest, ChangeAvatarPartResponse, ChangeAvatarPartHandler>(options);

// Interprocess pub/sub: broadcast avatar equipment changes to UI + MCP client
messagePipeBuilder.RegisterTcpInterprocessMessageBroker<string, AvatarEquipmentChangedMessage>(interprocess);
```
> 📎 Source: Assets/Scripts/Core/GameLifetimeScope.cs

`UIBootstrap` เปิด panel นี้ตอนเกมเริ่มเพื่อทดสอบ (ศิษย์ d001):

```csharp
public void Start()
{
    _uiService.Open("ResourceHud");
    _uiService.Open("LogWindow");
    // ทดสอบเปิดหน้าจอแต่งตัวศิษย์ d001
    _uiService.Open("AvatarCustomization", new AvatarCustomizationPayload("d001"));
}
```
> 📎 Source: Assets/Scripts/UI/Systems/UIBootstrap.cs

## Assets & Layout

- Data: `Assets/Resources/Data/avatar_parts.json` (TextAsset → `JsonUtility.FromJson<AvatarPartTable>`)
- Sprites/thumbnails: path ใน JSON ชี้ใต้ `Resources/` (โหลดด้วย `Resources.Load<Sprite>`)
- Panel layout: `UIRoot.ApplyLayout()` จับ prefab ชื่อขึ้นต้น `AvatarCustomization` → `ApplyAvatarCustomizationLayout()` — full-screen modal กลางจอ 900×620, ContentSizeFitter ต้อง Unconstrained

## Related Pages

- [[entities/disciples|Disciples]] — เจ้าของ Avatar field
- [[concepts/mvp-ui|MVP UI Pattern]] — framework ที่ panel นี้สร้างบน
- [[concepts/message-pipe-bus|MessagePipe Bus]] — channel/serialization
- [[concepts/state-management|State Management]] — single live instance
- [[sources/avatar-appearance|Avatar Customization (Sprite Swap)]] — design source doc

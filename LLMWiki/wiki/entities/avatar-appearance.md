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
> Status: `implemented — v3 portrait-swap + outfit packages`

ระบบประกอบด้วย 4 ชั้น ทุกชั้นอัพเดตตามโค้ดจริงแล้ว (scan วันที่ 2026‑09‑01):
1. **State schema** — `AvatarAppearance` (dictionary) บน `DiscipleState.Avatar` + `AvatarSlots` constants/categories
2. **Data pool** — `AvatarPartPool` โหลด def‑table จาก `Resources/Data/avatar_parts.json` (plain C# JSON, mirror แพทเทิร์นของ `LubanEventPool`)
3. **Rendering** — `AvatarRenderer` (UGUI, layered sprites, hair 2‑layer, framing presets, polling ทุก 0.1s พร้อม signature guard + sprite cache + layer pooling)
4. **Customization UI** — `AvatarCustomizationPresenter` (draft/diff‑commit/rollback/external‑sync) + `AvatarCustomizationView` (category/slot tabs, option grid pooling) + `AvatarOptionButton`

Mutation ผ่าน choke point เดียวคือ `SectStateProvider.TryChangeAvatarPart()` และใหม่ `SectStateProvider.TryApplyOutfit()` แล้ว broadcast `AvatarEquipmentChangedMessage` / `AvatarOutfitChangedMessage` — และเปิด write path ให้ AI Agent ผ่าน request/response `ChangeAvatarPartRequest/Response` และ (future) `ApplyOutfitRequest/Response` ฝั่ง interprocess

## Data Schema
The Avatar is defined using a dictionary mapping slots to part IDs. รองรับ slot จำนวนเท่าไหร่ก็ได้ ไม่ต้องแก้ schema เมื่อเพิ่ม slot ใหม่
```csharp
[MessagePackObject]
public sealed class AvatarAppearance
{
    [Key(0)] public Dictionary<string, string> Parts { get; set; } = new Dictionary<string, string>();
    [Key(1)] public Dictionary<string, string> Colors { get; set; } = new Dictionary<string, string>();
    [Key(2)] public string PoseId { get; set; } = string.Empty;   // "" = default pose (pose_idle_01)
    public string GetSlot(string slot) => Parts.TryGetValue(slot, out var v) ? v : string.Empty;
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

### Slot Constants & Category Tabs
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

`Equippable` (10 slots, ไม่รวม `base`) ใช้เป็นตัวขับใน diff‑commit และ randomize; `base` เป็นโครงคงที่ที่ Renderer resolve เองเสมอ

## Data Pool
Avatar definitions are loaded from a JSON file into `AvatarPartPool`, which is registered as a Singleton in `GameLifetimeScope`. It handles loading and resolving missing parts to default parts — แพทเทิร์นเดียวกับ `LubanEventPool` (plain C# class, ไม่ใช่ ScriptableObject)
```csharp
public class AvatarPartPool
{
    private const string ResourcePath = "Data/avatar_parts";
    private readonly Dictionary<string, AvatarPartDef> _byId = new Dictionary<string, AvatarPartDef>();
    private readonly Dictionary<string, List<AvatarPartDef>> _bySlot = new Dictionary<string, List<AvatarPartDef>>();
    private readonly Dictionary<string, AvatarPartDef> _defaultBySlot = new Dictionary<string, AvatarPartDef>();
    public bool IsLoaded { get; private set; }
    public AvatarPartPool() { Load(); }
    private void Load()
    {
        var asset = Resources.Load<TextAsset>(ResourcePath);
        AvatarPartTable table;
        try { table = JsonUtility.FromJson<AvatarPartTable>(asset.text); }
        catch (System.Exception ex) { Debug.LogError($"[AvatarPartPool] JSON parse failed: {ex.Message}"); return; }
        // index into _byId / _bySlot / _defaultBySlot
        // duplicate partId → LogWarning + skip, first default wins per slot
        IsLoaded = true;
    }
    // Resolve, GetById, GetDefaultForSlot, IsValidForSlot, GetPartsForSlot etc.
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

### 0.1s Refresh Loop + Signature Guard
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

private bool IsVisible() => isActiveAndEnabled && _canvasGroup.alpha > 0.01f;

private static string BuildSignature(AvatarAppearance a)
{
    var keys = new List<string>(a.Parts.Keys);
    keys.Sort();
    var sb = new System.Text.StringBuilder();
    for (int i = 0; i < keys.Count; i++)
    {
        if (i > 0) sb.Append("|");
        sb.Append(keys[i]).Append("=").Append(a.Parts[keys[i]]);
    }
    return sb.ToString();
}
```
> 📎 Source: Assets/Scripts/UI/Views/AvatarRenderer.cs

### Rebuild — Layer Sorting + hair_back/hair_front Split
```csharp
private void Rebuild()
{
    ReleaseAllLayers();
    var layersToDraw = new List<(int order, string path)>(16);
    // Base layer (always present)
    var baseDef = _pool.Resolve(AvatarSlots.Base, string.Empty);
    if (baseDef != null) layersToDraw.Add((baseDef.drawOrder, baseDef.spritePath));
    // Parts
    foreach (var kvp in _appearance.Parts)
    {
        var def = _pool.Resolve(kvp.Key, kvp.Value);
        if (def == null) continue;
        // back layer (e.g., hair back)
        if (!string.IsNullOrEmpty(def.spritePathBack))
        {
            int backOrder = def.drawOrderBack > 0 ? def.drawOrderBack : (def.drawOrder - 30);
            layersToDraw.Add((backOrder, def.spritePathBack));
        }
        // front layer
        if (!string.IsNullOrEmpty(def.spritePath))
            layersToDraw.Add((def.drawOrder, def.spritePath));
    }
    layersToDraw.Sort((a, b) => a.order.CompareTo(b.order));
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

### Layer Pooling
```csharp
private Image RentLayer()
{
    var img = _layerPool.Count > 0 ? _layerPool.Pop() : Instantiate(layerPrefab, layerRoot);
    img.transform.SetAsLastSibling();
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
All messaging lives in `GameMessages.cs`.
```csharp
// ---------- Avatar equipment change ----------
[MessagePackObject]
public class AvatarEquipmentChangedMessage
{
    [Key(0)] public string DiscipleId { get; set; } = string.Empty;
    [Key(1)] public string Slot { get; set; } = string.Empty;   // AvatarSlots.*
    [Key(2)] public string OldPartId { get; set; } = string.Empty;
    [Key(3)] public string NewPartId { get; set; } = string.Empty;
}

// ---------- Outfit change (new) ----------
[MessagePackObject]
public class AvatarOutfitChangedMessage
{
    [Key(0)] public string DiscipleId { get; set; } = string.Empty;
    [Key(1)] public string OutfitId { get; set; } = string.Empty;
    [Key(2)] public string PoseId { get; set; } = string.Empty;
}

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

*Topic*: `InterprocessTopics.AvatarEquipmentChanged = "sect.avatar_equipment_changed"`
*Topic*: `InterprocessTopics.AvatarOutfitChanged = "sect.avatar_outfit_changed"` (future)

`ChangeAvatarPartHandler` implements request/response.
```csharp
public class ChangeAvatarPartHandler : IAsyncRequestHandler<ChangeAvatarPartRequest, ChangeAvatarPartResponse>
{
    private readonly ISectStateProvider _stateProvider;
    public UniTask<ChangeAvatarPartResponse> InvokeAsync(ChangeAvatarPartRequest request, CancellationToken cancellationToken = default)
    {
        var success = _stateProvider.TryChangeAvatarPart(
            request.DiscipleId, request.Slot, request.PartId,
            out var failReason, out var resultAvatar);
        return UniTask.FromResult(new ChangeAvatarPartResponse { Success = success, FailReason = failReason, ResultAvatar = resultAvatar });
    }
}
```
> 📎 Source: Assets/Scripts/Core/ChangeAvatarPartHandler.cs

## Customization Flow (MVP UI)

### Payload & View DTOs
```csharp
/// <summary>payload ที่ส่งเข้า UIService.Open("AvatarCustomization", payload)</summary>
public sealed class AvatarCustomizationPayload
{
    public string DiscipleId;
    public AvatarCustomizationPayload(string discipleId) { DiscipleId = discipleId; }
}

public struct AvatarCategoryTabViewData
{
    public string Category;      // "ใบหน้า", "ลักษณะ", "ร่างกาย"
    public string DisplayName;
    public bool   IsActive;
}

public struct AvatarSlotTabViewData
{
    public string Slot;          // AvatarSlots.*
    public string DisplayName;   // "เสื้อผ้า", "ใบหน้า", ...
    public bool   IsActive;
}

public struct AvatarPartOptionViewData
{
    public string PartId;        // "" = ถอด/ใช้ default
    public string DisplayName;
    public string SpritePath;    // path ใต้ Resources/ สำหรับ thumbnail
    public bool   IsSelected;
    public bool   IsLocked;      // future unlocks
}
```
> 📎 Source: Assets/Scripts/UI/Views/AvatarCustomizationViewData.cs

### Presenter — Draft / Diff‑Commit / Rollback / External Sync
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
    if (disciple == null) { Debug.LogError($"[AvatarCustomizationPresenter] ไม่พบศิษย์: {_discipleId}"); return; }
    if (disciple.Avatar == null) disciple.Avatar = new AvatarAppearance();
    _original = disciple.Avatar.Clone();
    _draft    = disciple.Avatar.Clone();
    if (View.PreviewRenderer != null)
        View.PreviewRenderer.Initialize(_partPool);
    _subscription = _avatarChangedSub.Subscribe(OnAvatarChangedExternally);
    View.SetHeader("ปรับแต่งรูปลักษณ์", disciple.DisplayName);
    View.ShowError(null);
    RefreshAll();
}
```
> 📎 Source: Assets/Scripts/UI/Presenters/AvatarCustomizationPresenter.cs

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
            if (_stateProvider.TryChangeAvatarPart(_discipleId, slot, newVal, out reason, out result))
                applied.Add(slot);
            else
            {
                ok = false;
                failReason = reason;
                break;
            }
        }
        if (!ok)
        {
            foreach (var slot in applied)
            {
                AvatarAppearance dummy; string dummyReason;
                _stateProvider.TryChangeAvatarPart(_discipleId, slot, _original.GetSlot(slot), out dummyReason, out dummy);
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
    finally { _isCommitting = false; }
    RequestClose();
}
```
> 📎 Source: Assets/Scripts/UI/Presenters/AvatarCustomizationPresenter.cs

```csharp
private void OnAvatarChangedExternally(AvatarEquipmentChangedMessage msg)
{
    if (_isCommitting) return;                 // echo ของตัวเอง
    if (msg.DiscipleId != _discipleId) return; // คนละคน
    var state = _stateProvider.BuildSectEconomyState();
    var disciple = state.Disciples.Find(d => d.DiscipleId == _discipleId);
    if (disciple == null || disciple.Avatar == null) return;
    _original = disciple.Avatar.Clone();
    if (_draft.GetSlot(msg.Slot) == msg.OldPartId)
        _draft.SetSlot(msg.Slot, msg.NewPartId);
    View.RenderPreview(_draft);
    RefreshOptions();
    View.SetDirty(IsDirty());
}
```
> 📎 Source: Assets/Scripts/UI/Presenters/AvatarCustomizationPresenter.cs

### View — Category/Slot Tabs + Option Grid Pooling
```csharp
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
```
> 📎 Source: Assets/Scripts/UI/Views/AvatarCustomizationView.cs

## State Mutation Choke Point
```csharp
// ISectStateProvider
bool TryChangeAvatarPart(string discipleId, string slot, string partId,
                         out string failReason, out AvatarAppearance result);
bool TryApplyOutfit(string discipleId, string outfitId,
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
    if (System.Array.IndexOf(AvatarSlots.Equippable, slot) < 0) { failReason = $"Invalid slot: {slot}"; return false; }
    if (!_avatarPartPool.IsValidForSlot(slot, partId)) { failReason = $"PartId '{partId}' is not valid for slot '{slot}'"; return false; }
    if (disciple.Avatar == null) disciple.Avatar = new AvatarAppearance();
    var oldPart = disciple.Avatar.GetSlot(slot);
    disciple.Avatar.SetSlot(slot, partId);
    _avatarChangedPublisher.Publish(new AvatarEquipmentChangedMessage { DiscipleId = discipleId, Slot = slot, OldPartId = oldPart, NewPartId = partId });
    result = disciple.Avatar.Clone();
    return true;
}

public bool TryApplyOutfit(string discipleId, string outfitId,
                           out string failReason, out AvatarAppearance result)
{
    failReason = string.Empty;
    result = null;
    var disciple = _state.Disciples.FirstOrDefault(d => d.DiscipleId == discipleId);
    if (disciple == null) { failReason = $"No disciple with id: {discipleId}"; return false; }
    var outfit = _avatarPartPool.GetOutfit(outfitId);
    if (outfit == null) { failReason = $"Outfit '{outfitId}' not found"; return false; }
    // Validate pose compatibility (TODO: actual validation logic)
    // For now assume always valid.
    if (disciple.Avatar == null) disciple.Avatar = new AvatarAppearance();
    foreach (var kvp in outfit.Parts)
    {
        disciple.Avatar.SetSlot(kvp.Key, kvp.Value);
    }
    disciple.Avatar.PoseId = outfit.PoseId;
    _avatarChangedPublisher.Publish(new AvatarOutfitChangedMessage { DiscipleId = discipleId, OutfitId = outfitId, PoseId = outfit.PoseId });
    result = disciple.Avatar.Clone();
    return true;
}
```
> 📎 Source: Assets/Scripts/Systems/SectStateProvider.cs

## DI Registration
```csharp
// Avatar part definitions loaded from Resources/Data/avatar_parts.json
builder.Register<AvatarPartPool>(Lifetime.Singleton);

// UI (Xianxia.UI.MVP Lite)
builder.Register<Xianxia.Sect.UI.LogWindowPresenter>(Lifetime.Transient);
builder.Register<Xianxia.Sect.UI.AvatarCustomizationPresenter>(Lifetime.Transient);
builder.RegisterEntryPoint<Xianxia.Sect.UI.WorldEventUISystem>(Lifetime.Singleton);
builder.RegisterEntryPoint<Xianxia.Sect.UI.UIBootstrap>(Lifetime.Singleton);

// Request/response: changing a disciple's avatar part
messagePipeBuilder.RegisterTcpRemoteRequestHandler<ChangeAvatarPartRequest, ChangeAvatarPartResponse>(interprocess);
builder.RegisterAsyncRequestHandler<ChangeAvatarPartRequest, ChangeAvatarPartResponse, ChangeAvatarPartHandler>(options);

// Future: outfit apply request/response (TODO)
// messagePipeBuilder.RegisterTcpRemoteRequestHandler<ApplyOutfitRequest, ApplyOutfitResponse>(interprocess);

// Interprocess pub/sub: broadcast avatar equipment changes to UI + MCP client
messagePipeBuilder.RegisterTcpInterprocessMessageBroker<string, AvatarEquipmentChangedMessage>(interprocess);
messagePipeBuilder.RegisterTcpInterprocessMessageBroker<string, AvatarOutfitChangedMessage>(interprocess);
```
> 📎 Source: Assets/Scripts/Core/GameLifetimeScope.cs

## UI Bootstrap (demo)
```csharp
public void Start()
{
    _uiService.Open("ResourceHud");
    _uiService.Open("LogWindow");
    // Open AvatarCustomization for disciple d001 for testing
    _uiService.Open("AvatarCustomization", new AvatarCustomizationPayload("d001"));
}
```
> 📎 Source: Assets/Scripts/UI/Systems/UIBootstrap.cs

## Assets & Layout
- Data: `Assets/Resources/Data/avatar_parts.json` (TextAsset → `JsonUtility.FromJson<AvatarPartTable>`)
- Sprites/thumbnails: paths in JSON point under `Resources/` (loaded with `Resources.Load<Sprite>`)
- Panel layout: prefab `AvatarCustomization` full‑screen modal 900×620, `ContentSizeFitter` unconstrained.

## Related Pages
- [[entities/disciples|Disciples]] — owns the Avatar field
- [[concepts/mvp-ui|MVP UI Pattern]] — framework used by the panel
- [[concepts/message-pipe-bus|MessagePipe Bus]] — channel/serialization
- [[concepts/state-management|State Management]] — single live instance
- [[sources/avatar-appearance|Avatar Customization (Sprite Swap)]] — design source doc

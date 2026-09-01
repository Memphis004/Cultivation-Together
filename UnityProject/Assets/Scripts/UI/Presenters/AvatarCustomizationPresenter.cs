using System;
using System.Collections.Generic;
using MessagePipe;
using UnityEngine;
using Xianxia.Sect;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect.UI
{
    /// <summary>
    /// Avatar customization presenter — draft/diff-commit/rollback/sync pattern.
    ///
    /// Logic ทั้งหมดอยู่ที่นี่ 5 เรื่อง:
    ///   (1) Draft pattern — clone appearance จริงมาเป็น "ร่าง" แก้ในร่างเท่านั้น
    ///   (2) Commit แบบ diff — เทียบ draft vs original ทีละ slot
    ///   (3) Rollback ถ้ากลางทางพัง — commit slot 1-2 ผ่าน slot 3 fail ต้องย้อน
    ///   (4) External sync — ถ้า AI Agent เปลี่ยนหน้าตาผ่าน MCP ขณะ panel เปิดอยู่
    ///   (5) Dispose ครบ — unsubscribe + ตัด event ของ View
    ///
    /// Presenter = plain C# class + Transient → instance ใหม่ทุกครั้งที่เปิด panel
    /// ห้ามเป็น MonoBehaviour และห้ามมี Update() ของตัวเอง
    ///
    /// Mutation ต้องเรียก provider "ตรงๆ" ห้ามอ้อม pub/sub → เรียก ISectStateProvider.TryChangeAvatarPart() ตรง
    /// </summary>
    public class AvatarCustomizationPresenter : UIPresenter<AvatarCustomizationView>
    {
        private readonly ISectStateProvider _stateProvider;
        private readonly AvatarPartPool     _partPool;
        private readonly ISubscriber<AvatarEquipmentChangedMessage> _avatarChangedSub;
        private readonly UIService _uiService;

        private IDisposable _subscription;

        private string           _discipleId;
        private AvatarAppearance _original;      // snapshot ตอนเปิด
        private AvatarAppearance _draft;         // ร่างที่ผู้เล่นกำลังแก้
        private string           _activeSlot = AvatarSlots.Body;
        private bool             _isCommitting;  // guard กัน echo จาก message ตัวเอง

        private readonly Dictionary<string, Sprite> _thumbCache = new Dictionary<string, Sprite>();

        private string _activeCategory = "ร่างกาย";  // category tab ที่เลือกอยู่

        private static readonly string[] CategoryOrder = { "ใบหน้า", "ลักษณะ", "ร่างกาย" };

        // VContainer inject ทาง constructor (Transient)
        public AvatarCustomizationPresenter(
            ISectStateProvider stateProvider,
            AvatarPartPool partPool,
            ISubscriber<AvatarEquipmentChangedMessage> avatarChangedSub,
            UIService uiService)
        {
            _stateProvider    = stateProvider;
            _partPool         = partPool;
            _avatarChangedSub = avatarChangedSub;
            _uiService        = uiService;
        }
        

        // ── lifecycle ─────────────────────────────────────────

        protected override void OnViewBound()
        {
            View.CategoryTabClicked += OnCategoryTabClicked;
            View.SlotTabClicked     += OnSlotTabClicked;
            View.PartOptionClicked  += OnPartOptionClicked;
            View.ConfirmClicked     += OnConfirm;
            View.CancelClicked      += OnCancel;
            View.RandomizeClicked   += OnRandomize;
        }

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
            if (disciple == null)
            {
                Debug.LogError("[AvatarCustomizationPresenter] ไม่พบศิษย์: " + _discipleId);
                return;
            }

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

        public override void OnClose()
        {
            if (_subscription != null) { _subscription.Dispose(); _subscription = null; }

            View.CategoryTabClicked -= OnCategoryTabClicked;
            View.SlotTabClicked     -= OnSlotTabClicked;
            View.PartOptionClicked  -= OnPartOptionClicked;
            View.ConfirmClicked     -= OnConfirm;
            View.CancelClicked      -= OnCancel;
            View.RandomizeClicked   -= OnRandomize;

            _thumbCache.Clear();
            _original = null;
            _draft    = null;
        }

        public override void Dispose()
        {
            OnClose();
        }

        // ── input handlers ────────────────────────────────────

        private void OnCategoryTabClicked(string category)
        {
            if (_activeCategory == category) return;
            _activeCategory = category;

            // default _activeSlot เป็น slot แรกของ category ใหม่
            string[] slots;
            if (AvatarSlots.Categories.TryGetValue(category, out slots) && slots.Length > 0)
                _activeSlot = slots[0];

            RefreshCategoryTabs();
            RefreshTabs();
            RefreshOptions();
        }

        private void OnSlotTabClicked(string slot)
        {
            if (_activeSlot == slot) return;
            _activeSlot = slot;
            RefreshTabs();
            RefreshOptions();
        }

        private void OnPartOptionClicked(string partId)
        {
            if (_draft.GetSlot(_activeSlot) == partId) return;   // กดซ้ำชิ้นเดิม = no-op

            _draft.SetSlot(_activeSlot, partId);
            View.ShowError(null);

            View.RenderPreview(_draft);      // preview อัปเดตทันที ไม่รอ commit
            RefreshOptions();                 // ย้ายกรอบ selected
            View.SetDirty(IsDirty());
        }

        private void OnRandomize()
        {
            var rng = new System.Random();
            for (int i = 0; i < AvatarSlots.Equippable.Length; i++)
            {
                string slot = AvatarSlots.Equippable[i];
                var list = _partPool.GetPartsForSlot(slot);
                if (list == null || list.Count == 0) continue;
                _draft.SetSlot(slot, list[rng.Next(list.Count)].id);
            }
            View.RenderPreview(_draft);
            RefreshOptions();
            View.SetDirty(IsDirty());
        }

        private void OnCancel()
        {
            // ไม่ต้อง rollback อะไรเลย เพราะยังไม่เคยแตะ state จริง — นี่คือข้อดีของ draft pattern
            _draft = _original.Clone();
            RequestClose();
        }

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

        // ── external sync (MCP agent เปลี่ยนขณะ panel เปิด) ───

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

        // ── rendering ─────────────────────────────────────────

        private void RefreshAll()
        {
            RefreshCategoryTabs();
            RefreshTabs();
            RefreshOptions();
            View.RenderPreview(_draft);
            View.SetDirty(false);
        }

        private void RefreshCategoryTabs()
        {
            var tabs = new List<AvatarCategoryTabViewData>(CategoryOrder.Length);
            for (int i = 0; i < CategoryOrder.Length; i++)
            {
                string cat = CategoryOrder[i];
                var t = new AvatarCategoryTabViewData();
                t.Category    = cat;
                t.DisplayName = cat;
                t.IsActive    = (cat == _activeCategory);
                tabs.Add(t);
            }
            View.RenderCategoryTabs(tabs);
        }

        private void RefreshTabs()
        {
            // แสดงเฉพาะ slot ที่อยู่ใน category ที่เลือก
            string[] slotsInCategory;
            if (!AvatarSlots.Categories.TryGetValue(_activeCategory, out slotsInCategory))
                slotsInCategory = new string[0];

            var tabs = new List<AvatarSlotTabViewData>(slotsInCategory.Length);
            for (int i = 0; i < slotsInCategory.Length; i++)
            {
                string slot = slotsInCategory[i];
                string label;
                if (!AvatarSlots.Labels.TryGetValue(slot, out label)) label = slot;

                var t = new AvatarSlotTabViewData();
                t.Slot        = slot;
                t.DisplayName = label;
                t.IsActive    = (slot == _activeSlot);
                tabs.Add(t);
            }
            View.RenderSlotTabs(tabs);
        }

        private void RefreshOptions()
        {
            var defs     = _partPool.GetPartsForSlot(_activeSlot);
            var selected = _draft.GetSlot(_activeSlot);
            var options  = new List<AvatarPartOptionViewData>(defs.Count);

            for (int i = 0; i < defs.Count; i++)
            {
                var d = defs[i];
                var o = new AvatarPartOptionViewData();
                o.PartId      = d.id;
                o.DisplayName = d.displayName;
                o.SpritePath  = d.spritePath;
                // "" ใน draft = default → ให้ default part ติดกรอบ selected ด้วย
                o.IsSelected  = (d.id == selected) ||
                                (string.IsNullOrEmpty(selected) && d.isDefault);
                o.IsLocked    = false;      // hook ไว้ให้ระบบปลดล็อกในอนาคต
                options.Add(o);
            }

            View.RenderOptions(options, ResolveThumbnail);
        }

        private Sprite ResolveThumbnail(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            Sprite cached;
            if (_thumbCache.TryGetValue(path, out cached)) return cached;

            var sprite = Resources.Load<Sprite>(path);
            _thumbCache[path] = sprite;     // cache แม้เป็น null กัน Load ซ้ำตอนสลับแท็บไปมา
            return sprite;
        }

        // ── helpers ───────────────────────────────────────────

        private bool IsDirty()
        {
            for (int i = 0; i < AvatarSlots.Equippable.Length; i++)
            {
                string s = AvatarSlots.Equippable[i];
                if (_draft.GetSlot(s) != _original.GetSlot(s)) return true;
            }
            return false;
        }

        private void RequestClose()
        {
            _uiService.Close("AvatarCustomization");
        }
    }
}

using System;
using UnityEngine;
using Xianxia.Sect.Building;

namespace Xianxia.Sect.UI
{
    /// <summary>
    /// Building menu presenter (MVP Lite) — หน้าที่เดียว: แสดงแท็บ 4 หมวดหมู่ (§4)
    /// + การ์ดอาคาร แล้วสั่ง BeginPlacement บน PlacementController เมื่อคลิกการ์ด.
    ///
    /// ⚠️ presenter นี้เป็น Transient — ตายพร้อมเมนู จึงห้ามถือ placement flow:
    /// ghost/ปุ่มลอย/สถานะเป็นของ BuildingPlacementUISystem (persistent) ทั้งหมด
    /// (bug เดิม: Dispose ของเมนูยกเลิก ghost ที่เพิ่งเริ่ม → กด "วาง" ได้
    /// "Placement mode is not active." ทุกครั้ง)
    /// </summary>
    public class BuildingMenuPresenter : UIPresenter<BuildingMenuView>
    {
        private static readonly BuildingCategory[] Categories =
        {
            BuildingCategory.Production,
            BuildingCategory.Convenience,
            BuildingCategory.Shop,
            BuildingCategory.Landscape,
        };

        private static readonly string[] CategoryLabels =
        {
            "ผลิต", "สิ่งอำนวยความสะดวก", "ร้านค้า", "ภูมิทัศน์",
        };

        private readonly BuildingDefPool _defPool;
        private readonly PlacementController _placement;
        private readonly BuildingPlacementUISystem _placementUI;

        private System.Action _closeCallback;
        private BuildingTabCell[] _tabs;
        private BuildingCategory _selectedCategory = BuildingCategory.Production;

        public BuildingMenuPresenter(
            BuildingDefPool defPool,
            PlacementController placement,
            BuildingPlacementUISystem placementUI)
        {
            _defPool = defPool;
            _placement = placement;
            _placementUI = placementUI;
        }

        protected override void OnViewBound()
        {
            View.CloseClicked += OnCloseClicked;

            BuildTabs();
            SelectCategory(BuildingCategory.Production);
        }

        public override void OnOpen(object args)
        {
            _closeCallback = (args as BuildingMenuArgs)?.CloseCallback;
        }

        private void BuildTabs()
        {
            View.ClearTabs();
            _tabs = new BuildingTabCell[Categories.Length];

            for (int i = 0; i < Categories.Length; i++)
            {
                var tab = View.CreateTab(Categories[i]); // Init แล้วใน CreateTab — ครั้งเดียว
                if (tab == null) return;

                tab.SetLabel(CategoryLabels[i]);
                tab.Clicked += OnTabClicked;
                _tabs[i] = tab;
            }

            HighlightSelectedTab();
        }

        private void OnTabClicked(BuildingCategory category)
        {
            SelectCategory(category);
        }

        private void SelectCategory(BuildingCategory category)
        {
            _selectedCategory = category;
            HighlightSelectedTab();
            BuildItems();
        }

        private void HighlightSelectedTab()
        {
            if (_tabs == null) return;
            for (int i = 0; i < _tabs.Length; i++)
            {
                if (_tabs[i] != null) _tabs[i].SetSelected(Categories[i] == _selectedCategory);
            }
        }

        private void BuildItems()
        {
            View.ClearItems();

            var defs = _defPool.GetByCategory(_selectedCategory);
            for (int i = 0; i < defs.Count; i++)
            {
                var cell = View.CreateItem();
                if (cell == null) return;

                cell.Set(defs[i].Id, defs[i].DisplayName, FormatCost(defs[i]), LoadThumbnail(defs[i]));
                cell.Clicked += OnItemClicked;
            }
        }

        private static string FormatCost(BuildingDef def)
        {
            var cost = def.GetCost();
            if (cost.Count == 0) return "ฟรี";

            var parts = new string[cost.Count];
            int i = 0;
            foreach (var (resource, amount) in cost)
            {
                parts[i++] = $"{resource}×{amount}";
            }
            return string.Join("  ", parts);
        }

        private static Sprite LoadThumbnail(BuildingDef def)
        {
            var path = !string.IsNullOrEmpty(def.ThumbnailPath) ? def.ThumbnailPath : def.SpritePath;
            if (string.IsNullOrEmpty(path)) return null; // placeholder สี่เหลี่ยมใน cell
            return Resources.Load<Sprite>(path);
        }

        private void OnItemClicked(string defId)
        {
            var def = _placement.BeginPlacement(defId);
            if (def == null)
            {
                Debug.LogWarning($"[BuildingMenuPresenter] Unknown def '{defId}' - placement not started.");
                return;
            }

            // สั่ง persistent system เปิดปุ่มลอย + publish BuildModeStarted (กล้อง zoom)
            // — ทำที่ system ไม่ใช่ presenter เพราะ flow ต้องรอดจากการปิดเมนู
            if (_placementUI != null) _placementUI.OnPlacementBegan();

            // ปิดเมนูได้เลย — ghost + ปุ่มลอยอยู่กับ BuildingPlacementUISystem แล้ว
            NotifyCloseCallback();
        }

        private void OnCloseClicked()
        {
            // ปิดเมนู = แค่ปิดเมนู ไม่ยกเลิก ghost (การวางยังค้างอยู่กับ system จนกด วาง/ยกเลิก)
            NotifyCloseCallback();
        }

        private void NotifyCloseCallback()
        {
            var callback = _closeCallback;
            _closeCallback = null;
            callback?.Invoke();
        }

        public override void Dispose()
        {
            if (View != null)
                View.CloseClicked -= OnCloseClicked;

            if (_tabs != null)
            {
                for (int i = 0; i < _tabs.Length; i++)
                {
                    if (_tabs[i] != null) _tabs[i].Clicked -= OnTabClicked;
                }
                _tabs = null;
            }

            // ห้ามแตะ _placement ที่นี่ — flow การวางต้องรอดจากการปิดเมนู
            // (ปุ่ม วาง/หมุน/ยกเลิก ยังทำงานผ่าน BuildingPlacementUISystem)
            _closeCallback = null;
        }
    }
}

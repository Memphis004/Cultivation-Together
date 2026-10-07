using MessagePipe;
using UnityEngine;
using VContainer.Unity;
using Xianxia.Sect;
using Xianxia.Sect.Building;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect.UI
{
    /// <summary>
    /// เจ้าของ "โหมดวางอาคาร" ที่แท้จริง (persistent IStartable — แพทเทิร์นเดียวกับ
    /// DiscipleDetailUISystem): ฟังการเปลี่ยนสถานะของ PlacementController แล้ว
    /// แสดง/ซ่อนปุ่มลอย ยกเลิก|หมุน|วาง + publish BuildModeStarted/EndedMessage
    /// ให้กล้อง (Placement preset) กับ GridOverlayRenderer.
    ///
    /// ปุ่มลอย "เกาะ" เหนือ ghost (ตามเกม ref screenshot 3–4): ทุกเฟรมดึงจุดกึ่งกลาง
    /// ghost บนหน้าจอจาก BuildingSystem แล้ววางแถบปุ่มไว้เหนือ footprint เสมอ
    ///
    /// ทำไมต้องแยก: BuildingMenuPresenter เป็น Transient (ตายพร้อมเมนู) — ถ้า flow
    /// การวางอยู่กับ presenter ghost จะโดนยกเลิกตอนเมนูปิด (bug ที่เคยเกิด:
    /// เลือกการ์ดแล้วกด "วาง" ได้ "Placement mode is not active.")
    /// </summary>
    public class BuildingPlacementUISystem : IStartable, ITickable
    {
        private const float ButtonBarOffsetPixels = 90f; // ระยะปุ่มลอยเหนือกึ่งกลาง footprint

        private readonly PlacementController _placement;
        private readonly BuildingGrid _grid;
        private readonly UIRoot _uiRoot;
        private readonly IPublisher<BuildModeStartedMessage> _buildStartedPublisher;
        private readonly IPublisher<BuildModeEndedMessage> _buildEndedPublisher;
        private readonly BuildingSystem _buildingSystem;

        private BuildingPlacementView _view;
        private bool _viewWired;
        private bool _placementActive;

        public BuildingPlacementUISystem(
            PlacementController placement,
            BuildingGrid grid,
            UIRoot uiRoot,
            BuildingSystem buildingSystem,
            IPublisher<BuildModeStartedMessage> buildStartedPublisher,
            IPublisher<BuildModeEndedMessage> buildEndedPublisher)
        {
            _placement = placement;
            _grid = grid;
            _uiRoot = uiRoot;
            _buildingSystem = buildingSystem;
            _buildStartedPublisher = buildStartedPublisher;
            _buildEndedPublisher = buildEndedPublisher;
        }

        public void Start()
        {
            // poll state ของ PlacementController (ตั้งใจ: ไม่เพิ่ม message ใหม่บน bus —
            // สถานะเปลี่ยนเฉพาะจังหวะคลิกผู้ใช้ไม่ใช่ per-frame; ตัวสำรวจแบบเบา พอ)
            _placementActive = false;
        }

        /// <summary>เมนูเรียกหลัง BeginPlacement สำเร็จ — เปิดปุ่มลอย + กล้อง zoom</summary>
        public void OnPlacementBegan()
        {
            PublishBuildModeStarted();
            ShowControls();
        }

        private void PublishBuildModeStarted()
        {
            _buildStartedPublisher?.Publish(new BuildModeStartedMessage
            {
                SourceId = "BuildingPlacementUISystem",
                GhostId = null, // ghost ยังไม่มี id ระดับ scene — BuildingSystem sync จาก PlacementController
            });
        }

        private void ShowControls()
        {
            if (_view == null)
            {
                var parent = _uiRoot != null ? _uiRoot.Root : null;
                if (parent == null)
                {
                    Debug.LogError("[BuildingPlacementUISystem] No UIRoot to host placement controls.");
                    return;
                }

                _view = BuildingPlacementView.Build(parent);
            }

            if (!_viewWired)
            {
                _view.CancelClicked += OnCancel;
                _view.RotateClicked += OnRotate;
                _view.ConfirmClicked += OnConfirm;
                _viewWired = true;
            }

            SetStatus(string.Empty);
            _view.Show();
            _placementActive = true;
        }

        private void HideControls()
        {
            _placementActive = false;
            if (_view != null) _view.Hide();
        }

        // ปุ่มลอยเกาะเหนือ ghost ทุกเฟรม (ตำแหน่งเมาส์/กล้องเปลี่ยนตลอดขณะลาก)
        public void Tick()
        {
            if (!_placementActive || _view == null) return;

            Vector2 screenPos;
            if (_buildingSystem == null || !_buildingSystem.TryGetGhostScreenPosition(out screenPos)) return;
            if (_view.ButtonBarRect == null) return;

            var target = screenPos + new Vector2(0f, ButtonBarOffsetPixels);
            var canvas = _view.ButtonBarRect.GetComponentInParent<Canvas>();
            if (canvas == null) return;

            if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                // Overlay canvas: screen px == canvas px — bar anchor อยู่กลาง (0.5,0.5)
                // จึงเป็น offset จากจอครึ่งหนึ่ง ตรง ๆ
                _view.ButtonBarRect.anchoredPosition =
                    target - new Vector2(Screen.width, Screen.height) * 0.5f;
            }
            else
            {
                var uiCam = canvas.worldCamera;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvas.transform as RectTransform, target, uiCam, out var local);
                _view.ButtonBarRect.anchoredPosition = local;
            }
        }

        private void OnRotate()
        {
            _placement.Rotate(); // Rotatable=false → no-op
        }

        private void OnConfirm()
        {
            if (!_placement.IsActive)
            {
                // กดค้างจากรอบก่อน — เก็บปุ่มเงียบ ๆ (กล้องกลับ overview ด้วย)
                PublishBuildModeEnded(confirmed: false);
                HideControls();
                return;
            }

            if (_grid == null)
            {
                Debug.LogError("[BuildingPlacementUISystem] BuildingGrid not injected - cannot commit.");
                return;
            }

            string reason;
            Xianxia.Sect.PlacedBuildingState placed;
            if (_placement.TryCommit(_grid, out reason, out placed))
            {
                PublishBuildModeEnded(confirmed: true);
                SetStatus(string.Empty);
                HideControls();
            }
            else
            {
                SetStatus(reason); // ข้อความเดียวรวมทุกกรณี §3.2 (แดง = ghost อยู่แล้ว)
            }
        }

        private void OnCancel()
        {
            // ✕ ตอนไหนก็ได้ → ยกเลิก ghost ทิ้ง ไม่มีอะไรเปลี่ยนใน state (§5)
            _placement.Cancel();
            PublishBuildModeEnded(confirmed: false);
            SetStatus(string.Empty);
            HideControls();
        }

        /// <summary>ถูกเรียกเมื่อ build mode ปิดจากภายนอก (เช่น toggle ปุ่ม "สร้าง" ซ้ำ)</summary>
        public void OnBuildModeEndedExternally()
        {
            if (_placement.IsActive)
            {
                _placement.Cancel();
                PublishBuildModeEnded(confirmed: false); // กล้องกลับ overview ด้วย
            }
            HideControls();
        }

        private void PublishBuildModeEnded(bool confirmed)
        {
            _buildEndedPublisher?.Publish(new BuildModeEndedMessage
            {
                SourceId = "BuildingPlacementUISystem",
                Confirmed = confirmed,
            });
        }

        private void SetStatus(string message)
        {
            if (_view != null) _view.SetStatus(message ?? string.Empty);
        }
    }
}

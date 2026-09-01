using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Xianxia.Sect.UI
{
    /// <summary>
    /// ปุ่มลูกในกริด — 1 ตัว = thumbnail + label + selected frame
    /// แยกออกมาเป็น component เล็กๆ เพราะมี reference หลายตัวใน 1 ปุ่ม
    /// ถ้าให้ View ไล่ GetComponentInChildren เองจะเปราะมาก
    /// </summary>
    public class AvatarOptionButton : MonoBehaviour
    {
        [SerializeField] private Button     button;
        [SerializeField] private Image      thumbnail;
        [SerializeField] private TMP_Text   label;
        [SerializeField] private GameObject selectedFrame;
        [SerializeField] private GameObject lockedOverlay;

        private Action<string> _onClick;
        private string _partId;

        private void Awake()
        {
            if (button == null) button = GetComponent<Button>();
            button.onClick.AddListener(HandleClick);
        }

        private void OnDestroy()
        {
            button.onClick.RemoveListener(HandleClick);
            _onClick = null;                       // กัน presenter เก่าค้างใน closure
        }

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

        private void HandleClick()
        {
            var cb = _onClick;
            if (cb != null) cb(_partId);
        }
    }
}

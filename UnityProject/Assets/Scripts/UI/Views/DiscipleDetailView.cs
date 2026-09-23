using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Xianxia.Sect.UI
{
    /// <summary>
    /// Phase 4 — "click chibi → detail" panel view (plan §11).
    /// Pure display: presenter pushes strings + binds the shared AvatarRenderer
    /// (same renderer/resolver as AvatarCustomization — no second portrait
    /// renderer, no drift). All refs are wired by DiscipleDetailPanelGenerator.
    /// </summary>
    public class DiscipleDetailView : UIViewBase
    {
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text rankText;
        [SerializeField] private TMP_Text taskText;
        [SerializeField] private TMP_Text walletText;
        [SerializeField] private Button closeButton;
        [SerializeField] private AvatarRenderer portraitRenderer;

        public event Action CloseClicked;

        public AvatarRenderer PortraitRenderer { get { return portraitRenderer; } }

        private void Awake()
        {
            if (closeButton != null)
                closeButton.onClick.AddListener(() => CloseClicked?.Invoke());
        }

        public void SetInfo(string name, string rank, string task, string wallet)
        {
            if (nameText != null) nameText.text = name;
            if (rankText != null) rankText.text = rank;
            if (taskText != null) taskText.text = task;
            if (walletText != null) walletText.text = wallet;
        }
    }
}

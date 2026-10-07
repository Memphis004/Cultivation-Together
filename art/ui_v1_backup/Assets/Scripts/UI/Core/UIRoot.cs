using UnityEngine;

namespace Xianxia.Sect.UI
{
    /// <summary>
    /// Container that every UI panel is parented under.
    /// On Awake it forces its own RectTransform to stretch-fill the Canvas
    /// and lets every already-existing child panel (baked-in prefab clones)
    /// apply its own default layout via <see cref="UIViewBase.ApplyDefaultLayout"/>.
    ///
    /// Layout used to be string-matched on prefab names here
    /// (ApplyWalletHudLayout / ApplyBottomMenuLayout / ...) — migrated onto
    /// the views themselves per open question #13, so adding a panel no
    /// longer requires touching this class.
    /// </summary>
    public class UIRoot : MonoBehaviour
    {
        [SerializeField] private Transform root;
        public Transform Root => root != null ? root : transform;

        private void Awake()
        {
            // Force this RectTransform to fill the entire Canvas.
            var myRt = GetComponent<RectTransform>();
            if (myRt != null)
            {
                myRt.anchorMin = Vector2.zero;
                myRt.anchorMax = Vector2.one;
                myRt.offsetMin = Vector2.zero;
                myRt.offsetMax = Vector2.zero;
            }

            // Fix every already-existing child (baked-in prefab clones).
            foreach (Transform child in transform)
            {
                child.GetComponent<UIViewBase>()?.ApplyDefaultLayout();
            }
        }
    }
}

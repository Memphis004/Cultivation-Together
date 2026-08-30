using UnityEngine;

namespace Xianxia.Sect.UI
{
    public interface IUIView
    {
        GameObject GameObject { get; }
        void Show();
        void Hide();
    }
}

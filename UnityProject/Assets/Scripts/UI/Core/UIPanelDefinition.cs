using System;
using UnityEngine;

namespace Xianxia.Sect.UI
{
    [Serializable]
    public class UIPanelDefinition
    {
        public string PanelId;
        public GameObject Prefab;
        public UIPresenterKind PresenterKind;
    }
}

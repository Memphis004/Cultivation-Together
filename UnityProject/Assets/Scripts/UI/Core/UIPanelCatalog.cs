using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xianxia.Sect.UI
{
    [CreateAssetMenu(menuName = "Xianxia Sect/UI/Panel Catalog")]
    public class UIPanelCatalog : ScriptableObject
    {
        [SerializeField] private List<UIPanelDefinition> panels = new List<UIPanelDefinition>();

        public UIPanelDefinition Get(string panelId)
        {
            for (var i = 0; i < panels.Count; i++)
            {
                if (panels[i].PanelId == panelId) return panels[i];
            }

            throw new Exception($"UI panel not found: {panelId}");
        }
    }
}

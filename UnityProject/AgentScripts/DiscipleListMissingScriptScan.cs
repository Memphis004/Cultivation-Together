// Editor-only diagnostic: scan DiscipleListPanel.prefab for missing scripts
// (task step 2) + report the close-button wiring evidence for checks a-d.
// Read-only: reports, does not modify.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class DiscipleListMissingScriptScan
{
    public static void Main()
    {
        const string path = "Assets/Prefabs/UI/DiscipleListPanel.prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var report = new System.Text.StringBuilder();
            Walk(root.transform, "", report);
            Debug.Log("[ScanMissingScripts] report:\n" + report);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void Walk(Transform t, string prefix, System.Text.StringBuilder sb)
    {
        string path = prefix + "/" + t.name;
        int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
        sb.Append(path).Append(": missing=").Append(missing);
        if (missing > 0)
        {
            var so = new SerializedObject(t.gameObject);
            var it = so.GetIterator();
            while (it.NextVisible(true))
            {
                if (it.propertyType == SerializedPropertyType.ObjectReference &&
                    it.objectReferenceValue == null && it.name == "m_Script" &&
                    it.serializedObject.targetObject is MonoBehaviour mb &&
                    mb.gameObject == t.gameObject)
                {
                    sb.Append(" [missing component class=").Append(mb.GetType().Name).Append("]");
                }
            }
            // m_EditorClassIdentifier survives even when the script is missing:
            // read it raw off every MonoBehaviour so we can name the lost type.
            foreach (var mb in t.GetComponents<MonoBehaviour>())
            {
                if (mb == null)
                {
                    var s = new SerializedObject(t.gameObject);
                    sb.Append(" [unresolvable MonoBehaviour present]");
                }
            }
            sb.Append(" [EditorClassIdentifiers:");
            var ser = new SerializedObject(t.gameObject);
            var comp = ser.FindProperty("m_Component");
            for (int i = 0; i < comp.arraySize; i++)
            {
                var compRef = comp.GetArrayElementAtIndex(i).objectReferenceValue;
                if (compRef == null)
                {
                    sb.Append(" <null component ref>");
                    continue;
                }
                var cs = new SerializedObject(compRef);
                var id = cs.FindProperty("m_EditorClassIdentifier");
                if (id != null && !string.IsNullOrEmpty(id.stringValue))
                    sb.Append(" ").Append(id.stringValue);
            }
            sb.Append("]");
        }
        sb.Append("\n");

        // Close-button evidence (checks a & b)
        if (t.name == "Window")
        {
            sb.Append("  sibling order (top = last):");
            for (int i = 0; i < t.childCount; i++)
                sb.Append(" ").Append(i).Append("=").Append(t.GetChild(i).name);
            sb.Append("\n");
        }
        if (t.name == "CloseMedallion" || t.name == "CloseButton")
        {
            foreach (var g in t.GetComponents<Graphic>())
                sb.Append("  ").Append(t.name).Append(".Graphic ").Append(g.GetType().Name)
                  .Append(" raycast=").Append(g.raycastTarget)
                  .Append(" enabled=").Append(g.enabled).Append("\n");
            var btn = t.GetComponent<Button>();
            if (btn != null)
                sb.Append("  ").Append(t.name).Append(".Button targetGraphic=")
                  .Append(btn.targetGraphic != null ? btn.targetGraphic.name : "null")
                  .Append(" transition=").Append(btn.transition).Append("\n");
        }
        if (t.name == "ScrollView" || t.name == "Viewport")
        {
            foreach (var g in t.GetComponents<Graphic>())
                sb.Append("  ").Append(t.name).Append(".Graphic raycast=").Append(g.raycastTarget).Append("\n");
        }

        for (int i = 0; i < t.childCount; i++)
            Walk(t.GetChild(i), prefix == "" ? t.name : prefix + "/" + t.name, sb);
    }
}

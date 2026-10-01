#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// ช่อง [PassiveStartNode] = dropdown ของ node ที่ติ๊ก Start ใน PassiveTree (ค่าที่เก็บ = node id)
// id ที่ไม่มีใน tree แล้ว แสดงเป็น "(missing) id" ให้เห็นว่าต้องเลือกใหม่
[CustomPropertyDrawer(typeof(PassiveStartNodeAttribute))]
public class PassiveStartNodeDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        if (property.propertyType != SerializedPropertyType.String)
        {
            EditorGUI.PropertyField(position, property, label);
            return;
        }

        var tree = Resources.Load<PassiveTree>(PassiveTree.DefaultResourcePath);
        var ids = new List<string> { "" };
        var names = new List<string> { "(none)" };
        if (tree != null)
        {
            foreach (var n in tree.StartNodes)
            {
                ids.Add(n.id);
                names.Add($"{(string.IsNullOrEmpty(n.displayName) ? "Start" : n.displayName)}  [{n.id}]");
            }
        }

        string current = property.stringValue ?? "";
        int index = ids.IndexOf(current);
        if (index < 0)
        {
            ids.Add(current);
            names.Add($"(missing) {current}");
            index = ids.Count - 1;
        }

        EditorGUI.BeginProperty(position, label, property);
        int picked = EditorGUI.Popup(position, label.text, index, names.ToArray());
        if (picked != index) property.stringValue = ids[picked];
        EditorGUI.EndProperty();
    }
}
#endif

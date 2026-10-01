#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// Inspector ของ StatModifier (ใช้ทุกที่: ไอเทม / gem / passive node / PlayerStats)
// บรรทัด 1 = Stat + ชนิด / บรรทัด 2 = Damage แบบ Flat -> Min / Max (ช่วงดาเมจ) / stat อื่น -> Value
// ช่อง Per Level ใช้กับ gem (เพิ่มต่อ level ทั้ง min และ max)
[CustomPropertyDrawer(typeof(StatModifier))]
public class StatModifierDrawer : PropertyDrawer
{
    private const float Gap = 4f;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label) =>
        EditorGUIUtility.singleLineHeight * 2f + EditorGUIUtility.standardVerticalSpacing * 2f;

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        var stat = property.FindPropertyRelative("stat");
        var type = property.FindPropertyRelative("type");
        var value = property.FindPropertyRelative("value");
        var valueMax = property.FindPropertyRelative("valueMax");
        var perLevel = property.FindPropertyRelative("valuePerLevel");

        EditorGUI.BeginProperty(position, label, property);
        float line = EditorGUIUtility.singleLineHeight;
        float space = EditorGUIUtility.standardVerticalSpacing;
        var row1 = new Rect(position.x, position.y + space, position.width, line);
        var row2 = new Rect(position.x, row1.yMax + space, position.width, line);

        int indent = EditorGUI.indentLevel;
        float labelWidth = EditorGUIUtility.labelWidth;
        EditorGUI.indentLevel = 0;

        // บรรทัด 1: ชื่อ element + Stat + Type
        var labelRect = new Rect(row1.x + indent * 15f, row1.y, 90f, line);
        EditorGUI.LabelField(labelRect, label);
        float x = labelRect.xMax + Gap;
        float w = row1.xMax - x;
        EditorGUI.PropertyField(new Rect(x, row1.y, w * 0.6f - Gap, line), stat, GUIContent.none);
        EditorGUI.PropertyField(new Rect(x + w * 0.6f, row1.y, w * 0.4f, line), type, GUIContent.none);

        // บรรทัด 2: ค่า
        bool range = stat.enumValueIndex >= 0 && stat.enumNames[stat.enumValueIndex] == nameof(StatType.Damage)
                  && type.enumValueIndex >= 0 && type.enumNames[type.enumValueIndex] == nameof(ModifierType.Flat);
        EditorGUIUtility.labelWidth = 40f;
        x = labelRect.xMax + Gap;
        if (range)
        {
            float cw = (w - Gap * 2f) / 3f;
            EditorGUI.PropertyField(new Rect(x, row2.y, cw, line), value, new GUIContent("Min", "ดาเมจต่ำสุด"));
            float min = value.floatValue;
            EditorGUI.BeginChangeCheck();
            float max = EditorGUI.FloatField(new Rect(x + cw + Gap, row2.y, cw, line), new GUIContent("Max", "ดาเมจสูงสุด (สุ่มระหว่าง Min-Max ทุกครั้งที่โดน)"), Mathf.Max(min, valueMax.floatValue));
            if (EditorGUI.EndChangeCheck()) valueMax.floatValue = Mathf.Max(min, max);
            EditorGUIUtility.labelWidth = 50f;
            EditorGUI.PropertyField(new Rect(x + (cw + Gap) * 2f, row2.y, cw, line), perLevel, new GUIContent("Per Lv", "เพิ่มต่อ level ของ gem (ทั้ง min และ max)"));
        }
        else
        {
            float cw = (w - Gap) / 2f;
            string unit = type.enumValueIndex >= 0 && type.enumNames[type.enumValueIndex] != nameof(ModifierType.Flat) ? "%" : "";
            EditorGUI.PropertyField(new Rect(x, row2.y, cw, line), value, new GUIContent("Value" + unit, "Flat = หน่วยตรงๆ / Increased, More = % (20 = 20%)"));
            EditorGUIUtility.labelWidth = 50f;
            EditorGUI.PropertyField(new Rect(x + cw + Gap, row2.y, cw, line), perLevel, new GUIContent("Per Lv", "เพิ่มต่อ level ของ gem"));
        }

        EditorGUIUtility.labelWidth = labelWidth;
        EditorGUI.indentLevel = indent;
        EditorGUI.EndProperty();
    }
}
#endif

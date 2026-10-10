using System.Collections.Generic;
using UnityEngine;

// gem สำหรับ passive tree โดยเฉพาะ: ใส่ node แบบ Socket แล้วได้ modifiers ตาม level (ใส่ช่องสกิลไม่ได้)
// เป็นไอเทมเหมือน gem อื่น (อยู่ในกระเป๋า / ดรอปได้ / ItemInstance.level) asset อยู่ใต้ Gameobject/ScriptAbleObject/Items
[CreateAssetMenu(menuName = "Passives/Passive Gem", fileName = "PassiveGem_")]
public class PassiveGem : ItemDefinition
{
    public override ItemCategory Category => ItemCategory.PassiveGem;

    public Color gemColor = new Color(0.4f, 0.8f, 1f);
    [Min(1)] public int maxLevel = 20;
    [Tooltip("stat ที่ node ได้ตอนใส่ gem นี้ (valuePerLevel = เพิ่มต่อ level)")]
    public List<StatModifier> modifiers = new List<StatModifier>();

    // gem หนัก 1 Byte เหมือน gem อื่น
    public PassiveGem() => weight = 1;

    // modifier ที่ level นี้ (ค่าคงที่แล้ว ใส่เข้า PlayerStats ได้ตรงๆ)
    public StatModifier Scaled(StatModifier m, int level)
    {
        level = Mathf.Clamp(level, 1, maxLevel);
        var r = new StatModifier(m.stat, m.type, m.Evaluate(level));
        if (m.IsRange) r.valueMax = m.EvaluateMax(level);
        return r;
    }

    public string Describe(int level)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var m in modifiers)
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(StatMath.Describe(m, Mathf.Clamp(level, 1, maxLevel)));
        }
        return sb.ToString();
    }
}

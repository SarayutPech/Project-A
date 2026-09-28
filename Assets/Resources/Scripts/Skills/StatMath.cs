using System.Collections.Generic;
using UnityEngine;

// สูตรรวม modifier ที่ใช้ทุกที่ (stat sheet / สกิล / elite) ให้คิดเหมือนกันหมด
// ค่าสุดท้าย = (ฐาน + Σflat) × (1 + Σincreased%) × Π(1 + more%)
public static class StatMath
{
    public static float Apply(float baseValue, StatType stat, IEnumerable<(StatModifier mod, int level)> mods)
    {
        float flat = 0f, increased = 0f, more = 1f;
        if (mods != null)
        {
            foreach (var (m, level) in mods)
            {
                if (m.stat != stat) continue;
                float v = m.Evaluate(level);
                switch (m.type)
                {
                    case ModifierType.Flat: flat += v; break;
                    case ModifierType.Increased: increased += v * 0.01f; break;
                    case ModifierType.More: more *= 1f + v * 0.01f; break;
                }
            }
        }
        return (baseValue + flat) * Mathf.Max(0f, 1f + increased) * more;
    }

    // stat ระดับตัวละคร (support gem ใส่ไปไม่มีผล)
    public static bool IsCharacterStat(StatType stat) => stat == StatType.MaxHealth || stat == StatType.MovementSpeed;
}

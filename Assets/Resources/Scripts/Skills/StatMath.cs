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

    // แบบช่วง (Damage min-max): Flat บวกฝั่ง min/max แยกกัน (EvaluateMax) แล้ว increased/more คูณทั้งสองฝั่งเท่ากัน
    public static Vector2 ApplyRange(float baseMin, float baseMax, StatType stat, IEnumerable<(StatModifier mod, int level)> mods)
    {
        float flatMin = 0f, flatMax = 0f, increased = 0f, more = 1f;
        if (mods != null)
        {
            foreach (var (m, level) in mods)
            {
                if (m.stat != stat) continue;
                switch (m.type)
                {
                    case ModifierType.Flat:
                        flatMin += m.Evaluate(level);
                        flatMax += m.EvaluateMax(level);
                        break;
                    case ModifierType.Increased: increased += m.Evaluate(level) * 0.01f; break;
                    case ModifierType.More: more *= 1f + m.Evaluate(level) * 0.01f; break;
                }
            }
        }
        float mul = Mathf.Max(0f, 1f + increased) * more;
        float min = Mathf.Max(0f, (baseMin + flatMin) * mul);
        float max = Mathf.Max(min, (Mathf.Max(baseMin, baseMax) + flatMax) * mul);
        return new Vector2(min, max);
    }

    // "12-18" / "15" (ช่วงเท่ากัน)
    public static string FormatRange(float min, float max) =>
        Mathf.Approximately(min, max) ? $"{min:0.#}" : $"{min:0.#}-{max:0.#}";

    // stat ระดับตัวละคร (support gem ใส่ไปไม่มีผล)
    public static bool IsCharacterStat(StatType stat) => (int)stat >= (int)StatType.MaxHealth;

    // ชื่อ stat สำหรับ UI/tooltip
    public static string StatName(StatType stat) => stat switch
    {
        StatType.Damage => "Damage",
        StatType.AttackSpeed => "Attack Speed",
        StatType.MoveSpeedWhileUsing => "Movement Speed while using",
        StatType.AreaOfEffect => "Area of Effect",
        StatType.MaxHealth => "Maximum Life",
        StatType.MovementSpeed => "Movement Speed",
        StatType.MaxMana => "Maximum Mana",
        StatType.ManaRegen => "Mana Regeneration per second",
        StatType.CarryCapacity => "Carry Capacity (Byte)",
        StatType.Strength => "Strength",
        StatType.Dexterity => "Dexterity",
        StatType.Intelligence => "Intelligence",
        _ => stat.ToString(),
    };

    // ข้อความ modifier 1 บรรทัด เช่น "+20 Maximum Life" / "+10% increased Damage" / "+15% more Attack Speed"
    public static string Describe(StatModifier m, int level = 1)
    {
        float v = m.Evaluate(level);
        string sign = v >= 0f ? "+" : "";
        string stat = StatName(m.stat);
        return m.type switch
        {
            ModifierType.Flat when m.IsRange => $"Adds {m.Evaluate(level):0.#}-{m.EvaluateMax(level):0.#} {stat}",
            ModifierType.Flat => $"{sign}{v:0.##} {stat}",
            ModifierType.Increased => $"{sign}{v:0.#}% increased {stat}",
            _ => $"{sign}{v:0.#}% more {stat}",
        };
    }
}

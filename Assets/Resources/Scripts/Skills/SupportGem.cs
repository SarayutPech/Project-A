using System.Collections.Generic;
using UnityEngine;

// gem เสริม: ใส่ช่อง support ของสกิลแล้วปรับค่าของสกิลนั้น สร้างจาก Create > Skills > Support Gem
// ตอนนี้เป็นตัวปรับค่าพื้นฐาน (ดาเมจ / ความเร็วตี / ความเร็วเดินตอนใช้ / พื้นที่) ภายหลังเพิ่มพฤติกรรมพิเศษได้ (เช่นยิงซ้ำ, ธาตุ)
[CreateAssetMenu(menuName = "Skills/Support Gem", fileName = "SupportGem_")]
public class SupportGem : SkillGem
{
    public List<StatModifier> modifiers = new List<StatModifier>();

    // ข้อความสรุปผลที่ level นี้ ไว้แสดงใน UI/tooltip
    public string Describe(int level)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var m in modifiers)
        {
            float v = m.Evaluate(level);
            string sign = v >= 0f ? "+" : "";
            string stat = m.stat switch
            {
                StatType.Damage => "Damage",
                StatType.AttackSpeed => "Attack Speed",
                StatType.MoveSpeedWhileUsing => "Movement Speed while using",
                StatType.AreaOfEffect => "Area of Effect",
                _ => m.stat.ToString(),
            };
            string line = m.type switch
            {
                ModifierType.Flat => $"{sign}{v:0.##} {stat}",
                ModifierType.Increased => $"{sign}{v:0.#}% increased {stat}",
                _ => $"{sign}{v:0.#}% more {stat}",
            };
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(line);
        }
        return sb.ToString();
    }
}

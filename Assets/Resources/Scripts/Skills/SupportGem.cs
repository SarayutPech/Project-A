using System.Collections.Generic;
using UnityEngine;

// gem เสริม: ใส่ช่อง support ของสกิลแล้วปรับค่าของสกิลนั้น สร้างจาก Create > Skills > Support Gem
// ตอนนี้เป็นตัวปรับค่าพื้นฐาน (ดาเมจ / ความเร็วตี / ความเร็วเดินตอนใช้ / พื้นที่) ภายหลังเพิ่มพฤติกรรมพิเศษได้ (เช่นยิงซ้ำ, ธาตุ)
[CreateAssetMenu(menuName = "Skills/Support Gem", fileName = "SupportGem_")]
public class SupportGem : SkillGem
{
    public override ItemCategory Category => ItemCategory.SupportGem;

    public List<StatModifier> modifiers = new List<StatModifier>();

    // ข้อความสรุปผลที่ level นี้ ไว้แสดงใน UI/tooltip
    public string Describe(int level)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var m in modifiers)
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(StatMath.Describe(m, level));
        }
        return sb.ToString();
    }
}

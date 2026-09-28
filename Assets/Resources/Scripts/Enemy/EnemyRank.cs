using UnityEngine;

public enum EnemyRankType
{
    Normal,
    Elite,
}

[System.Serializable]
public struct EliteSettings
{
    [Tooltip("ตัวคูณขนาดตัว (collider + โมเดล + ระยะ hitbox)")]
    [Min(0.5f)] public float scale;
    [Min(0.1f)] public float healthMultiplier;
    [Min(0.1f)] public float damageMultiplier;
    [Tooltip("ตัวคูณความเร็วโจมตี (ตัวใหญ่ตีช้าลงได้ด้วยค่า < 1)")]
    [Min(0.1f)] public float attackSpeedMultiplier;

    public static EliteSettings Default => new EliteSettings
    {
        scale = 1.5f,
        healthMultiplier = 3f,
        damageMultiplier = 1.5f,
        attackSpeedMultiplier = 0.85f,
    };
}

// ระดับของศัตรู (simulation ฝั่ง server) EnemySpawner เรียก MakeElite ตอนเกิด
// ปรับค่า gameplay (ขนาด/HP/ดาเมจ/ความเร็วตี) ที่นี่ ส่วนเรืองแสงอยู่ใน EliteGlow (presentation) อ่าน Rank ไปแสดงเอง
// ภายหลังทำ Netcode: Rank เป็น NetworkVariable, scale sync ผ่าน NetworkTransform
public class EnemyRank : MonoBehaviour
{
    public EnemyRankType Rank { get; private set; } = EnemyRankType.Normal;
    public bool IsElite => Rank == EnemyRankType.Elite;
    public event System.Action<EnemyRank> RankChanged;

    public void MakeElite(EliteSettings s)
    {
        if (IsElite) return;
        Rank = EnemyRankType.Elite;

        transform.localScale *= s.scale;

        var health = GetComponent<Health>();
        if (health != null) health.SetMaxHealth(health.maxHealth * s.healthMultiplier, true);

        // ใช้ modifier แบบ More เหมือน support gem -> คิดรวมกับค่าสกิลในสูตรเดียวกัน
        var attack = GetComponent<MeleeAttack>();
        if (attack != null)
        {
            attack.AddModifier(new StatModifier(StatType.Damage, ModifierType.More, (s.damageMultiplier - 1f) * 100f));
            attack.AddModifier(new StatModifier(StatType.AttackSpeed, ModifierType.More, (s.attackSpeedMultiplier - 1f) * 100f));
        }

        // ตัวใหญ่ขึ้น ระยะต่อยต้องไกลขึ้นตาม ไม่งั้นยืนต่อยไม่ถึง
        var ai = GetComponent<EnemyAI>();
        if (ai != null) ai.attackRange *= s.scale;

        RankChanged?.Invoke(this);
    }
}

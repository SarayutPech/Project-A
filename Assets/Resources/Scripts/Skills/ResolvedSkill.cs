using System.Collections.Generic;
using UnityEngine;

// ค่าสุดท้ายของสกิล 1 ตัว = active gem (ตาม level) + support gem ทุกช่อง + modifier อื่น (stat sheet ของตัวละคร / elite)
// คำนวณครั้งเดียวตอนเปลี่ยน gem / stat (ไม่ใช่ทุกเฟรม) logic ล้วน รันบน server ได้
// สูตรแต่ละค่า: StatMath.Apply = (ฐาน + Σflat) × (1 + Σincreased%) × Π(1 + more%)
public class ResolvedSkill
{
    public ActiveSkillGem Gem { get; private set; }
    public int Level { get; private set; }

    public float Damage { get; private set; }
    // ความเร็วตีสุดท้าย (ฐาน = Attack Speed ของตัวละคร แล้ว support บวก/คูณต่อ) ท่า/hitbox/cooldown/animation เร็วขึ้นตามนี้
    public float AttackSpeed { get; private set; }
    public float MoveSpeedMultiplier { get; private set; }
    public float AreaMultiplier { get; private set; }
    // รัศมี hitbox โตตาม √พื้นที่ (พื้นที่ x2 = รัศมี x1.41)
    public float RadiusMultiplier => Mathf.Sqrt(AreaMultiplier);

    public float Duration => Gem.duration;
    public float Cooldown => Gem.cooldown;
    // ความยาวท่า / cooldown จริงหลังคิด attack speed
    public float ScaledDuration => Gem.duration / AttackSpeed;
    public float ScaledCooldown => Gem.cooldown / AttackSpeed;
    public IReadOnlyList<AttackHitbox> Hitboxes => Gem.hitboxes;
    public bool Channel => Gem.channel;
    public float LoopStart => Gem.loopStart;
    public float LoopEnd => Gem.loopEnd;
    public float MaxChannelTime => Gem.maxChannelTime;
    public AnimationClip Animation => Gem.animation;

    // support ที่มีผลจริง (ตัดช่องว่าง / gem ซ้ำชนิดเดียวกันนับครั้งเดียวแบบ PoE)
    public IReadOnlyList<GemInstance> ActiveSupports => _supports;
    private readonly List<GemInstance> _supports = new List<GemInstance>();

    // baseAttackSpeed = ความเร็วตีของตัวละครจาก stat sheet (ไม่มี = 1)
    public static ResolvedSkill Resolve(ActiveSkillGem gem, int level, IEnumerable<GemInstance> supports,
        IEnumerable<StatModifier> extraModifiers = null, float baseAttackSpeed = 1f)
    {
        if (gem == null) return null;
        var r = new ResolvedSkill { Gem = gem, Level = Mathf.Clamp(level, 1, gem.maxLevel) };

        // รวม modifier ทั้งหมดเป็น (ค่า, level)
        var mods = new List<(StatModifier mod, int level)>();
        if (supports != null)
        {
            var seen = new HashSet<SkillGem>();
            foreach (var s in supports)
            {
                if (!(s.gem is SupportGem support) || !seen.Add(support)) continue;
                r._supports.Add(s);
                foreach (var m in support.modifiers) mods.Add((m, s.ClampedLevel));
            }
        }
        if (extraModifiers != null)
            foreach (var m in extraModifiers) mods.Add((m, 1));

        r.Damage = Mathf.Max(0f, StatMath.Apply(gem.DamageAtLevel(r.Level), StatType.Damage, mods));
        r.AttackSpeed = Mathf.Max(0.1f, StatMath.Apply(Mathf.Max(0.1f, baseAttackSpeed), StatType.AttackSpeed, mods));
        r.MoveSpeedMultiplier = Mathf.Clamp(StatMath.Apply(1f - gem.moveSlowPercent * 0.01f, StatType.MoveSpeedWhileUsing, mods), 0f, 2f);
        r.AreaMultiplier = Mathf.Max(0.1f, StatMath.Apply(1f, StatType.AreaOfEffect, mods));
        return r;
    }
}

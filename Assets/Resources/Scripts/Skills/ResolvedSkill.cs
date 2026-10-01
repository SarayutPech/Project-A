using System.Collections.Generic;
using UnityEngine;

// ค่าสุดท้ายของสกิล 1 ตัว = active gem (ตาม level) + support gem ทุกช่อง + modifier อื่น (stat sheet ของตัวละคร / elite)
// คำนวณครั้งเดียวตอนเปลี่ยน gem / stat (ไม่ใช่ทุกเฟรม) logic ล้วน รันบน server ได้
// สูตรแต่ละค่า: StatMath.Apply = (ฐาน + Σflat) × (1 + Σincreased%) × Π(1 + more%)
public class ResolvedSkill
{
    public ActiveSkillGem Gem { get; private set; }
    public int Level { get; private set; }

    // ดาเมจต่อ hitbox แบบช่วง (สุ่มทุกครั้งที่โดน ฝั่ง server) / Damage = ค่าเฉลี่ยไว้แสดง/เทียบ
    public float DamageMin { get; private set; }
    public float DamageMax { get; private set; }
    public float Damage => (DamageMin + DamageMax) * 0.5f;
    public float RollDamage() => DamageMax > DamageMin ? Random.Range(DamageMin, DamageMax) : DamageMin;
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
    public AnimationClip GetAnimation(int variant) => Gem.GetAnimation(variant);
    public int AnimationCount => Gem.AnimationCount;
    public SkillMirrorMode Mirror => Gem.mirror;
    public bool UpperBodyOnly => Gem.upperBodyOnly;
    public bool TeleportToTarget => Gem.teleportToTarget;
    public float TeleportRange => Gem.teleportRange;
    public float TeleportLandingGap => Gem.teleportLandingGap;
    public bool AvoidSameTarget => Gem.avoidSameTarget;
    public int ShadowCloneCount => Gem.shadowCloneCount;
    public bool ShadowClonesOnlyWhileHeld => Gem.shadowClonesOnlyWhileHeld;
    public float ShadowCloneDistance => Gem.shadowCloneDistance;
    public float ShadowCloneAngle => Gem.shadowCloneAngle;
    public float ShadowCloneDamage => Gem.shadowCloneDamage;
    public AnimationClip BlendAnimation => Gem.blendAnimation;
    public Vector2 BlendWeightRange => Gem.blendWeightRange;
    // เปลี่ยนท่า/ข้างได้ (มีให้สุ่มหรือกลับข้าง) -> channel สุ่มใหม่ทุกรอบที่วน
    public bool HasPresentationVariety => Gem.AnimationCount > 1 || Gem.mirror != SkillMirrorMode.None;

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

        Vector2 dmg = StatMath.ApplyRange(gem.DamageMinAtLevel(r.Level), gem.DamageMaxAtLevel(r.Level), StatType.Damage, mods);
        r.DamageMin = dmg.x;
        r.DamageMax = dmg.y;
        // ฐาน = ความเร็วตีตัวละคร × ความเร็วฐานของสกิล แล้ว support บวก/คูณต่อ
        r.AttackSpeed = Mathf.Max(0.1f, StatMath.Apply(Mathf.Max(0.1f, baseAttackSpeed) * gem.attackSpeed, StatType.AttackSpeed, mods));
        r.MoveSpeedMultiplier = Mathf.Clamp(StatMath.Apply(1f - gem.moveSlowPercent * 0.01f, StatType.MoveSpeedWhileUsing, mods), 0f, 2f);
        // ฐาน = พื้นที่ของสกิล (gem) แล้ว stat sheet / support บวก/คูณต่อ
        r.AreaMultiplier = Mathf.Max(0.1f, StatMath.Apply(gem.areaOfEffect, StatType.AreaOfEffect, mods));
        return r;
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

// gem สกิลโจมตีระยะประชิด (ข้อมูลท่า) สร้างจาก Create > Skills > Active Skill Gem
// MeleeAttack อ่านค่าจาก gem นี้ + support gem ที่ใส่ช่อง (ผ่าน ResolvedSkill) ไม่เก็บค่าท่าเองแล้ว
// ศัตรูก็ใช้ gem แบบเดียวกัน (เช่น Punch) -> ท่าทุกตัวในเกมปรับจาก asset ได้ที่เดียว
public enum SkillMirrorMode
{
    None,       // ไม่กลับข้าง
    Alternate,  // สลับซ้าย/ขวาทุกครั้งที่ใช้ (หมัดซ้าย-ขวา ดูรัว)
    Random,     // สุ่มข้าง
}

[CreateAssetMenu(menuName = "Skills/Active Skill Gem", fileName = "ActiveGem_")]
public class ActiveSkillGem : SkillGem
{
    public override ItemCategory Category => ItemCategory.SkillGem;

    [Header("Presentation")]
    [Tooltip("ท่าหลัก (ใช้เมื่อ Animation Variants ว่าง)")]
    public AnimationClip animation;
    [Tooltip("สุ่มท่าจากรายการนี้ทุกครั้งที่ใช้ (ไม่ซ้ำท่าเดิมติดกัน) ว่าง = ใช้ Animation อย่างเดียว\nทุกท่าถูกยืด/หดให้ยาวเท่า Duration -> จังหวะ hitbox เหมือนกันทุกท่า")]
    public List<AnimationClip> animationVariants = new List<AnimationClip>();
    [Tooltip("ท่าที่ผสมทับท่าหลักทุกครั้ง (เช่น Dodging ผสมกับหมัด = ต่อยไปหลบไป) ว่าง = ไม่ผสม")]
    public AnimationClip blendAnimation;
    [Tooltip("น้ำหนักท่าผสม (min, max) สุ่มต่อครั้ง 0 = ท่าหลักล้วน, 1 = ท่าผสมล้วน")]
    public Vector2 blendWeightRange = new Vector2(0.25f, 0.5f);
    [Tooltip("กลับท่าซ้าย/ขวา (hitbox กลับข้างตามด้วย)")]
    public SkillMirrorMode mirror = SkillMirrorMode.None;
    [Tooltip("เล่นท่าเฉพาะท่อนบน (layer UpperBody) ขายังเดิน/วิ่ง/ยืนตามการเคลื่อนที่ -> ต่อยไปเดินไปได้เนียน\nปิด = เล่นเต็มตัว (เช่นเตะหมุน)")]
    public bool upperBodyOnly = false;

    [Header("Timing")]
    [Tooltip("ความยาวท่าที่ attack speed = 1 (วินาที)")]
    [Min(0.05f)] public float duration = 1f;
    [Tooltip("ความเร็วฐานของสกิลนี้ (คูณกับ Attack Speed ของตัวละคร แล้ว support บวก/คูณต่อ) ท่า/hitbox/cooldown/animation เร็วตาม\nเช่น 1.5 = สกิลนี้เร็วกว่าท่าปกติ 50%")]
    [Min(0.1f)] public float attackSpeed = 1f;
    [Tooltip("ต้องรอเท่านี้หลังจบท่าก่อนใช้ใหม่ (วินาที ที่ attack speed = 1)")]
    [Min(0f)] public float cooldown = 0.15f;

    [Header("Area")]
    [Tooltip("พื้นที่ฐานของสกิลนี้ (1 = ตาม hitbox ที่ตั้งไว้) stat sheet / support บวก/คูณต่อ\nรัศมี hitbox โตตาม √พื้นที่ (2 = พื้นที่ 2 เท่า รัศมี x1.41) แบบ PoE")]
    [Min(0.1f)] public float areaOfEffect = 1f;

    [Header("Teleport (เช่น Flicker Strike)")]
    [Tooltip("วาร์ปไปข้างศัตรูก่อนตี ไม่มีศัตรูในระยะ = ใช้ไม่ได้ / channel: ทุกรอบที่วนวาร์ปไปตัวใหม่ ไม่มีตัวใหม่ = จบท่า")]
    public bool teleportToTarget = false;
    [Tooltip("หาเป้าในระยะเท่านี้จากตัว (เลือกตัวที่ใกล้เคอร์เซอร์ที่สุด)")]
    [Min(0.5f)] public float teleportRange = 12f;
    [Tooltip("ยืนห่างจากขอบตัวเป้าเท่านี้หลังวาร์ป")]
    [Min(0f)] public float teleportLandingGap = 0.3f;
    [Tooltip("วนไปทั่วกลุ่ม: เลือกตัวที่วาร์ปไปหาล่าสุดนานที่สุดก่อน (ปิด = ตัวที่ใกล้เคอร์เซอร์ที่สุดเสมอ)")]
    public bool avoidSameTarget = true;

    [Header("Shadow Clones (ร่างเงาช่วยตี)")]
    [Tooltip("จำนวนร่างเงาที่โผล่มาตีพร้อมกัน (0 = ไม่มี) ตีด้วย hitbox เดียวกับท่า จากตำแหน่งของร่าง -> เพิ่มดาเมจจริง")]
    [Range(0, 6)] public int shadowCloneCount = 0;
    [Tooltip("มีร่างเงาเฉพาะตอนกดค้าง (channel) / ปิด = ทุกครั้งที่ใช้")]
    public bool shadowClonesOnlyWhileHeld = true;
    [Tooltip("ร่างเงายืนห่างจากตัวเท่านี้")]
    [Min(0.2f)] public float shadowCloneDistance = 1.3f;
    [Tooltip("มุมจากทิศที่ตีไปซ้าย/ขวา ของร่างคู่แรก (คู่ถัดไปกางออกเพิ่ม) 0 = ยืนหน้าตรง, 90 = ข้างตัว")]
    [Range(0f, 180f)] public float shadowCloneAngle = 55f;
    [Tooltip("ตัวคูณดาเมจต่อร่าง (คูณกับดาเมจของท่า)")]
    [Range(0f, 2f)] public float shadowCloneDamage = 0.35f;

    [Header("Damage")]
    [Tooltip("ดาเมจต่ำสุด / สูงสุดที่ level 1 (สุ่มในช่วงนี้ทุกครั้งที่โดน) สูงสุด <= ต่ำสุด = ค่าเดียว")]
    [FormerlySerializedAs("baseDamage")] [Min(0f)] public float baseDamageMin = 10f;
    [Min(0f)] public float baseDamageMax = 0f;
    [Tooltip("ดาเมจเพิ่มต่อ level ของ gem (ทั้งต่ำสุดและสูงสุด)")]
    [Min(0f)] public float damagePerLevel = 2f;
    public List<AttackHitbox> hitboxes = new List<AttackHitbox> { new AttackHitbox() };

    [Header("Movement ระหว่างใช้")]
    [Tooltip("ลดความเร็วเดินกี่ % ระหว่างท่า (0 = เดินปกติ, 100 = ยืนนิ่ง) support gem บวกเพิ่มได้")]
    [Range(0f, 100f)] public float moveSlowPercent = 40f;

    [Header("Channel (กดค้าง = วนท่าต่อเนื่อง)")]
    [Tooltip("กดค้างแล้ววนช่วง Loop Start-End ของท่าไปเรื่อยๆ (hitbox ในช่วงนั้นโดนซ้ำทุกรอบ) ปล่อยแล้วเล่นท่าที่เหลือจนจบ\nถ้ามีหลายท่า (Animation Variants) / Mirror ทุกรอบที่วนจะสุ่มท่าใหม่ + สลับข้าง")]
    public bool channel = false;
    [Range(0f, 1f)] public float loopStart = 0.25f;
    [Tooltip("pose ที่ loopStart กับ loopEnd ควรเหมือนกัน ไม่งั้นภาพกระตุกตอนวน")]
    [Range(0f, 1f)] public float loopEnd = 0.75f;
    [Tooltip("วนได้นานสุดกี่วินาที (0 = ไม่จำกัด)")]
    [Min(0f)] public float maxChannelTime = 0f;

    [Header("Spawn On Cast (เช่น Recall เปิด portal)")]
    [Tooltip("สร้าง object นี้หน้าตัวตอนเริ่มท่า (server) ว่าง = ไม่สร้าง\nถ้าเป็น ScenePortal ที่พาไป scene ที่อยู่แล้ว (เช่น portal กลับ Hideout ตอนอยู่ใน Hideout) จะใช้สกิลไม่ได้")]
    public GameObject spawnOnCast;
    [Tooltip("ระยะหน้าตัว (ตามทิศที่ใช้สกิล)")]
    [Min(0f)] public float spawnDistance = 2.5f;
    [Tooltip("มีได้ทีละ 1 อันต่อผู้เล่น (ใช้ใหม่ = อันเก่าหายไป)")]
    public bool singleSpawnInstance = true;

    public float DamageMinAtLevel(int level) => baseDamageMin + damagePerLevel * (Mathf.Max(1, level) - 1);
    public float DamageMaxAtLevel(int level) => Mathf.Max(baseDamageMin, baseDamageMax) + damagePerLevel * (Mathf.Max(1, level) - 1);

    // จำนวนท่าที่สุ่มได้ (อย่างน้อย 1 = animation หลัก)
    public int AnimationCount => animationVariants != null && animationVariants.Count > 0 ? animationVariants.Count : 1;

    public AnimationClip GetAnimation(int variant)
    {
        if (animationVariants == null || animationVariants.Count == 0) return animation;
        var clip = animationVariants[Mathf.Clamp(variant, 0, animationVariants.Count - 1)];
        return clip != null ? clip : animation;
    }
}

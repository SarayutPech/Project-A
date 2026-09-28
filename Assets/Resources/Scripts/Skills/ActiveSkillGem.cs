using System.Collections.Generic;
using UnityEngine;

// gem สกิลโจมตีระยะประชิด (ข้อมูลท่า) สร้างจาก Create > Skills > Active Skill Gem
// MeleeAttack อ่านค่าจาก gem นี้ + support gem ที่ใส่ช่อง (ผ่าน ResolvedSkill) ไม่เก็บค่าท่าเองแล้ว
// ศัตรูก็ใช้ gem แบบเดียวกัน (เช่น Punch) -> ท่าทุกตัวในเกมปรับจาก asset ได้ที่เดียว
[CreateAssetMenu(menuName = "Skills/Active Skill Gem", fileName = "ActiveGem_")]
public class ActiveSkillGem : SkillGem
{
    [Header("Presentation")]
    [Tooltip("ท่าที่เล่นใน state Attack (สลับ clip ให้ตอนเปลี่ยนสกิล)")]
    public AnimationClip animation;

    [Header("Timing")]
    [Tooltip("ความยาวท่าที่ attack speed = 1 (วินาที)")]
    [Min(0.05f)] public float duration = 1f;
    [Tooltip("ต้องรอเท่านี้หลังจบท่าก่อนใช้ใหม่ (วินาที ที่ attack speed = 1)")]
    [Min(0f)] public float cooldown = 0.15f;

    [Header("Damage")]
    [Min(0f)] public float baseDamage = 10f;
    [Tooltip("ดาเมจเพิ่มต่อ level ของ gem")]
    [Min(0f)] public float damagePerLevel = 2f;
    public List<AttackHitbox> hitboxes = new List<AttackHitbox> { new AttackHitbox() };

    [Header("Movement ระหว่างใช้")]
    [Tooltip("ลดความเร็วเดินกี่ % ระหว่างท่า (0 = เดินปกติ, 100 = ยืนนิ่ง) support gem บวกเพิ่มได้")]
    [Range(0f, 100f)] public float moveSlowPercent = 40f;

    [Header("Channel (กดค้าง = วนท่าต่อเนื่อง)")]
    [Tooltip("กดค้างแล้ววนช่วง Loop Start-End ของท่าไปเรื่อยๆ (hitbox ในช่วงนั้นโดนซ้ำทุกรอบ) ปล่อยแล้วเล่นท่าที่เหลือจนจบ")]
    public bool channel = false;
    [Range(0f, 1f)] public float loopStart = 0.25f;
    [Tooltip("pose ที่ loopStart กับ loopEnd ควรเหมือนกัน ไม่งั้นภาพกระตุกตอนวน")]
    [Range(0f, 1f)] public float loopEnd = 0.75f;
    [Tooltip("วนได้นานสุดกี่วินาที (0 = ไม่จำกัด)")]
    [Min(0f)] public float maxChannelTime = 0f;

    public float DamageAtLevel(int level) => baseDamage + damagePerLevel * (Mathf.Max(1, level) - 1);
}

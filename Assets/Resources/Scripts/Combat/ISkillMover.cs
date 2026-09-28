using UnityEngine;

// ตัวละครที่สกิลย้ายตำแหน่งให้ได้ (เช่น Flicker Strike วาร์ปไปข้างศัตรู) simulation ฝั่ง server
// PlayerMovement / EnemyMotor implement -> MeleeAttack ไม่ต้องรู้ว่าเป็นตัวไหน
public interface ISkillMover
{
    // วางเท้าที่ footPosition หันไปทาง facing (แนวนอน) ล้างความเร็วเดิม
    void SkillTeleport(Vector3 footPosition, Vector3 facing);
}

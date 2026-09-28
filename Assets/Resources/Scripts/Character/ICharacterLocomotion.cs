using UnityEngine;

// สถานะการเคลื่อนที่ที่ฝั่งภาพ (CharacterAnimator) อ่านได้ ใช้ร่วมกันทั้ง player (PlayerMovement) และศัตรู (EnemyMotor)
// ค่าทั้งหมดเป็นผลของ simulation ภายหลัง sync ผ่านเน็ตให้ client ได้ตรงๆ
public interface ICharacterLocomotion
{
    Vector3 Velocity { get; }
    bool IsGrounded { get; }
    bool IsDashing { get; }
    // โหมดวิ่ง (ใช้เลือกท่าวิ่งแทนท่าเดิน)
    bool Sprinting { get; }
    // ความเร็วเดินปกติ ใช้เทียบว่าขยับอยู่แค่ไหน (blend idle/walk)
    float MoveSpeed { get; }
    // นับครั้งที่กระโดด (เพิ่มทีละ 1)
    int JumpCount { get; }
    bool LastJumpFromGround { get; }
}

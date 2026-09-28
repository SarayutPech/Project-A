using UnityEngine;

// ระบบต่อสู้ของ player ฝั่ง simulation (server): รับ "คำขอโจมตี" แล้ว validate ก่อนสั่ง MeleeAttack
// + คุมการเคลื่อนที่ระหว่างโจมตี และจัดการตอนตาย/ฟื้น
// input ไม่ได้อ่านที่นี่ (อยู่ใน PlayerAttackInput ของ local player) -> ภายหลัง RequestAttack กลายเป็น ServerRpc ได้ตรงๆ
[RequireComponent(typeof(Health), typeof(MeleeAttack), typeof(PlayerMovement))]
public class PlayerCombat : MonoBehaviour
{
    [Tooltip("ตายแล้วกี่วินาทีถึงฟื้นที่จุด Start ของแผนที่ (0 = ไม่ฟื้นเอง)")]
    [Min(0f)] public float respawnDelay = 4f;
    [Tooltip("ฟื้นแล้วได้ HP กี่ส่วนของ max")]
    [Range(0.05f, 1f)] public float respawnHealthFraction = 1f;
    [Tooltip("ตายแล้วกระเด็นไปทางที่โดนตีด้วยความเร็วแนวนอน/แนวตั้งเท่านี้")]
    [Min(0f)] public float deathKnockbackSpeed = 4f;
    [Min(0f)] public float deathKnockbackUp = 3f;
    [Tooltip("โจมตีกลางอากาศได้ไหม")]
    public bool allowAirAttack = true;
    [Tooltip("dash ระหว่างท่าโจมตีได้ไหม (ปิด = ต้องรอท่าจบ / กระโดดได้เสมอ)")]
    public bool allowDashWhileAttacking = false;

    public Health Health { get; private set; }
    public MeleeAttack Attack { get; private set; }

    private PlayerMovement _movement;
    private PlayerSkills _skills;
    private float _respawnTimer = -1f;

    private void Awake()
    {
        Health = GetComponent<Health>();
        Attack = GetComponent<MeleeAttack>();
        _movement = GetComponent<PlayerMovement>();
        _skills = GetComponent<PlayerSkills>();
        Health.team = Team.Player;
    }

    private void OnEnable()
    {
        Health.Died += OnDied;
        Health.Revived += OnRevived;
        Attack.Started += OnAttackStarted;
        Attack.Finished += OnAttackFinished;
    }

    private void OnDisable()
    {
        Health.Died -= OnDied;
        Health.Revived -= OnRevived;
        Attack.Started -= OnAttackStarted;
        Attack.Finished -= OnAttackFinished;
    }

    // slot ของท่าที่กำลังเล่น (-1 = ไม่ได้โจมตี) client ใช้รู้ว่าต้องส่งสถานะปุ่มไหนมาตอนกดค้าง
    public int ActiveSlot => Attack.IsAttacking ? _activeSlot : -1;
    private int _activeSlot = -1;

    // คำขอใช้สกิลช่อง slot จาก input (ภายหลัง = ServerRpc) ส่งมาแค่ "ช่องไหน + ทิศ" ไม่ส่งผลลัพธ์ (กฎข้อ 1)
    // server เช็คเองว่าตีได้ไหม: ช่องมีสกิล / ตาย / ยังล็อกตอนโหลด map / dash อยู่ / ลอยอยู่ (ถ้าปิด air attack) / cooldown ของสกิลนั้น
    // held = ยังกดค้างอยู่ (ท่า channel จะวนต่อจนกว่า UpdateHeld(false))
    public bool RequestAttack(int slot, Vector3 aimDirection, bool held = false)
    {
        if (Health.IsDead || !_movement.CanMove || _movement.IsDashing) return false;
        if (!allowAirAttack && !_movement.IsGrounded) return false;
        if (!IsFinite(aimDirection)) return false; // ไม่ trust ค่าจาก client (กฎข้อ 8)

        SkillSlot s = _skills != null ? _skills.GetSlot(slot) : null; // index จาก client ต้องเช็คช่วง
        if (s == null || s.Resolved == null) return false;

        if (!Attack.TryAttack(s.Resolved, aimDirection, held)) return false;
        _activeSlot = slot;
        _movement.FaceDirection(Attack.AimDirection);
        return true;
    }

    // สถานะปุ่มระหว่างท่า (ภายหลัง = ServerRpc ทุก tick / ตอนเปลี่ยน) ส่งมาแค่ "ยังกดอยู่ไหม + ทิศ"
    public void UpdateHeld(bool held, Vector3 aimDirection)
    {
        if (!Attack.IsAttacking) return;
        Attack.SetHeld(held && !Health.IsDead);
        if (held && IsFinite(aimDirection)) Attack.SetAim(aimDirection);
    }

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);

    // ระหว่างท่า: เดิน/หัน/กระโดดได้ตามปกติ แค่ช้าลง moveSlowPercent (ทั้งบนพื้นและกลางอากาศ)
    private void OnAttackStarted(MeleeAttack attack)
    {
        _movement.ActionSpeedMultiplier = attack.MoveSpeedMultiplier;
        _movement.DashBlocked = !allowDashWhileAttacking;
    }

    private void OnAttackFinished(MeleeAttack attack)
    {
        _movement.DashBlocked = false;
        if (Health.IsDead) return; // ตายกลางท่า -> ยังล็อกต่อ
        _movement.ActionSpeedMultiplier = 1f;
    }

    private void OnDied(Health health)
    {
        Attack.Cancel();
        _movement.ActionLocked = true;
        _movement.ActionSpeedMultiplier = 0f;

        // กระเด็นไปทางที่โดนตี หันหลังให้ทิศนั้น (ท่า Flying Back Death ล้มไปข้างหลัง) ด้วย physics จริง ตกขอบได้
        Vector3 dir = health.LastDamage.direction;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f)
        {
            dir.Normalize();
            _movement.FaceDirection(-dir);
            _movement.Knockback(dir * deathKnockbackSpeed + Vector3.up * deathKnockbackUp);
        }
        _respawnTimer = respawnDelay > 0f ? respawnDelay : -1f;
    }

    private void OnRevived(Health health)
    {
        _respawnTimer = -1f;
        _movement.ActionLocked = false;
        _movement.ActionSpeedMultiplier = 1f;
    }

    private void FixedUpdate()
    {
        if (_respawnTimer < 0f) return;
        _respawnTimer -= Time.fixedDeltaTime;
        if (_respawnTimer > 0f) return;

        _respawnTimer = -1f;
        // scene ที่ไม่มีแผนที่ (hideout) ฟื้นที่เดิม
        _movement.SpawnAtMapStart();
        Health.Revive(respawnHealthFraction);
    }
}

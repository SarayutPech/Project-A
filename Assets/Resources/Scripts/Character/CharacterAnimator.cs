using UnityEngine;

// ส่งสถานะของตัวละครไปให้ Animator (presentation อย่างเดียว ไม่มีผลกับ gameplay) ใช้ร่วมกันทั้ง player และศัตรู
// อ่านสถานะจาก ICharacterLocomotion (PlayerMovement / EnemyMotor), MeleeAttack, Health ใน parent เท่านั้น
// ไม่อ่าน input เอง -> server headless ถอด component นี้ออกได้
// วางบน object ที่มี Animator (เช่น Player/Visual)
[RequireComponent(typeof(Animator))]
public class CharacterAnimator : MonoBehaviour
{
    [Tooltip("ลอยจากพื้นสั้นกว่านี้ (เช่นเดินลงเนิน) ยังนับว่าอยู่บนพื้น กันท่าตกกระพริบ")]
    [Min(0f)] public float airborneGrace = 0.1f;

    [Header("Locomotion")]
    [Tooltip("เวลาหน่วงค่า Gait ให้เปลี่ยน idle/walk/run นุ่มขึ้น")]
    [Min(0f)] public float gaitDampTime = 0.12f;
    [Tooltip("ความเร็วที่ท่าเดินก้าวจริงตอนเล่น 1x (หน่วย/วินาที ตาม scale ของโมเดล) ใช้ปรับ playback ให้เท้าไม่ไถล")]
    [Min(0.1f)] public float walkClipSpeed = 2.3f;
    [Tooltip("ความเร็วที่ท่าวิ่งก้าวจริงตอนเล่น 1x")]
    [Min(0.1f)] public float runClipSpeed = 6.6f;
    [Tooltip("ช่วง playback speed ของท่าเดิน/วิ่ง (กันเร็ว/ช้าเกินจนดูแปลก)")]
    public Vector2 locomotionPlaybackRange = new Vector2(0.5f, 2.5f);

    [Header("Skill Animation")]
    [Tooltip("clip ที่วางไว้ใน state Attack ของ Animator Controller (ช่องที่จะถูกแทนด้วยท่าของสกิลปัจจุบัน)")]
    public AnimationClip attackSlotClip;

    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int GaitHash = Animator.StringToHash("Gait");
    private static readonly int LocomotionSpeedHash = Animator.StringToHash("LocomotionSpeed");
    private static readonly int DashingHash = Animator.StringToHash("Dashing");
    private static readonly int GroundedHash = Animator.StringToHash("Grounded");
    private static readonly int VerticalSpeedHash = Animator.StringToHash("VerticalSpeed");
    private static readonly int JumpHash = Animator.StringToHash("Jump");
    private static readonly int AirJumpHash = Animator.StringToHash("AirJump");
    private static readonly int AttackHash = Animator.StringToHash("Attack");
    private static readonly int AttackTimeHash = Animator.StringToHash("AttackTime");
    private static readonly int DeadHash = Animator.StringToHash("Dead");
    private static readonly int AttackingHash = Animator.StringToHash("Attacking");

    private Animator _animator;
    private ICharacterLocomotion _source;
    private MeleeAttack _attack;
    private Health _health;
    private int _seenJumpCount;
    private int _seenAttackCount;
    private float _lastGroundedTime;
    private bool _jumped; // กระโดดแล้วยังไม่แตะพื้น -> ข้าม grace ทันที
    private AnimatorOverrideController _override; // สร้างเองต่อตัว ต้องลบเอง (กฎข้อ 10)

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        _source = GetComponentInParent<ICharacterLocomotion>();
        _attack = GetComponentInParent<MeleeAttack>();
        _health = GetComponentInParent<Health>();
        if (_source != null) _seenJumpCount = _source.JumpCount;
        if (_attack != null) _seenAttackCount = _attack.AttackCount;
    }

    private void OnEnable()
    {
        if (_attack == null) return;
        _attack.SkillChanged += OnSkillChanged;
        ApplySkillAnimation(DefaultClip());
    }

    private void OnDisable()
    {
        if (_attack != null) _attack.SkillChanged -= OnSkillChanged;
    }

    private void OnDestroy()
    {
        if (_override != null) Destroy(_override);
    }

    private void OnSkillChanged(MeleeAttack attack)
    {
        if (!attack.IsAttacking) ApplySkillAnimation(DefaultClip());
    }

    // ท่าของสกิลตั้งต้น (ศัตรู) / player ที่มีหลาย slot เลือกท่าตอนเริ่มท่าจาก ActiveSkill แทน
    private AnimationClip DefaultClip() => _attack != null && _attack.Skill != null ? _attack.Skill.Animation : null;

    // ใส่ท่าลงช่อง attackSlotClip ผ่าน override controller ของตัวนี้
    // สร้าง override ครั้งเดียว (เปลี่ยน controller ทำให้ Animator รีเซ็ต) ครั้งต่อไปแค่สลับ clip
    private void ApplySkillAnimation(AnimationClip clip)
    {
        if (!EnsureOverride() || clip == null) return;
        if (_override[attackSlotClip] != clip) _override[attackSlotClip] = clip;
    }

    private bool EnsureOverride()
    {
        if (attackSlotClip == null) return false;
        if (_override == null)
        {
            RuntimeAnimatorController current = _animator.runtimeAnimatorController;
            if (current == null) return false;
            // controller เดิมเป็น override อยู่แล้ว (เช่นศัตรู) -> ทำ override ใหม่จาก controller หลัก แล้วคัดลอกของเดิมมาด้วย
            var source = current as AnimatorOverrideController;
            _override = new AnimatorOverrideController(source != null ? source.runtimeAnimatorController : current) { name = $"{current.name} (Skill)" };
            if (source != null)
            {
                var pairs = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<AnimationClip, AnimationClip>>();
                source.GetOverrides(pairs);
                _override.ApplyOverrides(pairs);
            }
            _animator.runtimeAnimatorController = _override;
        }
        return true;
    }

    private void Update()
    {
        bool dead = _health != null && _health.IsDead;
        _animator.SetBool(DeadHash, dead);
        UpdateAttack(dead);
        if (_source != null) UpdateLocomotion(dead);
    }

    private void UpdateAttack(bool dead)
    {
        if (_attack == null) return;

        // ระหว่างท่าโจมตี: ท่ากระโดด/dash ไม่ตัดท่าโจมตี (transition ใน Animator เช็ค Attacking)
        _animator.SetBool(AttackingHash, _attack.IsAttacking && !dead);

        // เวลาของท่า = ตำแหน่งในท่าของ simulation (state Attack ใช้ Motion Time = AttackTime)
        // -> ท่ายืด/หดตาม attack speed และวนตอน channel ตรงกับ hitbox เสมอ
        // เติมเวลาที่ผ่านไปหลัง physics tick ล่าสุด ไม่งั้นท่าขยับเป็นขั้นตาม 50Hz
        if (_attack.IsAttacking) _animator.SetFloat(AttackTimeHash, _attack.PredictNormalizedTime(Time.time - Time.fixedTime));

        // เริ่มท่าใหม่ (นับจาก MeleeAttack ไม่ใช่จาก input)
        if (_attack.AttackCount == _seenAttackCount) return;
        _seenAttackCount = _attack.AttackCount;
        if (dead) return;
        _animator.SetFloat(AttackTimeHash, 0f);
        // หลาย slot: สลับท่าเป็นของสกิลที่เพิ่งใช้ ก่อนเข้า state Attack
        if (_attack.ActiveSkill != null) ApplySkillAnimation(_attack.ActiveSkill.Animation);
        _animator.SetTrigger(AttackHash);
    }

    private void UpdateLocomotion(bool dead)
    {
        Vector3 velocity = _source.Velocity;
        float vy = velocity.y;
        bool grounded = _source.IsGrounded;

        // Gait: 0 = idle, 1 = walk, 2 = run / ท่าเดินหรือวิ่งเลือกจากสถานะ sprint
        // ความเร็วใช้แค่บอกว่าขยับอยู่แค่ไหน (ออกตัว/เบรก = blend กับ idle)
        float horizontalSpeed = new Vector2(velocity.x, velocity.z).magnitude;
        float moving = _source.MoveSpeed > 0f ? Mathf.Clamp01(horizontalSpeed / _source.MoveSpeed) : 0f;
        float gait = moving * (_source.Sprinting ? 2f : 1f);
        _animator.SetFloat(GaitHash, gait, gaitDampTime, Time.deltaTime);
        _animator.SetFloat(SpeedHash, horizontalSpeed);

        // playback ของเดิน/วิ่ง = ความเร็วจริง / ความเร็วก้าวของท่า (ท่าผสมตาม Gait) -> เท้าไม่ไถล
        // ยังเกือบยืนนิ่ง (Gait ใกล้ 0) ให้ค่อยๆ กลับเป็น 1x ไม่งั้นท่า idle จะช้าตามไปด้วย
        float blendedGait = _animator.GetFloat(GaitHash);
        float clipSpeed = Mathf.Lerp(walkClipSpeed, runClipSpeed, Mathf.Clamp01(blendedGait - 1f));
        float playback = Mathf.Clamp(horizontalSpeed / clipSpeed, locomotionPlaybackRange.x, locomotionPlaybackRange.y);
        _animator.SetFloat(LocomotionSpeedHash, Mathf.Lerp(1f, playback, Mathf.Clamp01(blendedGait)));

        // dash: ค้างท่า dash ไว้ตลอดช่วงที่พุ่ง
        _animator.SetBool(DashingHash, _source.IsDashing && !dead);

        // กระโดดจริงเท่านั้น (นับจาก locomotion) ไม่เดาจากความเร็ว -> วิ่งขึ้นทางลาดแล้ว vy พุ่งขึ้นไม่นับเป็นกระโดด
        if (_source.JumpCount != _seenJumpCount)
        {
            _seenJumpCount = _source.JumpCount;
            if (!dead)
            {
                // กระโดดระหว่างโจมตี: ไม่ตั้ง trigger (ถูกบล็อกแล้วจะค้างไปเล่นท่ากระโดดทีหลังตอนลงพื้นแล้ว)
                // ท่าโจมตีจบกลางอากาศ Animator ไปท่าตกเอง
                bool attacking = _attack != null && _attack.IsAttacking;
                if (!attacking) _animator.SetTrigger(_source.LastJumpFromGround ? JumpHash : AirJumpHash);
                _jumped = true;
            }
        }

        // locomotion ไม่นับว่าแตะพื้นระหว่างตัวยังพุ่งขึ้นหลังกระโดด -> grounded = ลงพื้นจริง (รวมลงบนทางลาดขาขึ้น)
        if (grounded)
        {
            _lastGroundedTime = Time.time;
            _jumped = false;
        }

        bool groundedForAnim = !_jumped && Time.time - _lastGroundedTime <= airborneGrace;
        _animator.SetBool(GroundedHash, groundedForAnim);
        _animator.SetFloat(VerticalSpeedHash, vy);
    }
}

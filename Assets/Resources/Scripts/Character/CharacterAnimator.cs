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
    [Tooltip("clip ช่อง A ใน Animator Controller (state Attack ของ base layer + AttackA ของ layer ท่อนบน) ถูกแทนด้วยท่าของสกิล")]
    public AnimationClip attackSlotClip;
    [Tooltip("clip ช่อง B (AttackB ของ layer ท่อนบน) สลับใช้กับช่อง A ทุกครั้งที่เปลี่ยนท่า -> ท่าเก่าเฟดออกขณะท่าใหม่เฟดเข้า")]
    public AnimationClip attackSlotClipB;
    [Tooltip("clip ช่องท่าผสม (ลูกที่ 2 ของ blend tree ใน state โจมตี) ถูกแทนด้วย Blend Animation ของสกิล")]
    public AnimationClip blendSlotClip;
    [Tooltip("ชื่อ layer ท่อนบนใน Animator (สกิลที่เปิด Upper Body Only เล่นที่ layer นี้ ขายังเดินตาม locomotion)")]
    public string upperBodyLayer = "UpperBody";
    [Tooltip("เวลาเฟด layer ท่อนบนเข้า/ออก (วินาที)")]
    [Min(0.01f)] public float upperBodyFadeTime = 0.1f;
    [Tooltip("เวลาเฟดตอนเปลี่ยนท่า (เริ่มท่าใหม่ / channel วนแล้วสุ่มท่าใหม่)")]
    [Min(0f)] public float attackCrossFade = 0.08f;

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
    private static readonly int AttackMirrorHash = Animator.StringToHash("AttackMirror");
    private static readonly int AttackBlendHash = Animator.StringToHash("AttackBlend");
    private static readonly int AttackingUpperHash = Animator.StringToHash("AttackingUpper");
    // layer ท่อนบน: 2 state สลับกัน แต่ละ state มีเวลา/กลับข้าง/น้ำหนักผสมของตัวเอง (ท่าเก่าค้างค่าเดิมระหว่างเฟดออก)
    private static readonly int UpperStateA = Animator.StringToHash("AttackA");
    private static readonly int UpperStateB = Animator.StringToHash("AttackB");
    private static readonly int[] UpperTimeHash = { Animator.StringToHash("AttackTimeA"), Animator.StringToHash("AttackTimeB") };
    private static readonly int[] UpperMirrorHash = { Animator.StringToHash("AttackMirrorA"), Animator.StringToHash("AttackMirrorB") };
    private static readonly int[] UpperBlendHash = { Animator.StringToHash("AttackBlendA"), Animator.StringToHash("AttackBlendB") };

    private int _upperLayerIndex = -1;
    private bool _upperActive; // ท่าปัจจุบันเล่นที่ layer ท่อนบน
    private int _upperSide;    // 0 = A, 1 = B
    private int _seenPresentation;

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
        _upperLayerIndex = string.IsNullOrEmpty(upperBodyLayer) ? -1 : _animator.GetLayerIndex(upperBodyLayer);
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

    // ใส่ท่าลงช่อง A ผ่าน override controller ของตัวนี้
    // สร้าง override ครั้งเดียว (เปลี่ยน controller ทำให้ Animator รีเซ็ต) ครั้งต่อไปแค่สลับ clip
    private void ApplySkillAnimation(AnimationClip clip) => SetSlot(attackSlotClip, clip);

    private void SetSlot(AnimationClip slot, AnimationClip clip)
    {
        if (!EnsureOverride() || slot == null || clip == null) return;
        if (_override[slot] != clip) _override[slot] = clip;
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

        // เริ่มท่าใหม่ / channel วนแล้วสุ่มท่าใหม่ (นับจาก MeleeAttack ไม่ใช่จาก input)
        // ทำก่อนตั้ง flag ด้านล่าง ให้ flag เป็นของท่าที่เพิ่งเริ่ม
        if (_attack.AttackCount != _seenAttackCount)
        {
            _seenAttackCount = _attack.AttackCount;
            _seenPresentation = _attack.PresentationCount;
            if (!dead) PlayAttackAnimation(true);
        }
        else if (_attack.PresentationCount != _seenPresentation)
        {
            _seenPresentation = _attack.PresentationCount;
            if (!dead && _attack.IsAttacking) PlayAttackAnimation(false);
        }

        // ท่าเต็มตัว: ท่ากระโดด/dash ไม่ตัดท่าโจมตี (transition ใน Animator เช็ค Attacking)
        // ท่าท่อนบน: base layer ยังเป็น locomotion ปกติ (กระโดดได้) layer ท่อนบนออกเมื่อ AttackingUpper = false
        bool attacking = _attack.IsAttacking && !dead;
        _animator.SetBool(AttackingHash, attacking && !_upperActive);
        _animator.SetBool(AttackingUpperHash, attacking && _upperActive);

        // เวลาของท่า = ตำแหน่งในท่าของ simulation (state Attack ใช้ Motion Time = AttackTime)
        // -> ท่ายืด/หดตาม attack speed และวนตอน channel ตรงกับ hitbox เสมอ
        // เติมเวลาที่ผ่านไปหลัง physics tick ล่าสุด ไม่งั้นท่าขยับเป็นขั้นตาม 50Hz
        if (_attack.IsAttacking)
        {
            float t = _attack.PredictNormalizedTime(Time.time - Time.fixedTime);
            _animator.SetFloat(_upperActive ? UpperTimeHash[_upperSide] : AttackTimeHash, t);
        }

        // layer ท่อนบน: เฟดเข้าระหว่างท่าท่อนบน เฟดออกเมื่อจบ (น้ำหนัก 0 = ไม่มีผล ขาและตัวกลับเป็น locomotion ทั้งหมด)
        if (_upperLayerIndex >= 0)
        {
            float target = _upperActive && _attack.IsAttacking && !dead ? 1f : 0f;
            float w = Mathf.MoveTowards(_animator.GetLayerWeight(_upperLayerIndex), target, Time.deltaTime / upperBodyFadeTime);
            _animator.SetLayerWeight(_upperLayerIndex, w);
        }
    }

    // เล่นท่าที่ simulation เลือกไว้ (variant + กลับข้าง + น้ำหนักท่าผสม)
    // newAttack = เริ่มท่าใหม่ / false = channel วนแล้วสุ่มท่าใหม่กลางท่า
    private void PlayAttackAnimation(bool newAttack)
    {
        ResolvedSkill skill = _attack.ActiveSkill;
        if (skill == null) return;
        AnimationClip clip = skill.GetAnimation(_attack.AnimationVariant);
        AnimationClip blend = skill.BlendAnimation;
        float blendWeight = blend != null ? _attack.BlendWeight : 0f;
        if (blend != null) SetSlot(blendSlotClip, blend);
        float t = _attack.NormalizedTime;

        _upperActive = skill.UpperBodyOnly && _upperLayerIndex >= 0 && attackSlotClipB != null;
        if (_upperActive)
        {
            // สลับช่อง A/B ทุกครั้ง: ท่าเก่าเล่นต่อที่ช่องเดิม (ค่าเวลา/กลับข้างค้างไว้) ระหว่างเฟดไปท่าใหม่
            _upperSide = 1 - _upperSide;
            SetSlot(_upperSide == 0 ? attackSlotClip : attackSlotClipB, clip);
            _animator.SetFloat(UpperTimeHash[_upperSide], t);
            _animator.SetBool(UpperMirrorHash[_upperSide], _attack.Mirrored);
            _animator.SetFloat(UpperBlendHash[_upperSide], blendWeight);
            _animator.CrossFadeInFixedTime(_upperSide == 0 ? UpperStateA : UpperStateB, attackCrossFade, _upperLayerIndex);
            return;
        }

        // เต็มตัว (base layer): state Attack เดียว
        ApplySkillAnimation(clip);
        _animator.SetFloat(AttackTimeHash, t);
        _animator.SetBool(AttackMirrorHash, _attack.Mirrored);
        _animator.SetFloat(AttackBlendHash, blendWeight);
        if (newAttack) _animator.SetTrigger(AttackHash);
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
                // ท่าท่อนบนไม่บล็อก -> ต่อยไปกระโดดไปได้
                bool fullBodyAttack = _attack != null && _attack.IsAttacking && !_upperActive;
                if (!fullBodyAttack) _animator.SetTrigger(_source.LastJumpFromGround ? JumpHash : AirJumpHash);
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

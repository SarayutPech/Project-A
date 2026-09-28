using System.Collections.Generic;
using UnityEngine;

// hitbox 1 อันของท่าโจมตี: ตรวจโดนครั้งเดียว ณ จังหวะ time
[System.Serializable]
public class AttackHitbox
{
    [Tooltip("จังหวะที่ตรวจโดน เป็นสัดส่วนของท่า (0 = เริ่มท่า, 1 = จบท่า) -> เร็วขึ้นตาม attack speed เอง\nhitbox ที่ time เท่ากัน = จังหวะเดียวกัน เป้าเดียวโดนได้ครั้งเดียวต่อจังหวะ")]
    [Range(0f, 1f)] public float time = 0.3f;
    [Tooltip("ศูนย์กลางวงตรวจโดน เทียบกับเท้า (x = ขวา, y = ขึ้น, z = หน้า ตามทิศที่ตี) คูณ scale ตัวละคร")]
    public Vector3 offset = new Vector3(0f, 0.9f, 0.6f);
    [Min(0.05f)] public float radius = 1f;
    [Tooltip("มุมกวาดด้านหน้า (องศา) 360 = รอบตัว")]
    [Range(10f, 360f)] public float arcAngle = 360f;
    [Tooltip("ตัวคูณดาเมจของ hitbox นี้ (คูณกับ Damage ของท่า)")]
    [Min(0f)] public float damageMultiplier = 1f;
}

// ตัวใช้สกิลโจมตีระยะประชิด (simulation ล้วน ตัดสินฝั่ง server)
// ค่าท่าทั้งหมดมาจาก ActiveSkillGem + support gem (ResolvedSkill) ตัวนี้เก็บแค่สถานะการเล่นท่า
// - player: PlayerSkills เรียก Configure ตาม gem ที่ใส่ช่อง
// - ศัตรู: ใช้ Default Skill ของตัวเอง (+ modifier จาก elite ผ่าน AddModifier)
//
// กด 1 ครั้ง = 1 ท่า มี hitbox ได้หลายอัน (หลายจังหวะ / หลายตำแหน่งพร้อมกัน)
// จังหวะโดนเป็นเวลาไม่ผูกกับ animation event -> server headless ที่ไม่มี Animator ก็ตัดสินได้เหมือนกัน
// attack speed ย่อ/ยืดทั้งท่า (duration, cooldown, จังหวะ hitbox)
// ท่า channel: กดค้าง = วนช่วง loopStart-loopEnd ไปเรื่อยๆ ปล่อย = เล่นท่าจบ
// ฝั่งภาพ (CharacterAnimator) ตั้งเวลา animation จาก NormalizedTime ตรงๆ -> ภาพตรงกับ hitbox เสมอ
//
// ผู้เรียก TryAttack (PlayerCombat / EnemyAI) ต้อง validate สิทธิ์เองก่อน (ตาย/ติดสถานะ) ตัวนี้เช็คแค่ cooldown กับการทับท่า
public class MeleeAttack : MonoBehaviour
{
    [Tooltip("สกิลตั้งต้น (ใช้เมื่อไม่มีใครเรียก Configure เช่นศัตรู) player ตั้งผ่าน PlayerSkills แทน")]
    public ActiveSkillGem defaultSkill;
    [Min(1)] public int defaultSkillLevel = 1;
    public LayerMask hitMask = ~0;

    // ค่าสุดท้ายของสกิลปัจจุบัน (null = ไม่มีสกิล ตีไม่ได้)
    public ResolvedSkill Skill
    {
        get
        {
            if (_skill == null && !_configured && defaultSkill != null) Configure(defaultSkill, defaultSkillLevel, null);
            return _skill;
        }
    }

    public bool IsAttacking { get; private set; }
    // นับครั้งที่เริ่มท่า (เพิ่มทีละ 1) ให้ฝั่งภาพรู้ว่าเพิ่งเริ่มท่าใหม่ / sync ผ่านเน็ตเป็นตัวเลขตัวเดียวได้
    public int AttackCount { get; private set; }
    // ทิศที่ตี (แนวนอน normalized) ล็อกไว้ตั้งแต่เริ่มท่า ตัวละครหมุนตามการเดินได้โดย hitbox ยังออกทางเดิม
    public Vector3 AimDirection { get; private set; } = Vector3.forward;
    // ความยาวท่าที่คิด attack speed แล้ว (ของท่าที่กำลังเล่น)
    public float CurrentDuration { get; private set; }
    // สกิลของท่าที่กำลังเล่น / เล่นล่าสุด (ฝั่งภาพใช้เลือก animation)
    public ResolvedSkill ActiveSkill => _active;
    public float MoveSpeedMultiplier => _active != null ? _active.MoveSpeedMultiplier : Skill != null ? Skill.MoveSpeedMultiplier : 1f;
    public bool Ready => IsReady(Skill);

    // ใช้สกิลนี้ได้ไหม (ไม่ได้อยู่ในท่า + สกิลนี้หมด cooldown) cooldown แยกต่อสกิล: สกิลหนึ่งติด cooldown ไม่บล็อกสกิลอื่น
    public bool IsReady(ResolvedSkill skill) => skill != null && !IsAttacking && CooldownRemaining(skill.Gem) <= 0f;

    public float CooldownRemaining(ActiveSkillGem gem) =>
        gem != null && _cooldownUntil.TryGetValue(gem, out float until) ? Mathf.Max(0f, until - _simTime) : 0f;
    // ตำแหน่งในท่า 0-1 (วนกลับตอน channel) ฝั่งภาพใช้ตั้งเวลา animation ให้ตรงกับ hitbox
    public float NormalizedTime => _t;
    // กำลังวนท่าอยู่ (กดค้าง + ยังไม่เกิน maxChannelTime)
    public bool IsChanneling => IsAttacking && CanLoop;
    // วนครบไปกี่รอบในท่านี้
    public int LoopCount { get; private set; }

    private bool CanLoop => _active != null && _active.Channel && _held && _active.LoopEnd > _active.LoopStart + 0.01f
                            && (_active.MaxChannelTime <= 0f || _channelTime < _active.MaxChannelTime);

    public event System.Action<MeleeAttack> Started;
    public event System.Action<MeleeAttack> Finished; // จบเอง หรือถูก Cancel
    public event System.Action<MeleeAttack, Health> Hit;
    // เปลี่ยนสกิล/ค่าสกิล (ฝั่งภาพใช้สลับท่า)
    public event System.Action<MeleeAttack> SkillChanged;

    private ResolvedSkill _skill;
    private ResolvedSkill _active; // สกิลของท่าที่กำลังเล่น (เปลี่ยน gem กลางท่าไม่กระทบท่าที่เล่นอยู่)
    private bool _configured;
    private ActiveSkillGem _gem;
    private int _gemLevel = 1;
    private readonly List<GemInstance> _supports = new List<GemInstance>();
    private readonly List<StatModifier> _extraModifiers = new List<StatModifier>();

    private Health _owner;
    private float _t;           // ตำแหน่งในท่า 0-1
    private bool _held;
    private float _channelTime;
    // เวลา simulation (สะสม fixedDeltaTime) ใช้นับ cooldown แบบ tick ไม่พึ่ง Time.time
    private float _simTime;
    private readonly Dictionary<ActiveSkillGem, float> _cooldownUntil = new Dictionary<ActiveSkillGem, float>();
    private int _nextHitbox;
    private readonly List<AttackHitbox> _ordered = new List<AttackHitbox>();
    private readonly Collider[] _overlap = new Collider[32];
    private readonly HashSet<Health> _hitThisStrike = new HashSet<Health>();
    private float _strikeTime = -1f;

    private void Awake() => _owner = GetComponent<Health>();

    // ---------- Skill setup (server) ----------

    // ตั้งสกิล + support gem (ช่องว่างส่ง default ได้) คำนวณค่าสุดท้ายใหม่
    public void Configure(ActiveSkillGem gem, int level, IEnumerable<GemInstance> supports)
    {
        _configured = true;
        _gem = gem;
        _gemLevel = level;
        _supports.Clear();
        if (supports != null) _supports.AddRange(supports);
        Rebuild();
    }

    // modifier ถาวรจากที่อื่นนอก gem (เช่น elite) มีผลกับทุกสกิลของตัวนี้
    public void AddModifier(StatModifier modifier)
    {
        _extraModifiers.Add(modifier);
        if (!_configured && defaultSkill != null) Configure(defaultSkill, defaultSkillLevel, null);
        else Rebuild();
    }

    private void Rebuild()
    {
        _skill = ResolvedSkill.Resolve(_gem, _gemLevel, _supports, _extraModifiers);
        SkillChanged?.Invoke(this);
    }

    // ---------- Attack ----------

    // ใช้สกิลตั้งต้น/ที่ Configure ไว้ (ศัตรู)
    public bool TryAttack(Vector3 aimDirection, bool held = false) => TryAttack(Skill, aimDirection, held);

    // ใช้สกิลที่ระบุ (player: สกิลของ slot ที่กด) held = กดค้างอยู่ตอนเริ่ม (ท่า channel จะวนจนกว่า SetHeld(false))
    public bool TryAttack(ResolvedSkill skill, Vector3 aimDirection, bool held = false)
    {
        if (!IsReady(skill)) return false;
        _active = skill;

        aimDirection.y = 0f;
        AimDirection = aimDirection.sqrMagnitude > 0.0001f ? aimDirection.normalized : FlatForward();
        CurrentDuration = _active.ScaledDuration;
        IsAttacking = true;
        _t = 0f;
        _held = held;
        _channelTime = 0f;
        LoopCount = 0;
        _nextHitbox = 0;
        _strikeTime = -1f;

        // เรียงตามจังหวะ จังหวะเท่ากันคงลำดับใน list
        // -> hitbox ที่อยู่ก่อนได้สิทธิ์ก่อน เช่นวางวงดาเมจแรงไว้ก่อนวงรอบตัว เป้าที่โดนวงแรงจะไม่โดนวงเบาซ้ำ
        var source = _active.Hitboxes;
        _ordered.Clear();
        foreach (var h in source) if (h != null) _ordered.Add(h);
        _ordered.Sort((a, b) => a.time != b.time ? a.time.CompareTo(b.time) : IndexOf(source, a).CompareTo(IndexOf(source, b)));

        AttackCount++;
        Started?.Invoke(this);
        return true;
    }

    private static int IndexOf(IReadOnlyList<AttackHitbox> list, AttackHitbox item)
    {
        for (int i = 0; i < list.Count; i++) if (list[i] == item) return i;
        return -1;
    }

    // ระหว่างท่า channel: ยังกดค้างอยู่ไหม (ปล่อย = เล่นรอบปัจจุบันจนถึง loopEnd แล้วต่อท่าจบ)
    public void SetHeld(bool held)
    {
        if (IsAttacking) _held = held;
    }

    // เปลี่ยนทิศตีระหว่าง channel (เช่นลากเมาส์ขณะหมุน) มีผลกับ hitbox จังหวะถัดไป
    public void SetAim(Vector3 aimDirection)
    {
        aimDirection.y = 0f;
        if (IsChanneling && aimDirection.sqrMagnitude > 0.0001f) AimDirection = aimDirection.normalized;
    }

    // ตำแหน่งในท่าล่วงหน้า ahead วินาที (ฝั่งภาพใช้เติมช่วงระหว่าง physics tick ให้ animation ลื่น ไม่กระตุกตาม 50Hz)
    public float PredictNormalizedTime(float ahead)
    {
        if (!IsAttacking) return _t;
        float t = _t + Mathf.Max(0f, ahead) / CurrentDuration;
        if (CanLoop && t >= _active.LoopEnd) t = _active.LoopStart + Mathf.Repeat(t - _active.LoopEnd, _active.LoopEnd - _active.LoopStart);
        return Mathf.Min(t, 1f);
    }

    public void Cancel()
    {
        if (!IsAttacking) return;
        End();
    }

    private void End()
    {
        IsAttacking = false;
        _cooldownUntil[_active.Gem] = _simTime + _active.ScaledCooldown;
        Finished?.Invoke(this);
    }

    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        _simTime += dt;
        if (!IsAttacking) return;

        float t = _t + dt / CurrentDuration;
        bool looping = CanLoop;
        if (looping) _channelTime += dt;

        // กดค้างถึงท้ายช่วงวน: ตีให้ครบจังหวะก่อน loopEnd แล้วย้อนกลับ loopStart (hitbox ในช่วงวนพร้อมโดนใหม่)
        if (looping && t >= _active.LoopEnd)
        {
            ProcessHits(_active.LoopEnd, true);
            t = _active.LoopStart + Mathf.Repeat(t - _active.LoopEnd, _active.LoopEnd - _active.LoopStart);
            _nextHitbox = FirstHitboxAtOrAfter(_active.LoopStart);
            _strikeTime = -1f;
            LoopCount++;
        }

        _t = Mathf.Min(t, 1f);
        ProcessHits(_t, false);
        if (IsAttacking && t >= 1f) End();
    }

    // ตีทุก hitbox ที่ถึงจังหวะแล้ว (exclusive = ไม่รวมจังหวะที่ตรง limit พอดี)
    private void ProcessHits(float limit, bool exclusive)
    {
        while (IsAttacking && _nextHitbox < _ordered.Count)
        {
            float time = _ordered[_nextHitbox].time;
            if (exclusive ? time >= limit : time > limit) break;
            DoHit(_ordered[_nextHitbox++]);
        }
    }

    private int FirstHitboxAtOrAfter(float time)
    {
        for (int i = 0; i < _ordered.Count; i++)
            if (_ordered[i].time >= time) return i;
        return _ordered.Count;
    }

    private float Scale => Mathf.Max(transform.lossyScale.x, transform.lossyScale.z);

    private Vector3 HitCenter(AttackHitbox h, Vector3 aim, float radiusMul)
    {
        // พื้นที่ใหญ่ขึ้น = วงขยายออกจากตัวด้วย (วงหน้าตัวไม่จมเข้าหาตัวเมื่อรัศมีโต)
        Vector3 right = Vector3.Cross(Vector3.up, aim);
        Vector3 local = right * h.offset.x * radiusMul + Vector3.up * h.offset.y + aim * h.offset.z * radiusMul;
        return transform.position + local * Scale;
    }

    private void DoHit(AttackHitbox h)
    {
        // จังหวะใหม่ -> ล้างรายชื่อเป้าที่โดนแล้ว (hitbox ที่ time เท่ากันแชร์รายชื่อ ไม่โดนซ้อน)
        if (!Mathf.Approximately(h.time, _strikeTime))
        {
            _strikeTime = h.time;
            _hitThisStrike.Clear();
        }

        float radiusMul = _active.RadiusMultiplier;
        // ใช้ PhysicsScene ของ scene ตัวเอง รองรับหลาย instance ใน process เดียว (กฎข้อ 6)
        int count = gameObject.scene.GetPhysicsScene().OverlapSphere(HitCenter(h, AimDirection, radiusMul), h.radius * radiusMul * Scale,
            _overlap, hitMask, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            var target = _overlap[i].GetComponentInParent<Health>();
            if (target == null || target == _owner || target.IsDead) continue;
            if (_owner != null && target.team == _owner.team) continue;
            if (_hitThisStrike.Contains(target)) continue;

            Vector3 toTarget = target.transform.position - transform.position;
            toTarget.y = 0f;
            if (h.arcAngle < 360f && toTarget.sqrMagnitude > 0.0001f && Vector3.Angle(AimDirection, toTarget) > h.arcAngle * 0.5f) continue;

            _hitThisStrike.Add(target);
            Vector3 dir = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : AimDirection;
            if (target.ApplyDamage(new DamageInfo { amount = _active.Damage * h.damageMultiplier, source = gameObject, direction = dir }))
                Hit?.Invoke(this, target);
        }
    }

    private Vector3 FlatForward()
    {
        Vector3 f = transform.forward;
        f.y = 0f;
        return f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.forward;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        // ตอน Edit ยังไม่มี skill ที่ resolve -> วาดจาก defaultSkill (player ไม่มี default ก็ไม่วาด)
        ActiveSkillGem gem = _active != null ? _active.Gem : _skill != null ? _skill.Gem : defaultSkill;
        if (gem == null || gem.hitboxes == null) return;
        float radiusMul = _skill != null ? _skill.RadiusMultiplier : 1f;
        Vector3 aim = IsAttacking ? AimDirection : FlatForward();
        float t = IsAttacking ? _t : -1f;
        foreach (var h in gem.hitboxes)
        {
            if (h == null) continue;
            // ตอนเล่น: hitbox ที่เพิ่งตรวจไป (ภายใน 0.1 ของท่า) เป็นสีแดง
            bool recent = t >= h.time && t - h.time < 0.1f;
            Gizmos.color = recent ? new Color(1f, 0.1f, 0.1f, 1f) : new Color(1f, 0.6f, 0.2f, 0.5f);
            Gizmos.DrawWireSphere(HitCenter(h, aim, radiusMul), h.radius * radiusMul * Scale);
        }
    }
#endif
}

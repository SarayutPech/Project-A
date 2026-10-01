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

    [Header("Teleport (Flicker Strike)")]
    [Tooltip("layer ของพื้น/กำแพงที่ใช้หาจุดลงวาร์ป (ตัวละครถูกข้ามเอง)")]
    public LayerMask teleportGroundMask = ~0;
    [Tooltip("จุดลงต้องสูงต่างจากพื้นใต้เป้าไม่เกินนี้ (world unit) กันลงชั้นอื่นข้างหน้าผา / นอกแผนที่ / ในกำแพง")]
    [Min(0.05f)] public float teleportMaxStep = 0.5f;

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
    // ท่าที่สุ่มได้ของครั้งนี้ + กลับข้างไหม (ตัดสินฝั่ง simulation -> ภายหลัง sync เป็นเลข/บูลตัวเดียว ทุก client เห็นท่าเดียวกัน)
    public int AnimationVariant { get; private set; }
    public bool Mirrored { get; private set; }
    // น้ำหนักท่าผสม (blendAnimation) ของครั้งนี้
    public float BlendWeight { get; private set; }
    // นับครั้งที่เลือกท่าใหม่ (เริ่มท่า + ทุกรอบที่ channel วนแล้วสุ่มใหม่) ฝั่งภาพเทียบค่าเก่าเพื่อรู้ว่าต้องเปลี่ยนท่า
    public int PresentationCount { get; private set; }
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
    // สกิลกำลังวาร์ป (from, to = ตำแหน่งเท้า) เรียกก่อนย้ายตัว -> ฝั่งภาพทิ้งเงา/effect ที่จุดเดิมได้
    public event System.Action<MeleeAttack, Vector3, Vector3> Teleporting;
    // เป้าล่าสุดที่วาร์ปไปหา
    public Health TeleportTarget { get; private set; }

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
    // สุ่มท่า/ข้าง (ไม่แตะ UnityEngine.Random) + จำท่าล่าสุดของแต่ละสกิลไว้กันซ้ำ / สลับข้าง
    private readonly System.Random _rng = new System.Random();
    private readonly Dictionary<ActiveSkillGem, (int variant, bool mirrored)> _lastPick = new Dictionary<ActiveSkillGem, (int, bool)>();
    private int _nextHitbox;
    private readonly List<AttackHitbox> _ordered = new List<AttackHitbox>();
    private readonly Collider[] _overlap = new Collider[32];
    private readonly HashSet<Health> _hitThisStrike = new HashSet<Health>();
    private float _strikeTime = -1f;

    private ISkillMover _mover;
    // จุดที่ผู้เล่นเล็ง (เคอร์เซอร์บนพื้น) ใช้เลือกเป้าวาร์ป / ไม่มี = หน้าตัว
    private Vector3? _aimPoint;

    private void Awake()
    {
        _owner = GetComponent<Health>();
        _mover = GetComponent<ISkillMover>();
    }

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
    // aimPoint = จุดที่เล็ง (ใช้เลือกเป้าของสกิลวาร์ป) ผู้เรียกต้อง validate ว่าเป็นตัวเลขจริง
    public bool TryAttack(ResolvedSkill skill, Vector3 aimDirection, bool held = false, Vector3? aimPoint = null)
    {
        if (!IsReady(skill)) return false;

        aimDirection.y = 0f;
        aimDirection = aimDirection.sqrMagnitude > 0.0001f ? aimDirection.normalized : FlatForward();
        _aimPoint = aimPoint;

        // สกิลวาร์ป: ต้องมีเป้าในระยะ ไม่งั้นใช้ไม่ได้ (เช็คก่อนเริ่มท่า)
        Health target = null;
        Vector3 teleportLand = default;
        if (skill.TeleportToTarget)
        {
            if (_mover == null) return false;
            target = FindTeleportTarget(skill, AimPointOr(aimDirection, skill), out teleportLand);
            if (target == null) return false;
        }

        _active = skill;
        PickPresentation(skill);
        AimDirection = aimDirection;
        if (target != null) TeleportNextTo(target, teleportLand);
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

    // สุ่มท่า (ไม่ซ้ำท่าเดิมติดกันถ้ามีหลายท่า) + กลับข้างตาม Mirror ของสกิล
    private void PickPresentation(ResolvedSkill skill)
    {
        bool hasLast = _lastPick.TryGetValue(skill.Gem, out var last);
        int count = skill.AnimationCount;
        int variant = 0;
        if (count > 1)
        {
            variant = _rng.Next(hasLast ? count - 1 : count);
            if (hasLast && variant >= last.variant) variant++; // ข้ามท่าล่าสุด
        }

        bool mirrored = skill.Mirror switch
        {
            SkillMirrorMode.Alternate => hasLast ? !last.mirrored : false,
            SkillMirrorMode.Random => _rng.Next(2) == 1,
            _ => false,
        };

        AnimationVariant = variant;
        Mirrored = mirrored;
        Vector2 range = skill.BlendWeightRange;
        BlendWeight = skill.BlendAnimation != null ? Mathf.Clamp01(Mathf.Lerp(range.x, range.y, (float)_rng.NextDouble())) : 0f;
        _lastPick[skill.Gem] = (variant, mirrored);
        PresentationCount++;
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

    // เปลี่ยนทิศตีระหว่าง channel (เช่นลากเมาส์ขณะหมุน) มีผลกับ hitbox จังหวะถัดไป / aimPoint ใช้เลือกเป้าวาร์ปรอบถัดไป
    public void SetAim(Vector3 aimDirection, Vector3? aimPoint = null)
    {
        aimDirection.y = 0f;
        if (!IsChanneling) return;
        if (aimPoint.HasValue) _aimPoint = aimPoint;
        // สกิลวาร์ปหันหาเป้าที่วาร์ปไปแล้ว ไม่หันตามเมาส์
        if (_active.TeleportToTarget) return;
        if (aimDirection.sqrMagnitude > 0.0001f) AimDirection = aimDirection.normalized;
    }

    // ---------- Teleport (Flicker Strike) ----------

    private Vector3 AimPointOr(Vector3 aimDirection, ResolvedSkill skill) =>
        _aimPoint ?? transform.position + aimDirection * (skill.TeleportRange * 0.5f);

    // เป้าวาร์ป: ศัตรูที่ยังไม่ตายในระยะจากตัว และมีจุดลงข้างตัวที่ยืนได้จริง (land = ตำแหน่งเท้าตอนลง)
    // Avoid Same Target: เลือกตัวที่วาร์ปไปหาครั้งล่าสุดนานที่สุดก่อน (ไม่เคย = ก่อนสุด) -> กดค้างแล้ววนไปทั่วทั้งกลุ่ม
    // เสมอกัน / ปิด avoid = ตัวที่ใกล้จุดเล็งที่สุด / ตัวที่หาจุดลงไม่ได้ (ลอย ตกใต้แผนที่ ติดกำแพง) ข้ามไปตัวถัดไป
    // ตัดสินฝั่ง server เอง จุดเล็งจาก client เป็นแค่ตัวช่วยเลือก ระยะจำกัดด้วยค่าของ gem (กฎข้อ 8)
    private Health FindTeleportTarget(ResolvedSkill skill, Vector3 aimPoint, out Vector3 land)
    {
        land = default;
        float range = skill.TeleportRange * Scale;
        Vector3 origin = transform.position;

        _teleportCandidates.Clear();
        foreach (var h in Health.All)
        {
            if (h == null || h == _owner || h.IsDead || !h.isActiveAndEnabled) continue;
            if (_owner != null && h.team == _owner.team) continue;
            Vector3 p = h.transform.position;
            if (FlatDistance(origin, p) > range || Mathf.Abs(p.y - origin.y) > range) continue;

            float last = skill.AvoidSameTarget && _flickedAt.TryGetValue(h, out float at) ? at : float.NegativeInfinity;
            _teleportCandidates.Add((h, last, FlatDistance(aimPoint, p)));
        }

        _teleportCandidates.Sort((x, y) => x.last != y.last ? x.last.CompareTo(y.last) : x.dist.CompareTo(y.dist));
        foreach (var c in _teleportCandidates)
            if (TryFindLanding(c.health, skill, out land)) return c.health;
        return null;
    }

    private readonly List<(Health health, float last, float dist)> _teleportCandidates = new List<(Health, float, float)>();
    private readonly RaycastHit[] _groundHits = new RaycastHit[16];

    // ทิศที่ลองวางจุดลงรอบเป้า (องศาเทียบฝั่งที่หันเข้าหาจากจุดเดิม) ฝั่งเดิมก่อน แล้วค่อยๆ อ้อมไปด้านหลัง
    private static readonly float[] LandingAngles = { 0f, 45f, -45f, 90f, -90f, 135f, -135f, 180f };

    // หาจุดลงข้างเป้าที่มีพื้นระดับเดียวกับพื้นใต้เป้า และตัวเราไม่ทับกำแพง/หน้าผา
    // (เดิมวางที่ระดับเท้าเป้าตรงๆ -> เป้ายืนชิดหน้าผา จุดลงไปอยู่ใต้ผิวชั้นบน/นอกแผนที่ แล้วตกทะลุ)
    private bool TryFindLanding(Health target, ResolvedSkill skill, out Vector3 land)
    {
        land = default;
        PhysicsScene physics = gameObject.scene.GetPhysicsScene();
        Vector3 to = target.transform.position;
        var targetCol = target.GetComponent<Collider>();
        float targetFoot = targetCol != null && targetCol.enabled ? targetCol.bounds.min.y : to.y;

        // พื้นใต้เป้า: ไม่มี = เป้าลอยสูง/ตกใต้แผนที่ ไปหาไม่ได้
        if (!TryGroundAt(physics, new Vector3(to.x, targetFoot + 0.5f, to.z), 3f, out float groundY)) return false;

        Vector3 dir = to - transform.position;
        dir.y = 0f;
        dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : FlatForward();
        float gap = RadiusOf(target) + RadiusOf(_owner) + skill.TeleportLandingGap;

        // ยิงลงจากสูงพอจะชนหลังที่ราบชั้นที่สูงกว่า (จะได้รู้ว่าเป็นชั้นอื่น ไม่ใช่ลงไปจมใต้ผิว)
        const float probeUp = 3f;
        foreach (float angle in LandingAngles)
        {
            Vector3 p = to - Quaternion.AngleAxis(angle, Vector3.up) * dir * gap;
            if (!TryGroundAt(physics, new Vector3(p.x, groundY + probeUp, p.z), probeUp + teleportMaxStep, out float y)) continue;
            if (Mathf.Abs(y - groundY) > teleportMaxStep) continue;

            p.y = y;
            if (!HasClearance(physics, p)) continue;
            land = p;
            return true;
        }
        return false;
    }

    // ผิวพื้นแรกใต้ origin (ข้ามตัวละคร/trigger และผิวที่ชันเกินยืน)
    private bool TryGroundAt(PhysicsScene physics, Vector3 origin, float distance, out float y)
    {
        y = 0f;
        int count = physics.Raycast(origin, Vector3.down, _groundHits, distance, teleportGroundMask, QueryTriggerInteraction.Ignore);
        int nearest = -1;
        for (int i = 0; i < count; i++)
        {
            if (IsCharacter(_groundHits[i].collider)) continue;
            if (nearest < 0 || _groundHits[i].distance < _groundHits[nearest].distance) nearest = i;
        }
        // ผิวแรกที่ชนชันเกินยืน (ผนัง/ขอบหน้าผา) = ใช้ไม่ได้
        if (nearest < 0 || _groundHits[nearest].normal.y < 0.6f) return false;
        y = _groundHits[nearest].point.y;
        return true;
    }

    // capsule ของตัวเราวางที่เท้า foot แล้วไม่ทับอะไรที่ไม่ใช่ตัวละคร (ยกขึ้นจากพื้นนิดนึง พื้นที่ยืนไม่นับ)
    private bool HasClearance(PhysicsScene physics, Vector3 foot)
    {
        float radius = RadiusOf(_owner) * 0.9f;
        float height = 1.8f;
        var cap = _owner != null ? _owner.GetComponent<CapsuleCollider>() : null;
        if (cap != null) height = cap.height * Mathf.Abs(_owner.transform.lossyScale.y);
        height = Mathf.Max(height, radius * 2f + 0.1f);

        Vector3 bottom = foot + Vector3.up * (radius + 0.1f);
        Vector3 top = foot + Vector3.up * (height - radius);
        int count = physics.OverlapCapsule(bottom, top, radius, _overlap, teleportGroundMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
            if (!IsCharacter(_overlap[i])) return false;
        return true;
    }

    private static bool IsCharacter(Collider col) => col.GetComponentInParent<Health>() != null;

    // เวลา (sim) ที่วาร์ปไปหาแต่ละตัวครั้งล่าสุด
    private readonly Dictionary<Health, float> _flickedAt = new Dictionary<Health, float>();
    private readonly List<Health> _staleFlicks = new List<Health>();

    private void RememberFlick(Health target)
    {
        _flickedAt[target] = _simTime;
        if (_flickedAt.Count < 64) return;
        // ล้างตัวที่ตาย/หายไปแล้ว กัน dictionary โตไม่หยุด
        _staleFlicks.Clear();
        foreach (var kv in _flickedAt) if (kv.Key == null || kv.Key.IsDead) _staleFlicks.Add(kv.Key);
        foreach (var h in _staleFlicks) _flickedAt.Remove(h);
    }

    // วาร์ปไปยืนที่ land (จาก TryFindLanding) แล้วหันหาเป้า
    private void TeleportNextTo(Health target, Vector3 land)
    {
        Vector3 from = transform.position;
        Vector3 dir = target.transform.position - land;
        dir.y = 0f;
        dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : FlatForward();

        Teleporting?.Invoke(this, from, land);
        _mover.SkillTeleport(land, dir);
        AimDirection = dir;
        TeleportTarget = target;
        RememberFlick(target);
    }

    private static float RadiusOf(Health h)
    {
        if (h == null) return 0.5f;
        var cap = h.GetComponent<CapsuleCollider>();
        if (cap == null) return 0.5f;
        Vector3 s = h.transform.lossyScale;
        return cap.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z));
    }

    private static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

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
            // มีหลายท่า/กลับข้างได้ -> รอบใหม่สุ่มท่าใหม่ (กดค้างแล้วไม่ออกท่าเดิมซ้ำ)
            if (_active.HasPresentationVariety) PickPresentation(_active);

            // สกิลวาร์ป: ทุกรอบวาร์ปไปตัวถัดไป ไม่มีเป้าเหลือ = เลิกวน เล่นท่าจบ
            if (_active.TeleportToTarget)
            {
                Vector3 land = default;
                Health next = _mover != null ? FindTeleportTarget(_active, AimPointOr(AimDirection, _active), out land) : null;
                if (next != null) TeleportNextTo(next, land);
                else _held = false;
            }
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

    // ---------- Shadow Clones ----------

    // จำนวนร่างเงาที่ตีอยู่ตอนนี้ (ฝั่งภาพอ่านค่านี้ไปแสดง -> ภายหลัง sync ผ่านเน็ตได้เพราะคำนวณจาก state ที่ sync อยู่แล้ว)
    public int ActiveCloneCount
    {
        get
        {
            if (!IsAttacking || _active == null || _active.ShadowCloneCount <= 0) return 0;
            if (_active.ShadowClonesOnlyWhileHeld && !IsChanneling) return 0;
            return _active.ShadowCloneCount;
        }
    }

    // ตำแหน่งเท้าของร่างที่ index: คู่ซ้าย/ขวาเยื้องจากทิศที่ตี คู่ถัดไปกางออกกว้างขึ้น
    // ใช้ทั้ง hitbox ของร่าง (server) และตำแหน่งภาพของร่าง (client) -> ตรงกันเสมอ
    public Vector3 GetClonePosition(int index)
    {
        if (_active == null) return transform.position;
        int pair = index / 2;
        float side = index % 2 == 0 ? 1f : -1f;
        float angle = side * Mathf.Min(170f, _active.ShadowCloneAngle * (1f + pair * 0.7f));
        Vector3 dir = Quaternion.Euler(0f, angle, 0f) * AimDirection;
        float dist = _active.ShadowCloneDistance * (1f + pair * 0.25f) * Scale;
        return transform.position + dir * dist;
    }

    // ร่างที่ index ต่อยข้างตรงข้ามกับร่างก่อนหน้า (ซ้าย/ขวาสลับกันรัวๆ)
    public bool IsCloneMirrored(int index) => Mirrored ^ (index % 2 == 0);

    // ทิศที่ร่างหัน/ต่อย: เข้าหาจุดรวมหน้าตัวเรา (ห่างเท่าระยะร่าง) -> ทุกร่างรุมต่อยเป้าเดียวกับเรา
    public Vector3 GetCloneAim(int index)
    {
        if (_active == null) return AimDirection;
        Vector3 focus = transform.position + AimDirection * (_active.ShadowCloneDistance * Scale);
        Vector3 dir = focus - GetClonePosition(index);
        dir.y = 0f;
        return dir.sqrMagnitude > 0.0001f ? dir.normalized : AimDirection;
    }

    private readonly List<HashSet<Health>> _cloneHits = new List<HashSet<Health>>();

    private Vector3 HitCenter(AttackHitbox h, Vector3 origin, Vector3 aim, bool mirrored, float radiusMul)
    {
        // พื้นที่ใหญ่ขึ้น = วงขยายออกจากตัวด้วย (วงหน้าตัวไม่จมเข้าหาตัวเมื่อรัศมีโต)
        // ท่ากลับข้าง (mirror) -> hitbox ฝั่งซ้าย/ขวากลับตาม
        Vector3 right = Vector3.Cross(Vector3.up, aim) * (mirrored ? -1f : 1f);
        Vector3 local = right * h.offset.x * radiusMul + Vector3.up * h.offset.y + aim * h.offset.z * radiusMul;
        return origin + local * Scale;
    }

    private Vector3 HitCenter(AttackHitbox h, Vector3 aim, float radiusMul) => HitCenter(h, transform.position, aim, Mirrored, radiusMul);

    private void DoHit(AttackHitbox h)
    {
        int clones = ActiveCloneCount;
        while (_cloneHits.Count < clones) _cloneHits.Add(new HashSet<Health>());

        // จังหวะใหม่ -> ล้างรายชื่อเป้าที่โดนแล้ว (hitbox ที่ time เท่ากันแชร์รายชื่อ ไม่โดนซ้อน)
        // ร่างเงาแต่ละร่างมีรายชื่อของตัวเอง -> เป้าเดียวโดนได้จากทั้งเราและทุกร่าง (ดาเมจเพิ่มจริง)
        if (!Mathf.Approximately(h.time, _strikeTime))
        {
            _strikeTime = h.time;
            _hitThisStrike.Clear();
            foreach (var set in _cloneHits) set.Clear();
        }

        HitFrom(h, transform.position, AimDirection, Mirrored, _active.Damage, _hitThisStrike);
        for (int c = 0; c < clones; c++)
            HitFrom(h, GetClonePosition(c), GetCloneAim(c), IsCloneMirrored(c), _active.Damage * _active.ShadowCloneDamage, _cloneHits[c]);
    }

    private void HitFrom(AttackHitbox h, Vector3 origin, Vector3 aim, bool mirrored, float damage, HashSet<Health> alreadyHit)
    {
        if (damage <= 0f) return;
        float radiusMul = _active.RadiusMultiplier;
        // ใช้ PhysicsScene ของ scene ตัวเอง รองรับหลาย instance ใน process เดียว (กฎข้อ 6)
        int count = gameObject.scene.GetPhysicsScene().OverlapSphere(HitCenter(h, origin, aim, mirrored, radiusMul), h.radius * radiusMul * Scale,
            _overlap, hitMask, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            var target = _overlap[i].GetComponentInParent<Health>();
            if (target == null || target == _owner || target.IsDead) continue;
            if (_owner != null && target.team == _owner.team) continue;
            if (alreadyHit.Contains(target)) continue;

            Vector3 toTarget = target.transform.position - origin;
            toTarget.y = 0f;
            if (h.arcAngle < 360f && toTarget.sqrMagnitude > 0.0001f && Vector3.Angle(aim, toTarget) > h.arcAngle * 0.5f) continue;

            alreadyHit.Add(target);
            Vector3 dir = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : aim;
            if (target.ApplyDamage(new DamageInfo { amount = damage * h.damageMultiplier, source = gameObject, direction = dir }))
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

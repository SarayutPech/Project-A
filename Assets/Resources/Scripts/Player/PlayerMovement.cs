using UnityEngine;
using UnityEngine.InputSystem;

// เดินด้วย Rigidbody (gravity จาก physics) ทิศตามมุมกล้อง (กด W = เดินขึ้นจอ เหมาะกับกล้อง isometric)
// ปุ่มเริ่มต้น Keyboard: WASD / ลูกศร, Space = กระโดด, Ctrl = แตะ dash / กดค้าง วิ่ง
//             Gamepad: left stick, ปุ่มล่าง (A/Cross) = กระโดด, ปุ่มซ้าย (X/Square) = แตะ dash / กดค้าง วิ่ง
// (ปิด Dash Sprint Same Key = แยกปุ่มแบบเดิม: Shift / left stick click = วิ่ง, Ctrl / ปุ่มซ้าย = dash ตอนกด)
// ถ้ามี ProceduralMapGenerator ในฉาก จะวาง player ที่จุด Start และล็อกไว้จน MapBuildAnimator เล่นจบ
//
// บ่อน้ำ/รู: ถ้า config เปิด Walkable Water จะลุยน้ำได้เหมือนพื้นปกติ (generator สร้างพื้นลาดล่องหนให้ ไม่มี trigger respawn)
// ถ้าปิด: เดินเข้าไม่ได้ (ชนกำแพง Hole Blockers ของ generator) แต่ตอนกระโดด/dash จะทะลุกำแพงนี้ข้ามไปได้
// ถ้าข้ามไม่พ้นแล้วตกน้ำ (หรือตกต่ำกว่า respawnBelowY) จะกลับไปยืนจุดปลอดภัยล่าสุด
[RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    [Min(0f)] public float moveSpeed = 5f;
    [Min(1f)] public float sprintMultiplier = 1.6f;
    [Tooltip("อัตราเร่ง/เบรกบนพื้น (หน่วย/วินาที²) ยิ่งมากยิ่งหยุด/ออกตัวไว")]
    [Min(0f)] public float groundAcceleration = 60f;
    [Tooltip("อัตราเร่งกลางอากาศ (บังคับทิศตอนลอยได้แค่ไหน)")]
    [Min(0f)] public float airAcceleration = 20f;
    [Tooltip("ความเร็วหันตัวตามทิศที่เดิน")]
    [Min(0f)] public float rotationSpeed = 12f;

    [Header("Jump")]
    [Tooltip("ความสูงกระโดด (world unit) คำนวณแรงจาก Physics.gravity ให้เอง")]
    [Min(0f)] public float jumpHeight = 1.5f;
    [Tooltip("ตัวคูณ gravity ตอนตก ให้กระโดดดูหนักแน่นไม่ลอยนาน (1 = gravity ปกติ)")]
    [Min(1f)] public float fallGravityMultiplier = 2f;
    [Tooltip("ยังกระโดดได้หลังเดินพ้นขอบมาแล้วกี่วินาที (coyote time)")]
    [Min(0f)] public float coyoteTime = 0.1f;
    [Tooltip("กดกระโดดก่อนถึงพื้นได้กี่วินาที แล้วจะกระโดดทันทีที่แตะพื้น")]
    [Min(0f)] public float jumpBufferTime = 0.1f;
    [Tooltip("จำนวนครั้งที่กระโดดได้ก่อนแตะพื้น (1 = กระโดดปกติ, 2 = double jump, 3 = triple ...)")]
    [Min(1)] public int maxJumps = 2;
    [Tooltip("ความสูงของครั้งที่กระโดดกลางอากาศ (ครั้งที่ 2 ขึ้นไป)")]
    [Min(0f)] public float airJumpHeight = 1.2f;
    [Tooltip("กดปุ่มกระโดดค้าง = ถึงพื้นแล้วกระโดดต่อเองเรื่อยๆ (ครั้งกลางอากาศยังต้องกดใหม่ทุกครั้ง)")]
    public bool holdToAutoJump = true;

    [Header("Dash")]
    [Tooltip("ระยะพุ่งต่อ 1 ครั้ง (world unit) ระหว่าง dash ไม่มี gravity จึงพุ่งข้ามน้ำได้เท่าระยะนี้")]
    [Min(0f)] public float dashDistance = 4f;
    [Tooltip("เวลาที่ใช้พุ่ง (วินาที) ยิ่งน้อยยิ่งเร็ว")]
    [Min(0.01f)] public float dashDuration = 0.18f;
    [Tooltip("ต้องรอเท่านี้ก่อน dash ครั้งถัดไป (วินาที)")]
    [Min(0f)] public float dashCooldown = 0.5f;
    [Tooltip("dash กลางอากาศได้ 1 ครั้งต่อการลอย (กระโดด + dash ข้ามบ่อกว้างได้)")]
    public bool allowAirDash = true;

    [Header("Dash + Sprint ปุ่มเดียวกัน")]
    [Tooltip("ใช้ปุ่ม Dash ปุ่มเดียว: กดแล้วปล่อย = dash (เกิดตอนปล่อย) / กดค้างครบ Sprint Hold Time = วิ่งจนกว่าจะปล่อย (ไม่ dash)\nKeyboard ใช้ Dash Key (Sprint Key ไม่ใช้) / Gamepad ใช้ปุ่มซ้าย (X/Square)")]
    public bool dashSprintSameKey = true;
    [Tooltip("ต้องกดค้างนานเท่านี้ (วินาที) ถึงเริ่มวิ่ง ปล่อยก่อนนี้ = dash")]
    [Min(0.05f)] public float sprintHoldTime = 1f;

    [Header("Input Keys (Keyboard)")]
    public Key jumpKey = Key.Space;
    public Key sprintKey = Key.LeftShift;
    public Key dashKey = Key.LeftCtrl;

    [Header("Ground Check")]
    [Tooltip("layer ที่นับเป็นพื้น (ตัว player เองถูกข้ามให้อัตโนมัติ)")]
    public LayerMask groundLayers = ~0;
    [Tooltip("ระยะตรวจพื้นใต้เท้า")]
    [Min(0.01f)] public float groundCheckDistance = 0.15f;
    [Tooltip("ชันเกินกี่องศาไม่นับเป็นพื้น (นับเป็นกำแพง) ทางลาดของแผนที่ ≈ 27°")]
    [Range(10f, 80f)] public float maxSlopeAngle = 50f;
    [Tooltip("หลุดพื้นเองโดยไม่ได้กระโดด (วิ่งเร็วพ้นหัวทางลาด) ถ้าพื้นอยู่ใต้เท้าไม่เกินนี้จะดูดกลับลงพื้น ไม่เด้งลอย (0 = ปิด) ต่ำกว่าความสูงหน้าผาไว้ ไม่งั้นเดินตกหน้าผาแล้ววาร์ปลง")]
    [Min(0f)] public float groundSnapDistance = 0.6f;

    [Header("Camera Relative")]
    [Tooltip("ถ้าเว้นว่างจะใช้ Camera.main")]
    public Transform cameraTransform;

    [Header("Map Spawn")]
    [Tooltip("ถ้าเว้นว่างจะใช้ ProceduralMapGenerator.Instance")]
    public ProceduralMapGenerator generator;
    [Tooltip("ถ้าเว้นว่างจะหาในฉากเอง ถ้าไม่มี animator เลย player จะเดินได้ทันที")]
    public MapBuildAnimator buildAnimator;
    [Tooltip("วาง player ที่จุด Start ของแผนที่ทุกครั้งที่ generate")]
    public bool spawnAtMapStart = true;
    [Tooltip("ยกให้สูงจากผิวพื้นเท่านี้ตอน spawn กันจมพื้น")]
    [Min(0f)] public float spawnHeightOffset = 0.05f;
    [Tooltip("ห้ามเดิน (และไม่มี gravity) จนแผนที่ขึ้นครบ")]
    public bool lockUntilMapBuilt = true;
    [Tooltip("ซ่อน renderer ของ player จนแผนที่ขึ้นครบ")]
    public bool hideUntilMapBuilt = true;

    [Header("Fall Safety")]
    [Tooltip("ถ้าตกต่ำกว่านี้ (เช่นหลุดขอบ/ตกรู) จะกลับไปยืนจุดปลอดภัยล่าสุด")]
    public float respawnBelowY = -20f;
    [Tooltip("จำจุดปลอดภัย (ไว้ respawn ตอนตกน้ำ) เฉพาะตอนมีพื้นรอบตัวห่างจากขอบตัวอย่างน้อยเท่านี้ กันเกิดเกยขอบบ่อแล้วตกซ้ำ")]
    [Min(0f)] public float safeEdgeMargin = 0.5f;

    // ล็อกแล้ว Rigidbody จะเป็น kinematic (ไม่โดน gravity / ไม่ถูกชนกระเด็น)
    public bool CanMove
    {
        get => _canMove;
        set
        {
            _canMove = value;
            _dashKeyDownTime = -1f; // ล็อก/ปลดล็อกกลางกดค้าง ไม่ให้ปล่อยปุ่มทีหลังแล้วนับเป็น dash
            _sprint = false;
            if (_body == null) return;
            if (!value)
            {
                EndDash(false);
                if (!_body.isKinematic) _body.linearVelocity = Vector3.zero;
            }
            _body.isKinematic = !value;
        }
    }

    public bool IsGrounded { get; private set; }
    public bool IsDashing => _dashTimeLeft > 0f;

    // Singleton: player มีได้ตัวเดียวในฉาก ตัวที่เกินมาจะถูกลบทิ้ง
    public static PlayerMovement Instance { get; private set; }

    // ลอยนานกว่านี้ (ไม่ได้กระโดด เช่นตกจากที่สูง) ก็นับเป็นกลางอากาศ ทะลุกำแพงบ่อได้
    private const float AirborneGrace = 0.1f;

    private bool _canMove = true;
    private Rigidbody _body;
    private CapsuleCollider _capsule;
    private Renderer[] _renderers;
    private Vector2 _moveInput;
    private bool _sprint;
    private float _lastGroundedTime = float.NegativeInfinity;
    private float _lastJumpPressedTime = float.NegativeInfinity;
    private bool _jumpedSinceGrounded;
    private int _jumpsUsed; // กระโดดไปแล้วกี่ครั้งตั้งแต่แตะพื้นล่าสุด
    private bool _jumpHeld;

    private bool _dashRequested;
    private float _dashKeyDownTime = -1f; // เวลาที่เริ่มกดปุ่ม dash/sprint (-1 = ไม่ได้กดอยู่)
    private float _dashTimeLeft;
    private Vector3 _dashDir;
    private float _lastDashTime = float.NegativeInfinity;
    private bool _airDashUsed;

    private Vector3 _groundNormal = Vector3.up;
    private readonly RaycastHit[] _wallHits = new RaycastHit[8];

    private Collider[] _holeBlockers = new Collider[0];
    private bool _ignoringHoleBlockers;
    private Vector3 _lastSafePosition;
    private bool _hasSafePosition;

    private ProceduralMapGenerator _subscribedGenerator;
    private MapBuildAnimator _subscribedAnimator;

    private ProceduralMapGenerator Generator => generator != null ? generator : ProceduralMapGenerator.Instance;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"[{nameof(PlayerMovement)}] มี player อยู่แล้วที่ '{Instance.name}' จึงลบ '{name}' (Singleton)", this);
            enabled = false; // Destroy ทำงานท้ายเฟรม ปิดไว้ก่อนกัน Start/Update ของตัวซ้ำทำงาน
            Destroy(gameObject);
            return;
        }
        Instance = this;

        _body = GetComponent<Rigidbody>();
        _capsule = GetComponent<CapsuleCollider>();
        _renderers = GetComponentsInChildren<Renderer>(true);
        SetupPhysics();
        CanMove = _canMove;
        WarnExtraColliders();
    }

    // collider ตันอื่นบนตัว player (เช่น BoxCollider / CharacterController ค้างจาก prefab เก่า) จะแตะพื้นด้วย friction ของมันเอง
    // ทำให้ฝืดบนทางลาด/ติดกำแพง ทั้งที่ capsule ตั้ง friction 0 ไว้แล้ว
    private void WarnExtraColliders()
    {
        foreach (var col in GetComponents<Collider>())
        {
            if (col == _capsule || col.isTrigger) continue;
            Debug.LogWarning($"[{nameof(PlayerMovement)}] '{name}' มี {col.GetType().Name} เพิ่มจาก CapsuleCollider -> ควรลบออก (ทำให้ฝืด/ติดกำแพง)", this);
        }
    }

    // ตั้งค่า Rigidbody/Collider ให้เหมาะกับตัวละคร (ไม่ล้ม ไม่ติดผนัง)
    private void SetupPhysics()
    {
        _body.useGravity = true;
        _body.freezeRotation = true; // หมุนเองผ่าน MoveRotation ไม่ให้ physics ทำให้ล้ม
        _body.interpolation = RigidbodyInterpolation.Interpolate; // กล้องตามแล้วไม่สั่น
        // ตกเร็วไม่ทะลุพื้น (Speculative ใช้ได้ทั้งตอน kinematic ระหว่างรอ map ไม่ขึ้น warning)
        _body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        // friction 0 กันตัวค้างติดผนัง/กำแพงขอบ map ตอนกระโดดชน (การหยุดใช้ groundAcceleration แทน)
        if (_capsule.sharedMaterial == null)
        {
            _capsule.sharedMaterial = new PhysicsMaterial("Player (auto)")
            {
                dynamicFriction = 0f,
                staticFriction = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounciness = 0f,
                bounceCombine = PhysicsMaterialCombine.Minimum,
            };
        }
    }

    private void OnEnable()
    {
        _subscribedGenerator = Generator;
        if (_subscribedGenerator != null) _subscribedGenerator.MapGenerated += OnMapGenerated;
    }

    private void OnDisable()
    {
        if (_subscribedGenerator != null) _subscribedGenerator.MapGenerated -= OnMapGenerated;
        _subscribedGenerator = null;
    }

    private void Start()
    {
        if (buildAnimator == null) buildAnimator = FindFirstObjectByType<MapBuildAnimator>();
        if (buildAnimator != null)
        {
            _subscribedAnimator = buildAnimator;
            _subscribedAnimator.onBuildFinished.AddListener(OnMapBuilt);
        }

        // generator สร้างแผนที่เสร็จตั้งแต่ Awake แล้ว
        if (Generator != null && ProceduralMapGenerator.GeneratedRoot != null) HandleMapGenerated(true);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (_subscribedAnimator != null) _subscribedAnimator.onBuildFinished.RemoveListener(OnMapBuilt);
    }

    private void OnMapGenerated() => HandleMapGenerated(false);

    private void HandleMapGenerated(bool initial)
    {
        RefreshHoleBlockers();
        _hasSafePosition = false;

        if (spawnAtMapStart) SpawnAtMapStart();

        // รอบแรก animator เล่นถ้า playOnStart / หลัง regenerate เล่นใหม่เฉพาะถ้าเปิด replayOnRegenerate
        bool willAnimate = initial ? buildAnimator != null && (buildAnimator.playOnStart || buildAnimator.IsPlaying)
                                   : buildAnimator != null && buildAnimator.replayOnRegenerate;
        bool waitForBuild = willAnimate && buildAnimator.isActiveAndEnabled;
        if (waitForBuild)
        {
            if (lockUntilMapBuilt) CanMove = false;
            if (hideUntilMapBuilt) SetVisible(false);
        }
    }

    private void OnMapBuilt()
    {
        CanMove = true;
        SetVisible(true);
    }

    [ContextMenu("Spawn At Map Start")]
    public void SpawnAtMapStart()
    {
        var gen = Generator;
        if (gen == null) return;
        Teleport(gen.StartPosition);
    }

    // วางเท้า (ก้น capsule) ไว้ที่ groundPoint
    public void Teleport(Vector3 groundPoint)
    {
        if (_capsule == null) _capsule = GetComponent<CapsuleCollider>();
        SetBodyPosition(groundPoint + Vector3.up * (FootToPivot() + spawnHeightOffset));
    }

    // กลับไปยืนจุดล่าสุดที่ยืนบนพื้นปกติ (ไม่ใช่ก้นหลุม) ถ้ายังไม่มีเลยกลับจุด Start
    public void RespawnAtSafePosition()
    {
        if (_hasSafePosition) SetBodyPosition(_lastSafePosition);
        else SpawnAtMapStart();
    }

    private void SetBodyPosition(Vector3 pos)
    {
        if (_body == null) _body = GetComponent<Rigidbody>();
        EndDash(false);

        // ตั้งทั้ง transform และ body กัน interpolation ลากตัวจากตำแหน่งเก่ามา
        transform.position = pos;
        _body.position = pos;
        if (!_body.isKinematic)
        {
            _body.linearVelocity = Vector3.zero;
            _body.angularVelocity = Vector3.zero;
        }
    }

    private void Update()
    {
        if (!_canMove) return;

        // อ่าน input ใน Update (ไม่พลาดปุ่มที่กดสั้นๆ) แล้วไปใช้ใน FixedUpdate
        _moveInput = ReadMoveInput();
        if (JumpPressedThisFrame()) _lastJumpPressedTime = Time.time;
        _jumpHeld = JumpHeld();
        if (dashSprintSameKey)
        {
            ReadDashSprintKey();
        }
        else
        {
            _sprint = IsSprinting();
            if (DashPressedThisFrame()) _dashRequested = true;
        }

        if (transform.position.y < respawnBelowY) RespawnAtSafePosition();
    }

    private void FixedUpdate()
    {
        if (!_canMove || _body.isKinematic) return;

        UpdateGrounded();
        Vector3 move = CameraRelative(_moveInput);

        if (_dashRequested)
        {
            _dashRequested = false;
            TryStartDash(move);
        }

        if (IsDashing)
        {
            _dashTimeLeft -= Time.fixedDeltaTime;
            _body.linearVelocity = _dashDir * (dashDistance / dashDuration);
            if (_dashTimeLeft <= 0f) EndDash(true);
            UpdateHoleBlockerCollision();
            return;
        }

        Vector3 velocity = _body.linearVelocity;
        Vector3 desired = move * (moveSpeed * (_sprint ? sprintMultiplier : 1f));
        bool onGround = IsGrounded && !_jumpedSinceGrounded;

        if (onGround)
        {
            // บนพื้น: วิ่งไปตามระนาบพื้น (ขึ้น/ลงทางลาดด้วยความเร็วเท่าพื้นเรียบ ไม่ดันเข้าเนิน ไม่เด้งตอนลง)
            if (desired.sqrMagnitude > 0.0001f)
                desired = Vector3.ProjectOnPlane(desired, _groundNormal).normalized * desired.magnitude;
            // หักความเร็วเข้าระนาบพื้นปัจจุบันทันที (เช่นจากทางลาดขึ้นมาพื้นเรียบ ความเร็วแกนตั้งหายเลย ไม่พุ่งลอยต่อ)
            velocity = Vector3.ProjectOnPlane(velocity, _groundNormal);
            velocity = Vector3.MoveTowards(velocity, desired, groundAcceleration * Time.fixedDeltaTime);
        }
        else
        {
            // กลางอากาศ: คุมแค่แกนนอน แกนตั้งปล่อยให้ gravity จัดการ
            Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);
            horizontal = Vector3.MoveTowards(horizontal, desired, airAcceleration * Time.fixedDeltaTime);
            velocity.x = horizontal.x;
            velocity.z = horizontal.z;
        }

        // ตัดส่วนที่ดันเข้ากำแพง/หน้าผาทิ้ง -> ไถลไปตามกำแพงแทนการกดค้าง (กันติดกำแพงตอนกระโดด)
        velocity = SlideAlongWalls(velocity);

        // เดินตกขอบเองเกิน coyote time = นับว่าใช้ครั้งบนพื้นไปแล้ว (เหลือแค่ครั้งกลางอากาศ)
        bool inCoyote = Time.time - _lastGroundedTime <= coyoteTime;
        if (!inCoyote && _jumpsUsed == 0) _jumpsUsed = 1;

        bool groundJump = inCoyote && _jumpsUsed == 0;
        bool airJump = !groundJump && _jumpsUsed < maxJumps;
        bool pressed = Time.time - _lastJumpPressedTime <= jumpBufferTime;
        // กดค้าง: ถึงพื้นเมื่อไหร่กระโดดต่อเอง (เฉพาะจากพื้น กดค้างไม่ใช้ครั้งกลางอากาศ)
        bool held = holdToAutoJump && _jumpHeld && groundJump;

        if ((groundJump || airJump) && (pressed || held))
        {
            // v = sqrt(2gh) ได้ความสูง jumpHeight พอดีตาม gravity ปัจจุบัน (ครั้งกลางอากาศตั้งทับความเร็วตก)
            velocity.y = Mathf.Sqrt(2f * Mathf.Abs(Physics.gravity.y) * (groundJump ? jumpHeight : airJumpHeight));
            _jumpsUsed++;
            _jumpedSinceGrounded = true;
            _lastJumpPressedTime = float.NegativeInfinity;
            IsGrounded = false;
        }

        _body.linearVelocity = velocity;

        if (onGround && !_jumpedSinceGrounded)
        {
            // ยืนบนพื้น: หักล้าง gravity ไม่ให้ไถลลงเนิน/ถ่วงตอนขึ้นเนิน (ความเร็วแกนตั้งมาจากระนาบพื้นแล้ว)
            _body.AddForce(-Physics.gravity, ForceMode.Acceleration);
        }
        else if (velocity.y < 0f && fallGravityMultiplier > 1f)
        {
            // ตกลงเร็วกว่าตอนขึ้น (gravity ปกติมาจาก Rigidbody อยู่แล้ว เติมเฉพาะส่วนเกิน)
            _body.AddForce(Physics.gravity * (fallGravityMultiplier - 1f), ForceMode.Acceleration);
        }

        if (move.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(move);
            _body.MoveRotation(Quaternion.Slerp(_body.rotation, targetRot, rotationSpeed * Time.fixedDeltaTime));
        }

        UpdateHoleBlockerCollision();
    }

    // ---------- Walls ----------

    private float MinGroundNormalY => Mathf.Cos(maxSlopeAngle * Mathf.Deg2Rad);

    // CapsuleCast ไปทางที่กำลังเคลื่อนที่ในแนวนอน ถ้าเจอผิวชัน (กำแพง/หน้าผา) ให้ตัดความเร็วส่วนที่พุ่งเข้าหามันทิ้ง
    private Vector3 SlideAlongWalls(Vector3 velocity)
    {
        Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);
        float speed = horizontal.magnitude;
        if (speed < 0.001f) return velocity;

        Vector3 dir = horizontal / speed;
        float scale = Mathf.Max(transform.lossyScale.x, transform.lossyScale.z);
        float radius = _capsule.radius * scale * 0.95f;
        float halfLine = Mathf.Max(0f, _capsule.height * 0.5f * transform.lossyScale.y - _capsule.radius * scale);
        Vector3 center = transform.TransformPoint(_capsule.center);
        // ยกปลายล่างขึ้นเล็กน้อย กันไปโดนพื้น/ขอบทางลาดใต้เท้าแล้วนับเป็นกำแพง
        Vector3 bottom = center - Vector3.up * halfLine + Vector3.up * 0.1f;
        Vector3 top = center + Vector3.up * halfLine;

        int count = Physics.CapsuleCastNonAlloc(bottom, top, radius, dir, _wallHits,
            speed * Time.fixedDeltaTime + 0.05f, groundLayers, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = _wallHits[i];
            if (hit.collider.attachedRigidbody == _body) continue;
            if (_ignoringHoleBlockers && System.Array.IndexOf(_holeBlockers, hit.collider) >= 0) continue;
            if (hit.normal.y >= MinGroundNormalY) continue; // พื้น/ทางลาด เดินขึ้นได้

            Vector3 wallNormal = new Vector3(hit.normal.x, 0f, hit.normal.z);
            if (wallNormal.sqrMagnitude < 0.0001f) continue;
            wallNormal.Normalize();

            float into = Vector3.Dot(velocity, wallNormal);
            if (into < 0f) velocity -= wallNormal * into;
        }
        return velocity;
    }

    // ---------- Dash ----------

    private void TryStartDash(Vector3 move)
    {
        if (dashDistance <= 0f || Time.time - _lastDashTime < dashCooldown) return;
        if (!IsGrounded && (!allowAirDash || _airDashUsed)) return;

        // พุ่งไปทางที่กดเดินอยู่ ถ้าไม่ได้กดพุ่งไปทางที่หันหน้า
        Vector3 dir = move.sqrMagnitude > 0.01f ? move : Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (dir.sqrMagnitude < 0.0001f) return;

        _dashDir = dir.normalized;
        _dashTimeLeft = dashDuration;
        _lastDashTime = Time.time;
        if (!IsGrounded) _airDashUsed = true;

        _body.useGravity = false; // พุ่งเป็นเส้นตรงไม่ตก ข้ามน้ำได้เต็มระยะ
        _body.MoveRotation(Quaternion.LookRotation(_dashDir));
        SetIgnoreHoleBlockers(true);
    }

    private void EndDash(bool keepMomentum)
    {
        if (_dashTimeLeft <= 0f && (_body == null || _body.useGravity)) return;

        _dashTimeLeft = 0f;
        if (_body == null) return;
        _body.useGravity = true;

        // เหลือความเร็วเท่าเดินปกติ ไม่ไถลไกลหลังพุ่ง
        if (keepMomentum && !_body.isKinematic) _body.linearVelocity = _dashDir * moveSpeed;
    }

    // ---------- Water / Hole Blockers ----------

    private void RefreshHoleBlockers()
    {
        var gen = Generator;
        _holeBlockers = gen != null && gen.HoleBlockerObject != null
            ? gen.HoleBlockerObject.GetComponents<Collider>()
            : new Collider[0];
        _ignoringHoleBlockers = false; // collider ชุดใหม่ยังไม่ถูก ignore
    }

    // เดินบนพื้น = ชนกำแพงรอบบ่อ / กระโดด ลอย หรือ dash = ทะลุผ่านได้
    private void UpdateHoleBlockerCollision()
    {
        bool airborne = _jumpedSinceGrounded || Time.time - _lastGroundedTime > AirborneGrace;
        bool ignore = IsDashing || airborne;

        // เพิ่งลงพื้นทับกำแพงอยู่ ยังไม่เปิดชน กัน physics ดีดตัวกระเด็น/ดันตกบ่อ
        if (!ignore && _ignoringHoleBlockers && OverlapsHoleBlocker()) ignore = true;

        SetIgnoreHoleBlockers(ignore);
    }

    private void SetIgnoreHoleBlockers(bool ignore)
    {
        if (ignore == _ignoringHoleBlockers) return;

        foreach (var blocker in _holeBlockers)
            if (blocker != null) Physics.IgnoreCollision(_capsule, blocker, ignore);

        _ignoringHoleBlockers = ignore;
    }

    private bool OverlapsHoleBlocker()
    {
        Bounds bounds = _capsule.bounds;
        foreach (var blocker in _holeBlockers)
            if (blocker != null && blocker.bounds.Intersects(bounds)) return true;
        return false;
    }

    // ตกลงผิวน้ำ (trigger ของ Pond Water) = กระโดด/dash ไม่ถึงฝั่ง
    private void OnTriggerEnter(Collider other) => CheckWater(other);
    private void OnTriggerStay(Collider other) => CheckWater(other);

    private void CheckWater(Collider other)
    {
        if (!_canMove) return;

        var gen = Generator;
        if (gen != null && gen.WaterObject != null && other.gameObject == gen.WaterObject)
            RespawnAtSafePosition();
    }

    // ---------- Ground Check ----------

    // SphereCast จากกลางตัวลงล่าง หาพื้นที่ใกล้สุดที่ขอบล่าง sphere เลยเท้าลงไปไม่เกิน extraDistance
    // ข้าม trigger (เช่นน้ำในบ่อ) ตัวเอง และผิวชันเกิน maxSlopeAngle
    // footGap = ระยะจากเท้าลงไปถึงพื้น (ติดลบ/0 = แตะอยู่)
    private bool FindGround(float extraDistance, out Collider ground, out Vector3 normal, out float footGap)
    {
        float scale = Mathf.Max(transform.lossyScale.x, transform.lossyScale.z);
        float radius = _capsule.radius * scale * 0.95f;
        Vector3 origin = transform.TransformPoint(_capsule.center);
        float footY = transform.position.y - FootToPivot();
        float toFoot = origin.y - footY - radius; // ระยะที่ sphere ต้องเลื่อนลงจนขอบล่างถึงเท้า

        ground = null;
        normal = Vector3.up;
        footGap = 0f;
        float nearest = float.PositiveInfinity;

        // เลือกพื้นที่ใกล้สุด (SphereCastAll ไม่เรียงตามระยะ) เพราะใช้ normal ของมันเป็นระนาบเดิน
        var hits = Physics.SphereCastAll(origin, radius, Vector3.down, Mathf.Max(extraDistance, toFoot + extraDistance),
            groundLayers, QueryTriggerInteraction.Ignore);
        foreach (var hit in hits)
        {
            if (hit.collider.attachedRigidbody == _body) continue;
            if (_ignoringHoleBlockers && System.Array.IndexOf(_holeBlockers, hit.collider) >= 0) continue; // ทะลุอยู่ ไม่นับเป็นพื้น
            // ผิวชันเกิน (กำแพง/หน้าผา) ไม่นับเป็นพื้น ไม่งั้นกระโดดชนกำแพงแล้วค้าง/กระโดดซ้ำได้
            // (hit ที่ทับตั้งแต่เริ่ม cast ได้ distance 0 และ normal ชี้ย้อนทิศ cast = ขึ้น นับเป็นพื้นตามเดิม)
            if (hit.distance > 0f && hit.normal.y < MinGroundNormalY) continue;
            if (hit.distance >= nearest) continue;
            nearest = hit.distance;
            ground = hit.collider;
            normal = hit.distance > 0f ? hit.normal : Vector3.up;
            footGap = hit.distance - toFoot;
        }
        return ground != null;
    }

    // ดึงตัวลงแปะพื้นที่อยู่ใต้เท้าไม่เกิน groundSnapDistance และหักความเร็วส่วนที่พุ่งออกจากพื้นทิ้ง (คงความเร็วรวมไว้)
    private bool SnapToGround(out Collider ground)
    {
        if (!FindGround(groundSnapDistance, out ground, out Vector3 normal, out float footGap)) return false;

        if (footGap > 0f) _body.position += Vector3.down * footGap;

        Vector3 velocity = _body.linearVelocity;
        float speed = velocity.magnitude;
        float away = Vector3.Dot(velocity, normal);
        if (away > 0f) _body.linearVelocity = (velocity - normal * away).normalized * speed;

        _groundNormal = normal;
        return true;
    }

    private void UpdateGrounded()
    {
        bool wasOnGround = IsGrounded && !_jumpedSinceGrounded;
        bool grounded = FindGround(groundCheckDistance, out Collider groundCollider, out _groundNormal, out _);

        // ยังพุ่งขึ้นจากการกระโดดอยู่ ไม่นับว่าแตะพื้น (กัน coyote time กระโดดซ้ำกลางอากาศ)
        if (grounded && _body.linearVelocity.y > 0.1f && _jumpedSinceGrounded) grounded = false;

        // หลุดพื้นเองโดยไม่ได้กระโดด (วิ่งพ้นหัวทางลาด/ขอบเนิน) -> ดูดกลับลงพื้นถ้าพื้นอยู่ไม่ไกล กันตัวเด้งลอย
        if (!grounded && wasOnGround && !IsDashing && SnapToGround(out groundCollider)) grounded = true;

        IsGrounded = grounded;
        if (!grounded) _groundNormal = Vector3.up;
        if (!grounded) return;

        _lastGroundedTime = Time.time;
        _jumpedSinceGrounded = false;
        _jumpsUsed = 0;
        _airDashUsed = false;

        // จำจุดปลอดภัยไว้ respawn ตอนตกน้ำ (ไม่นับก้นหลุม และตอนยังทับกำแพงบ่อ)
        var gen = Generator;
        bool onPitFloor = gen != null && gen.PitObject != null && groundCollider.gameObject == gen.PitObject;
        if (!onPitFloor && !IsDashing && !OverlapsHoleBlocker() && IsSafeSpot(gen))
        {
            _lastSafePosition = _body.position;
            _hasSafePosition = true;
        }
    }

    // จุดปลอดภัยต้องมีพื้นจริงรอบตัว ห่างจากขอบ capsule อีก safeEdgeMargin (ไม่ใช่แค่ขอบ capsule แตะขอบบ่อ)
    // ไม่งั้น respawn ไปยืนเกยขอบบ่อแล้วไถล/เดินตกน้ำซ้ำไม่จบ (จำเป็นตอนไม่มีกำแพงรอบบ่อ = Walkable Water)
    private static readonly Vector3[] SafeCheckDirections =
    {
        Vector3.zero,
        new Vector3(1f, 0f, 0f), new Vector3(-1f, 0f, 0f), new Vector3(0f, 0f, 1f), new Vector3(0f, 0f, -1f),
        new Vector3(0.7071f, 0f, 0.7071f), new Vector3(-0.7071f, 0f, 0.7071f),
        new Vector3(0.7071f, 0f, -0.7071f), new Vector3(-0.7071f, 0f, -0.7071f),
    };

    private bool IsSafeSpot(ProceduralMapGenerator gen)
    {
        float scale = Mathf.Max(transform.lossyScale.x, transform.lossyScale.z);
        float ringRadius = _capsule.radius * scale + safeEdgeMargin;
        Vector3 center = transform.TransformPoint(_capsule.center);
        float footY = transform.position.y - FootToPivot();
        float distance = center.y - footY + groundCheckDistance * 2f;
        GameObject pit = gen != null ? gen.PitObject : null;

        foreach (var dir in SafeCheckDirections)
        {
            bool hasGround = false;
            var hits = Physics.RaycastAll(center + dir * ringRadius, Vector3.down, distance, groundLayers, QueryTriggerInteraction.Ignore);
            foreach (var hit in hits)
            {
                if (hit.collider.attachedRigidbody == _body) continue;
                if (pit != null && hit.collider.gameObject == pit) continue;
                if (System.Array.IndexOf(_holeBlockers, hit.collider) >= 0) continue;
                if (Mathf.Abs(hit.point.y - footY) > groundCheckDistance * 2f) continue; // ต้องเป็นพื้นระดับเท้า ไม่ใช่หัวต้นไม้/ก้อนหิน
                hasGround = true;
                break;
            }
            if (!hasGround) return false;
        }
        return true;
    }

    // ระยะจาก pivot ลงไปถึงก้น capsule
    private float FootToPivot()
    {
        float scaleY = transform.lossyScale.y;
        return (_capsule.height * 0.5f - _capsule.center.y) * scaleY;
    }

    // ---------- Input ----------

    private Vector3 CameraRelative(Vector2 input)
    {
        Transform cam = cameraTransform != null ? cameraTransform : (Camera.main != null ? Camera.main.transform : null);
        if (cam == null) return Vector3.ClampMagnitude(new Vector3(input.x, 0f, input.y), 1f);

        Vector3 forward = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
        Vector3 right = Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized;
        return Vector3.ClampMagnitude(forward * input.y + right * input.x, 1f);
    }

    private static Vector2 ReadMoveInput()
    {
        Vector2 value = Vector2.zero;

        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) value.y += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) value.y -= 1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) value.x += 1f;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) value.x -= 1f;
        }

        var gamepad = Gamepad.current;
        if (gamepad != null) value += gamepad.leftStick.ReadValue();

        return Vector2.ClampMagnitude(value, 1f);
    }

    private bool IsSprinting()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard[sprintKey].isPressed) return true;

        var gamepad = Gamepad.current;
        return gamepad != null && gamepad.leftStickButton.isPressed;
    }

    private bool JumpHeld()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard[jumpKey].isPressed) return true;

        var gamepad = Gamepad.current;
        return gamepad != null && gamepad.buttonSouth.isPressed;
    }

    private bool JumpPressedThisFrame()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard[jumpKey].wasPressedThisFrame) return true;

        var gamepad = Gamepad.current;
        return gamepad != null && gamepad.buttonSouth.wasPressedThisFrame;
    }

    // ปุ่มเดียว: แตะ = dash ตอนปล่อย / ค้างครบ sprintHoldTime = วิ่งจนปล่อย
    private void ReadDashSprintKey()
    {
        if (DashPressedThisFrame()) _dashKeyDownTime = Time.time;

        if (_dashKeyDownTime < 0f)
        {
            _sprint = false;
            return;
        }

        bool heldLongEnough = Time.time - _dashKeyDownTime >= sprintHoldTime;
        if (DashHeld())
        {
            _sprint = heldLongEnough;
            return;
        }

        // เพิ่งปล่อย (รวมกรณีกด-ปล่อยในเฟรมเดียว)
        if (!heldLongEnough) _dashRequested = true;
        _dashKeyDownTime = -1f;
        _sprint = false;
    }

    private bool DashHeld()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard[dashKey].isPressed) return true;

        var gamepad = Gamepad.current;
        return gamepad != null && gamepad.buttonWest.isPressed;
    }

    private bool DashPressedThisFrame()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard[dashKey].wasPressedThisFrame) return true;

        var gamepad = Gamepad.current;
        return gamepad != null && gamepad.buttonWest.wasPressedThisFrame;
    }

    private void SetVisible(bool visible)
    {
        foreach (var r in _renderers)
            if (r != null) r.enabled = visible;
    }
}

using UnityEngine;

// การเคลื่อนที่ของศัตรูด้วย Rigidbody (simulation ฝั่ง server) รับคำสั่งจาก EnemyAI: เดิน/วิ่งไปทิศไหน, กระโดดไปลงจุดไหน
// ไม่อ่าน input / ไม่พึ่งกล้อง / physics query ผ่าน PhysicsScene ของ scene ตัวเอง
//
// กระโดดข้ามพื้นต่างระดับ: JumpTo(จุดลงพื้น) คำนวณวิถีโค้ง (projectile) ให้ยอดสูงกว่าจุดที่สูงกว่า jumpClearance
// แล้วตกลงตรงจุดหมายพอดี ระหว่างลอยไม่บังคับทิศ (ให้ลงตรงที่คำนวณไว้)
[RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
public class EnemyMotor : MonoBehaviour, ICharacterLocomotion
{
    [Header("Movement")]
    [Min(0f)] public float walkSpeed = 2.2f;
    [Min(0f)] public float runSpeed = 5f;
    [Tooltip("อัตราเร่ง/เบรก (หน่วย/วินาที²)")]
    [Min(0f)] public float acceleration = 30f;
    [Min(0f)] public float rotationSpeed = 10f;

    [Header("Jump")]
    [Tooltip("กระโดดขึ้นหน้าผาได้สูงสุดเท่านี้ (AI ใช้กรองเส้นทาง)")]
    [Min(0f)] public float maxJumpUp = 1.6f;
    [Tooltip("กระโดดลงได้ลึกสุดเท่านี้")]
    [Min(0f)] public float maxJumpDown = 3.2f;
    [Tooltip("ยอดวิถีกระโดดสูงกว่าจุดที่สูงกว่า (จุดเริ่ม/จุดลง) เท่านี้ กันเท้าเกี่ยวขอบหน้าผา")]
    [Min(0.1f)] public float jumpClearance = 0.7f;

    [Header("Death")]
    [Tooltip("ตายแล้วกระเด็นไปทางที่โดนตีด้วยความเร็วแนวนอนเท่านี้ (physics จริง ตกขอบได้)")]
    [Min(0f)] public float deathKnockbackSpeed = 4f;
    [Tooltip("ความเร็วพุ่งขึ้นตอนกระเด็น")]
    [Min(0f)] public float deathKnockbackUp = 3f;
    [Tooltip("ศพไถลบนพื้นแล้วเบรกด้วยอัตรานี้ (หน่วย/วินาที²) หยุดแล้วถึงแช่ตัว")]
    [Min(0.1f)] public float corpseFriction = 10f;
    [Tooltip("กันค้าง: ตายนานเกินนี้แช่ตัวทันทีแม้ยังไม่หยุด")]
    [Min(0.5f)] public float corpseSettleTimeout = 4f;

    [Header("Ground Check")]
    public LayerMask groundLayers = ~0;
    [Min(0.01f)] public float groundCheckDistance = 0.15f;
    [Range(10f, 80f)] public float maxSlopeAngle = 50f;
    [Tooltip("ตกต่ำกว่านี้ = กลับไปจุดที่ยืนบนพื้นล่าสุด")]
    public float respawnBelowY = -20f;

    public Vector3 Velocity => _body != null ? _body.linearVelocity : Vector3.zero;
    public bool IsGrounded { get; private set; }
    public bool IsDashing => false;
    public bool Sprinting => _run;
    public float MoveSpeed => walkSpeed;
    public int JumpCount { get; private set; }
    public bool LastJumpFromGround => true;
    // ลอยจากการ JumpTo ยังไม่ลงพื้น
    public bool IsJumping { get; private set; }
    // ตำแหน่งเท้า (ก้น capsule)
    public Vector3 FootPosition => transform.position - Vector3.up * FootToPivot();

    private Rigidbody _body;
    private CapsuleCollider _capsule;
    private Vector3 _moveDir;
    private bool _run;
    private Vector3 _faceDir;
    private Vector3 _groundNormal = Vector3.up;
    private float _jumpStartTime;
    private Vector3 _lastGroundedPos;
    private bool _frozen;
    private bool _dead;
    private float _deathTime;
    private readonly RaycastHit[] _hits = new RaycastHit[8];

    private void Awake()
    {
        _body = GetComponent<Rigidbody>();
        _capsule = GetComponent<CapsuleCollider>();
        _body.useGravity = true;
        _body.freezeRotation = true;
        _body.interpolation = RigidbodyInterpolation.Interpolate;
        _body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        _lastGroundedPos = _body.position;

        // friction 0 เหมือน player: ไม่ติดกำแพง/ไม่ฝืดบนทางลาด (การหยุดใช้ acceleration แทน)
        if (_capsule.sharedMaterial == null)
        {
            _capsule.sharedMaterial = new PhysicsMaterial("Enemy (auto)")
            {
                dynamicFriction = 0f,
                staticFriction = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounciness = 0f,
                bounceCombine = PhysicsMaterialCombine.Minimum,
            };
        }
    }

    private void OnDestroy()
    {
        // physics material ที่สร้างเองต้องลบเอง (กฎข้อ 10)
        if (_capsule != null && _capsule.sharedMaterial != null && _capsule.sharedMaterial.name == "Enemy (auto)")
            Destroy(_capsule.sharedMaterial);
    }

    // ---------- Commands (จาก AI) ----------

    // ทิศแนวนอน (ความยาว 0-1 = สัดส่วนความเร็ว) / run = ใช้ runSpeed
    public void Move(Vector3 direction, bool run)
    {
        direction.y = 0f;
        _moveDir = Vector3.ClampMagnitude(direction, 1f);
        _run = run && _moveDir.sqrMagnitude > 0.0001f;
    }

    public void Stop() => Move(Vector3.zero, false);

    // หันไปทางนี้ (หมุนนุ่มๆ) ใช้ตอนยืนตี/มองเป้า
    public void Face(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.0001f) _faceDir = direction.normalized;
    }

    // ตาย: กระเด็นไปทาง knockDirection (ทิศที่โดนตี) ด้วย physics จริง -> ตายบนที่สูงแล้วตกลงพื้นได้
    // หันหลังให้ทิศกระเด็น ให้ท่า Flying Back Death (ล้มไปข้างหลัง) ไปทางเดียวกับตัว
    // ลงพื้นแล้วไถลจนหยุด ค่อยแช่ตัว (Freeze) -> ไม่ค้างกลางอากาศ
    public void Die(Vector3 knockDirection)
    {
        if (_dead || _frozen) return;
        _dead = true;
        _deathTime = Time.time;
        Stop();

        knockDirection.y = 0f;
        if (knockDirection.sqrMagnitude < 0.0001f) knockDirection = -transform.forward;
        knockDirection.Normalize();
        _body.rotation = Quaternion.LookRotation(-knockDirection);

        if (_body.isKinematic) return;
        _body.linearVelocity = knockDirection * deathKnockbackSpeed + Vector3.up * deathKnockbackUp;
        IsJumping = true; // ใช้กติกาเดียวกับกระโดด: ช่วงพุ่งขึ้นไม่นับว่าแตะพื้น
        IsGrounded = false;
        _jumpStartTime = Time.time;
    }

    // แช่ตัว: ปิด physics + การชน (ศพไม่ขวางทาง) ค้างตำแหน่งไว้
    public void Freeze()
    {
        _frozen = true;
        Stop();
        IsJumping = false;
        if (!_body.isKinematic) _body.linearVelocity = Vector3.zero;
        _body.isKinematic = true;
        _capsule.enabled = false;
    }

    private void CorpseTick()
    {
        UpdateGrounded();
        bool timedOut = Time.time - _deathTime > corpseSettleTimeout;

        if (IsGrounded)
        {
            // ไถลตามพื้นแล้วเบรก / หักล้าง gravity ไม่ให้ไถลลงเนินต่อ
            Vector3 v = Vector3.ProjectOnPlane(_body.linearVelocity, _groundNormal);
            v = Vector3.MoveTowards(v, Vector3.zero, corpseFriction * Time.fixedDeltaTime);
            _body.linearVelocity = v;
            _body.AddForce(-Physics.gravity, ForceMode.Acceleration);
            if (v.sqrMagnitude < 0.04f || timedOut) Freeze();
        }
        else if (timedOut && Mathf.Abs(_body.linearVelocity.y) < 0.05f)
        {
            Freeze(); // ค้างอยู่บนอะไรสักอย่างที่ไม่นับเป็นพื้น (ขอบหน้าผา/หัวศัตรูตัวอื่น) ไม่ได้ตกต่อแล้ว
        }

        if (transform.position.y < respawnBelowY) Freeze();
    }

    public void Teleport(Vector3 footPosition)
    {
        Vector3 pos = footPosition + Vector3.up * (FootToPivot() + 0.05f);
        transform.position = pos;
        _body.position = pos;
        if (!_body.isKinematic) _body.linearVelocity = Vector3.zero;
        _lastGroundedPos = pos;
    }

    // กระโดดจากตรงนี้ไปลงที่ landing (ตำแหน่งเท้า) คืน false ถ้ายังไม่ได้ยืนบนพื้น / สูงเกินความสามารถ
    public bool JumpTo(Vector3 landing)
    {
        if (_frozen || _dead || !IsGrounded || IsJumping) return false;

        Vector3 foot = FootPosition;
        float dy = landing.y - foot.y;
        if (dy > maxJumpUp + 0.05f || -dy > maxJumpDown + 0.05f) return false;

        // ยอดวิถี apex เหนือเท้า: สูงกว่าจุดที่สูงกว่า jumpClearance
        float g = Mathf.Abs(Physics.gravity.y);
        float apex = Mathf.Max(dy, 0f) + jumpClearance;
        float vy = Mathf.Sqrt(2f * g * apex);
        float timeUp = vy / g;
        float timeDown = Mathf.Sqrt(2f * (apex - dy) / g);
        float flight = timeUp + timeDown;

        Vector3 flat = landing - foot;
        flat.y = 0f;
        Vector3 velocity = flat / flight;
        velocity.y = vy;

        _body.linearVelocity = velocity;
        if (flat.sqrMagnitude > 0.0001f) _faceDir = flat.normalized;
        IsJumping = true;
        IsGrounded = false;
        _jumpStartTime = Time.time;
        JumpCount++;
        return true;
    }

    // ---------- Simulation ----------

    private void FixedUpdate()
    {
        if (_frozen || _body.isKinematic) return;
        if (_dead)
        {
            CorpseTick();
            return;
        }

        UpdateGrounded();
        Vector3 velocity = _body.linearVelocity;
        float speed = _run ? runSpeed : walkSpeed;
        Vector3 desired = _moveDir * speed;

        if (IsGrounded)
        {
            // เดินตามระนาบพื้น (ทางลาดขึ้น/ลงความเร็วเท่าพื้นเรียบ) + หักล้าง gravity ไม่ให้ไถลลงเนิน
            if (desired.sqrMagnitude > 0.0001f)
                desired = Vector3.ProjectOnPlane(desired, _groundNormal).normalized * desired.magnitude;
            velocity = Vector3.ProjectOnPlane(velocity, _groundNormal);
            velocity = Vector3.MoveTowards(velocity, desired, acceleration * Time.fixedDeltaTime);
            _body.linearVelocity = velocity;
            _body.AddForce(-Physics.gravity, ForceMode.Acceleration);
        }
        else if (!IsJumping)
        {
            // ตกจากขอบเอง: คุมทิศแนวนอนได้นิดหน่อย
            Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);
            horizontal = Vector3.MoveTowards(horizontal, desired, acceleration * 0.3f * Time.fixedDeltaTime);
            _body.linearVelocity = new Vector3(horizontal.x, velocity.y, horizontal.z);
        }

        // หัน: ตามทิศที่สั่ง Face ถ้ามี ไม่งั้นตามทิศเดิน
        Vector3 look = _faceDir.sqrMagnitude > 0.0001f ? _faceDir : _moveDir;
        if (look.sqrMagnitude > 0.0001f)
        {
            Quaternion target = Quaternion.LookRotation(new Vector3(look.x, 0f, look.z));
            _body.MoveRotation(Quaternion.Slerp(_body.rotation, target, rotationSpeed * Time.fixedDeltaTime));
        }
        _faceDir = Vector3.zero; // Face ต้องสั่งซ้ำทุก tick ที่ต้องการ

        if (transform.position.y < respawnBelowY) Teleport(_lastGroundedPos - Vector3.up * FootToPivot());
    }

    private void UpdateGrounded()
    {
        // เพิ่งกระโดด ยังพุ่งขึ้นอยู่ ไม่นับว่าแตะพื้น
        if (IsJumping && (Time.time - _jumpStartTime < 0.1f || _body.linearVelocity.y > 0.1f))
        {
            IsGrounded = false;
            return;
        }

        float scale = Mathf.Max(transform.lossyScale.x, transform.lossyScale.z);
        float radius = _capsule.radius * scale * 0.95f;
        Vector3 origin = transform.TransformPoint(_capsule.center);
        float toFoot = origin.y - FootPosition.y - radius;
        float minNormalY = Mathf.Cos(maxSlopeAngle * Mathf.Deg2Rad);

        int count = gameObject.scene.GetPhysicsScene().SphereCast(origin, radius, Vector3.down, _hits,
            toFoot + groundCheckDistance, groundLayers, QueryTriggerInteraction.Ignore);

        bool grounded = false;
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = _hits[i];
            if (hit.collider.attachedRigidbody == _body) continue;
            if (hit.distance > 0f && hit.normal.y < minNormalY) continue; // กำแพง/หน้าผา
            if (hit.distance >= nearest) continue;
            nearest = hit.distance;
            grounded = true;
            _groundNormal = hit.distance > 0f ? hit.normal : Vector3.up;
        }

        IsGrounded = grounded;
        if (!grounded)
        {
            _groundNormal = Vector3.up;
            return;
        }
        IsJumping = false;
        _lastGroundedPos = _body.position;
    }

    private float FootToPivot()
    {
        if (_capsule == null) _capsule = GetComponent<CapsuleCollider>();
        return (_capsule.height * 0.5f - _capsule.center.y) * transform.lossyScale.y;
    }
}

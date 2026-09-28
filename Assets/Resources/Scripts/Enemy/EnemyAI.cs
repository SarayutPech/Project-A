using System.Collections.Generic;
using UnityEngine;

public enum EnemyState
{
    Idle,        // ยืนนิ่ง (player อยู่นอกระยะ)
    Wander,      // สุ่มเดินรอบจุดเกิด
    Suspicious,  // player เข้าระยะแรก: เดินเข้าไปดู
    Chase,       // player เข้าระยะสอง: lock เป้า วิ่งไล่ต่อย
    Dead,
}

// สมองของศัตรู (simulation ฝั่ง server เท่านั้น) คิดเป็น tick ใน FixedUpdate ไม่ผูก frame rate (กฎข้อ 9)
// หาเส้นทางบน MapNavGraph ของแผนที่ที่ตัวเองอยู่ (เดิน + กระโดดขึ้น/ลงหน้าผา) สั่ง EnemyMotor เดิน/กระโดด สั่ง MeleeAttack ต่อย
// เป้าหมายหาจาก Health.All (ทุก player ในเกม ไม่ใช่ PlayerMovement.Instance -> รองรับหลายผู้เล่น กฎข้อ 3)
//
//   นอกระยะ detectRange      -> Idle <-> Wander สลับกัน
//   เข้า detectRange         -> Suspicious (เดินเข้าหา) ถ้าออกนอกระยะนานเกิน forgetTime กลับไปเดินเล่น
//   เข้า aggroRange          -> Chase (lock เป้า วิ่งไล่) ปล่อยเป้าเมื่อเป้าตาย / ห่างเกิน loseTargetRange / ไปไม่ถึงนานเกิน
//   HP หมด                   -> Dead (ท่าตาย ศพหายหลัง corpseLifetime)
[RequireComponent(typeof(EnemyMotor), typeof(Health), typeof(MeleeAttack))]
public class EnemyAI : MonoBehaviour
{
    [Header("Ranges")]
    [Tooltip("ระยะแรก: เห็น player แล้วเดินเข้าไปดู (สงสัย)")]
    [Min(0f)] public float detectRange = 12f;
    [Tooltip("ระยะสอง: lock เป้า วิ่งไล่ต่อย")]
    [Min(0f)] public float aggroRange = 6f;
    [Tooltip("lock แล้ว เป้าหนีห่างเกินนี้ถึงปล่อย")]
    [Min(0f)] public float loseTargetRange = 20f;
    [Tooltip("ระยะเริ่มต่อย (แนวนอน วัดจากตัวถึงตัว)")]
    [Min(0.1f)] public float attackRange = 1.3f;
    [Tooltip("ต่างระดับเกินนี้ไม่ต่อย (เป้าอยู่บนหน้าผา ต้องหาทางขึ้นไปก่อน)")]
    [Min(0.1f)] public float attackMaxHeightDiff = 1f;

    [Header("Idle / Wander")]
    [Tooltip("เดินเล่นห่างจากจุดเกิดได้ไม่เกินนี้")]
    [Min(0f)] public float wanderRadius = 8f;
    [Tooltip("ยืนนิ่งกี่วินาที (สุ่มในช่วง) ก่อนเดินต่อ")]
    public Vector2 idleTimeRange = new Vector2(1.5f, 4f);
    [Tooltip("สงสัยแล้ว player ออกนอกระยะแรกนานเท่านี้ ถึงเลิกสนใจ")]
    [Min(0f)] public float forgetTime = 3f;

    [Header("Thinking")]
    [Tooltip("คิดใหม่ (หาเป้า/เปลี่ยน state) ทุกกี่วินาที")]
    [Min(0.02f)] public float thinkInterval = 0.2f;
    [Tooltip("หาเส้นทางใหม่ตอนไล่เป้าทุกกี่วินาที (เป้าขยับ)")]
    [Min(0.1f)] public float repathInterval = 0.6f;
    [Tooltip("ถึงจุดบนเส้นทางเมื่อห่าง (แนวนอน) ไม่เกินนี้")]
    [Min(0.05f)] public float waypointReach = 0.45f;
    [Tooltip("ไม่ขยับเข้าใกล้จุดหมายเลยนานเท่านี้ = ติด ให้หาทางใหม่")]
    [Min(0.2f)] public float stuckTime = 1.5f;

    [Header("Death")]
    [Tooltip("ศพอยู่กี่วินาทีก่อนหายไป (0 = ไม่หาย)")]
    [Min(0f)] public float corpseLifetime = 6f;

    public EnemyState State { get; private set; } = EnemyState.Idle;
    public Health Target { get; private set; }

    private EnemyMotor _motor;
    private Health _health;
    private MeleeAttack _attack;
    private MapNavGraph _graph;
    private System.Random _rng;
    private Vector3 _home;
    private bool _hasHome;

    private float _thinkTimer;
    private float _stateTimer;     // Idle: เวลาที่เหลือ / Suspicious: เวลาที่เป้าอยู่นอกระยะ
    private float _repathTimer;
    private float _deathTimer;

    private readonly List<NavWaypoint> _path = new List<NavWaypoint>();
    private int _pathIndex;
    private float _bestDistance;
    private float _stuckTimer;

    [Header("Pack")]
    [Tooltip("ตัวเองเริ่มไล่เป้า (Chase) -> เพื่อนใน pack เดียวกันไล่เป้านั้นด้วย")]
    public bool alertPackOnAggro = true;

    // pack ที่สังกัด (null = ตัวเดี่ยว)
    public EnemyPack Pack { get; private set; }

    // ตั้งค่าตอน spawn: กราฟของแผนที่นี้ + seed (สุ่มพฤติกรรมซ้ำได้) + pack (ใช้จุดกลาง pack เป็นจุดเดินเล่น)
    // วางในฉากเองโดยไม่เรียก = หา generator ในฉากเดียวกันใน Start
    public void Initialize(MapNavGraph graph, int seed, EnemyPack pack = null)
    {
        _graph = graph;
        _rng = new System.Random(seed);
        Pack = pack;
        if (pack != null)
        {
            pack.Add(this);
            _home = pack.Center;
            _hasHome = true;
        }
    }

    // ถูกเพื่อนใน pack เรียกให้ไล่เป้า (ฝั่ง server)
    public void Alert(Health target)
    {
        if (State == EnemyState.Dead || State == EnemyState.Chase || !IsValidTarget(target)) return;
        Target = target;
        EnterChase(false);
    }

    private void Awake()
    {
        _motor = GetComponent<EnemyMotor>();
        _health = GetComponent<Health>();
        _attack = GetComponent<MeleeAttack>();
        _health.team = Team.Enemy;
    }

    private void OnEnable() => _health.Died += OnDied;
    private void OnDisable() => _health.Died -= OnDied;

    private void Start()
    {
        if (!_hasHome) _home = _motor.FootPosition;
        if (_rng == null) _rng = new System.Random(System.Environment.TickCount ^ name.GetHashCode());
        if (_graph == null)
        {
            foreach (var gen in FindObjectsByType<ProceduralMapGenerator>())
                if (gen.gameObject.scene == gameObject.scene) { _graph = gen.NavGraph; break; }
        }
        EnterIdle();
    }

    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        if (State == EnemyState.Dead)
        {
            if (corpseLifetime > 0f && (_deathTimer -= dt) <= 0f) Destroy(gameObject);
            return;
        }

        _thinkTimer -= dt;
        if (_thinkTimer <= 0f)
        {
            _thinkTimer = thinkInterval;
            Think(thinkInterval);
        }
        Act(dt);
    }

    // ---------- Decide (ทุก thinkInterval) ----------

    private void Think(float dt)
    {
        switch (State)
        {
            case EnemyState.Idle:
            case EnemyState.Wander:
            {
                Health seen = FindNearestTarget(detectRange, out float dist);
                if (seen != null)
                {
                    Target = seen;
                    if (dist <= aggroRange) EnterChase(); else EnterSuspicious();
                    return;
                }
                if (State == EnemyState.Idle && (_stateTimer -= dt) <= 0f) EnterWander();
                break;
            }

            case EnemyState.Suspicious:
            {
                // มีตัวอื่นเข้าระยะสองก่อน -> lock ตัวนั้น
                Health close = FindNearestTarget(aggroRange, out _);
                if (close != null) { Target = close; EnterChase(); return; }

                if (!IsValidTarget(Target)) { Target = FindNearestTarget(detectRange, out _); if (Target == null) { EnterIdle(); return; } }

                float d = Distance(Target);
                if (d > detectRange)
                {
                    _stateTimer += dt;
                    if (_stateTimer >= forgetTime) { Target = null; EnterIdle(); return; }
                }
                else _stateTimer = 0f;

                RepathTowardTarget(dt);
                break;
            }

            case EnemyState.Chase:
            {
                if (!IsValidTarget(Target) || Distance(Target) > loseTargetRange)
                {
                    Target = null;
                    EnterIdle();
                    return;
                }
                RepathTowardTarget(dt);
                break;
            }
        }
    }

    private void RepathTowardTarget(float dt)
    {
        _repathTimer -= dt;
        if (_repathTimer > 0f && _pathIndex < _path.Count) return;
        _repathTimer = repathInterval;
        SetDestination(Target.transform.position);
    }

    // ---------- Act (ทุก tick) ----------

    private void Act(float dt)
    {
        if (_attack.IsAttacking)
        {
            _motor.Move(_motor.transform.forward * _attack.MoveSpeedMultiplier, false);
            if (Target != null) _motor.Face(Target.transform.position - transform.position);
            return;
        }

        if (State == EnemyState.Chase && Target != null && InAttackPosition(Target))
        {
            _motor.Stop();
            Vector3 toTarget = Target.transform.position - transform.position;
            _motor.Face(toTarget);
            // หันเกือบตรงแล้วค่อยต่อย ท่าไม่ออกข้างตัว
            if (Vector3.Angle(Flat(transform.forward), Flat(toTarget)) < 30f) _attack.TryAttack(toTarget);
            return;
        }

        bool arrived = FollowPath(dt, State == EnemyState.Chase);
        if (arrived && State == EnemyState.Wander) EnterIdle();
    }

    // เดินตามเส้นทาง: จุดแบบ Jump = ยืนที่จุดก่อนหน้าแล้วกระโดดไปลง / คืน true ถ้าถึงปลายทางแล้ว (หรือไม่มีทาง)
    private bool FollowPath(float dt, bool run)
    {
        if (_pathIndex >= _path.Count)
        {
            _motor.Stop();
            return true;
        }
        if (_motor.IsJumping || !_motor.IsGrounded)
        {
            _motor.Stop(); // ลอยอยู่ ปล่อยวิถีกระโดดทำงาน
            return false;
        }

        NavWaypoint wp = _path[_pathIndex];
        Vector3 foot = _motor.FootPosition;
        Vector3 to = wp.position - foot;
        float flatDist = Flat(to).magnitude;

        if (wp.edge == NavEdge.JumpUp || wp.edge == NavEdge.JumpDown)
        {
            // ยืนที่จุดตั้งท่าแล้ว (ถึงจุดก่อนหน้าแล้วถึงได้มาถึง index นี้) -> กระโดดไปลงจุดนี้เลย
            _motor.Stop();
            _motor.Face(to);
            if (_motor.JumpTo(wp.position)) { _pathIndex++; ResetProgress(); }
            else if (TickStuck(flatDist, dt)) Repath();
            return false;
        }

        if (flatDist <= waypointReach && Mathf.Abs(to.y) < 1f)
        {
            _pathIndex++;
            ResetProgress();
            if (_pathIndex >= _path.Count) { _motor.Stop(); return true; }
            return false;
        }

        _motor.Move(Flat(to).normalized, run);
        if (TickStuck(flatDist, dt)) Repath();
        return false;
    }

    // ไม่เข้าใกล้จุดหมายเลยนานเกิน stuckTime
    private bool TickStuck(float distance, float dt)
    {
        if (distance < _bestDistance - 0.05f)
        {
            _bestDistance = distance;
            _stuckTimer = 0f;
            return false;
        }
        _stuckTimer += dt;
        return _stuckTimer >= stuckTime;
    }

    private void ResetProgress()
    {
        _bestDistance = float.PositiveInfinity;
        _stuckTimer = 0f;
    }

    private void Repath()
    {
        ResetProgress();
        if ((State == EnemyState.Chase || State == EnemyState.Suspicious) && Target != null) SetDestination(Target.transform.position);
        else EnterIdle(); // เดินเล่นแล้วติด -> พักแล้วสุ่มจุดใหม่
    }

    private void SetDestination(Vector3 destination)
    {
        _pathIndex = 0;
        ResetProgress();
        if (_graph != null)
        {
            if (_graph.FindPath(_motor.FootPosition, destination, _motor.maxJumpUp, _motor.maxJumpDown, _path)) return;
            _path.Clear();
            return; // ไปไม่ถึง (เป้าอยู่ที่ที่กระโดดขึ้นไม่ได้) ยืนรอ ถึงรอบ repath ค่อยลองใหม่
        }

        // ไม่มีกราฟ (ฉากที่ไม่มีแผนที่ procedural) -> เดินตรงเข้าหา
        _path.Clear();
        _path.Add(new NavWaypoint { position = destination, edge = NavEdge.Walk });
    }

    // ---------- State transitions ----------

    private void EnterIdle()
    {
        State = EnemyState.Idle;
        _path.Clear();
        _pathIndex = 0;
        _motor.Stop();
        _stateTimer = Mathf.Lerp(idleTimeRange.x, idleTimeRange.y, (float)_rng.NextDouble());
    }

    private void EnterWander()
    {
        State = EnemyState.Wander;
        if (_graph != null)
        {
            int node = _graph.RandomNodeNear(_home, wanderRadius, _rng);
            if (node < 0 || !_graph.FindPath(_motor.FootPosition, _graph.GetNode(node).position, _motor.maxJumpUp, _motor.maxJumpDown, _path))
            {
                EnterIdle();
                return;
            }
            _pathIndex = 0;
            ResetProgress();
            return;
        }

        float angle = (float)(_rng.NextDouble() * Mathf.PI * 2f);
        float r = wanderRadius * Mathf.Sqrt((float)_rng.NextDouble());
        SetDestination(_home + new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r));
    }

    private void EnterSuspicious()
    {
        State = EnemyState.Suspicious;
        _stateTimer = 0f;
        _repathTimer = 0f;
    }

    private void EnterChase(bool alertPack = true)
    {
        State = EnemyState.Chase;
        _repathTimer = 0f;
        if (alertPack && alertPackOnAggro && Pack != null) Pack.Alert(Target, this);
    }

    private void OnDestroy()
    {
        if (Pack != null) Pack.Remove(this);
    }

    private void OnDied(Health health)
    {
        State = EnemyState.Dead;
        Target = null;
        _path.Clear();
        _attack.Cancel();
        _motor.Die(health.LastDamage.direction); // กระเด็นไปทางที่โดนตี (ลอยอยู่ก็ตกลงพื้นก่อนค่อยแช่ตัว)
        _deathTimer = corpseLifetime;
    }

    // ---------- Targets ----------

    private Health FindNearestTarget(float range, out float distance)
    {
        Health best = null;
        distance = float.PositiveInfinity;
        foreach (var h in Health.All)
        {
            if (!IsValidTarget(h)) continue;
            float d = Distance(h);
            if (d > range || d >= distance) continue;
            distance = d;
            best = h;
        }
        return best;
    }

    private bool IsValidTarget(Health h) => h != null && h.isActiveAndEnabled && !h.IsDead && h.team != _health.team;

    private float Distance(Health h) => Vector3.Distance(h.transform.position, transform.position);

    private bool InAttackPosition(Health target)
    {
        Vector3 to = target.transform.position - transform.position;
        return Mathf.Abs(to.y) <= attackMaxHeightDiff && Flat(to).magnitude <= attackRange;
    }

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 1f, 0f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, detectRange);
        Gizmos.color = new Color(1f, 0.3f, 0f, 0.7f);
        Gizmos.DrawWireSphere(transform.position, aggroRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        if (_path.Count == 0) return;
        Gizmos.color = Color.magenta;
        Vector3 prev = transform.position;
        for (int i = _pathIndex; i < _path.Count; i++)
        {
            Gizmos.DrawLine(prev, _path[i].position + Vector3.up * 0.2f);
            prev = _path[i].position + Vector3.up * 0.2f;
        }
    }
#endif
}

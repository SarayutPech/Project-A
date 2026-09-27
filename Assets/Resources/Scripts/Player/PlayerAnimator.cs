using UnityEngine;

// ส่งสถานะการเคลื่อนที่ของ player ไปให้ Animator (presentation อย่างเดียว ไม่มีผลกับ gameplay)
// อ่านสถานะจาก PlayerMovement/Rigidbody เท่านั้น ไม่อ่าน input เอง -> server headless ถอด component นี้ออกได้
// วางบน object ที่มี Animator (เช่น Player/Visual)
[RequireComponent(typeof(Animator))]
public class PlayerAnimator : MonoBehaviour
{
    [Tooltip("ถ้าเว้นว่างจะหาจาก parent")]
    public PlayerMovement movement;

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

    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int GaitHash = Animator.StringToHash("Gait");
    private static readonly int LocomotionSpeedHash = Animator.StringToHash("LocomotionSpeed");
    private static readonly int DashingHash = Animator.StringToHash("Dashing");
    private static readonly int GroundedHash = Animator.StringToHash("Grounded");
    private static readonly int VerticalSpeedHash = Animator.StringToHash("VerticalSpeed");
    private static readonly int JumpHash = Animator.StringToHash("Jump");
    private static readonly int AirJumpHash = Animator.StringToHash("AirJump");

    private Animator _animator;
    private Rigidbody _body;
    private int _seenJumpCount;
    private float _lastGroundedTime;
    private bool _jumped; // กระโดดแล้วยังไม่แตะพื้น -> ข้าม grace ทันที

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        if (movement == null) movement = GetComponentInParent<PlayerMovement>();
        if (movement != null)
        {
            _body = movement.GetComponent<Rigidbody>();
            _seenJumpCount = movement.JumpCount;
        }
    }

    private void Update()
    {
        if (movement == null || _body == null) return;

        Vector3 velocity = _body.linearVelocity;
        float vy = velocity.y;
        bool grounded = movement.IsGrounded;

        // Gait: 0 = idle, 1 = walk, 2 = run / ท่าเดินหรือวิ่งเลือกจากสถานะ sprint
        // ความเร็วใช้แค่บอกว่าขยับอยู่แค่ไหน (ออกตัว/เบรก = blend กับ idle)
        float horizontalSpeed = new Vector2(velocity.x, velocity.z).magnitude;
        float moving = movement.moveSpeed > 0f ? Mathf.Clamp01(horizontalSpeed / movement.moveSpeed) : 0f;
        float gait = moving * (movement.Sprinting ? 2f : 1f);
        _animator.SetFloat(GaitHash, gait, gaitDampTime, Time.deltaTime);
        _animator.SetFloat(SpeedHash, horizontalSpeed);

        // playback ของเดิน/วิ่ง = ความเร็วจริง / ความเร็วก้าวของท่า (ท่าผสมตาม Gait) -> เท้าไม่ไถล
        // ยังเกือบยืนนิ่ง (Gait ใกล้ 0) ให้ค่อยๆ กลับเป็น 1x ไม่งั้นท่า idle จะช้าตามไปด้วย
        float blendedGait = _animator.GetFloat(GaitHash);
        float clipSpeed = Mathf.Lerp(walkClipSpeed, runClipSpeed, Mathf.Clamp01(blendedGait - 1f));
        float playback = Mathf.Clamp(horizontalSpeed / clipSpeed, locomotionPlaybackRange.x, locomotionPlaybackRange.y);
        _animator.SetFloat(LocomotionSpeedHash, Mathf.Lerp(1f, playback, Mathf.Clamp01(blendedGait)));

        // dash: ค้างท่า dash ไว้ตลอดช่วงที่พุ่ง
        _animator.SetBool(DashingHash, movement.IsDashing);

        // กระโดดจริงเท่านั้น (นับจาก PlayerMovement) ไม่เดาจากความเร็ว -> วิ่งขึ้นทางลาดแล้ว vy พุ่งขึ้นไม่นับเป็นกระโดด
        if (movement.JumpCount != _seenJumpCount)
        {
            _seenJumpCount = movement.JumpCount;
            _animator.SetTrigger(movement.LastJumpFromGround ? JumpHash : AirJumpHash);
            _jumped = true;
        }

        // PlayerMovement ไม่นับว่าแตะพื้นระหว่างตัวยังพุ่งขึ้นหลังกระโดด -> grounded = ลงพื้นจริง (รวมลงบนทางลาดขาขึ้น)
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

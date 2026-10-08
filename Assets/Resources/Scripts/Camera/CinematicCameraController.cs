using UnityEngine;
using UnityEngine.InputSystem;

// มุมกล้อง cinematic สำหรับอัดคลิปโปรโมต (ฝั่งภาพล้วน ไม่แตะ simulation)
// วางบน Camera เดียวกับ IsometricCameraController: iso ยังคงตาม player อยู่เบื้องหลัง
// ตัวนี้รันหลัง iso (ExecutionOrder สูงกว่า) แล้ว "ทับ" ท่ากล้องด้วยมุมสวย ๆ — ปิดโหมดแล้ว iso รับช่วงต่อได้ทันทีโดยไม่กระตุก
//
// สิ่งที่ทำ:
//  - โฟกัส player แต่ดึงจุดมองไปทางศัตรูที่อยู่ใกล้ (ถ่วงน้ำหนักตามระยะ) ให้เห็นการปะทะ ไม่ใช่แค่ตัวละคร
//  - ซูมออกเมื่อศัตรูกระจายตัว / ซูมเข้า (kick) ตอน player ตีโดน และแรงขึ้นตอนฆ่า + กล้องสั่นเบา ๆ ตอนโดน/ตี
//  - หลายมุม (shot) สลับด้วยปุ่ม: perspective ก้มเฉียง, มุมต่ำ, orbit ช้า ๆ, iso กว้าง
//  - ทุกอย่างผ่าน SmoothDamp + blend เข้า/ออกโหมด
// ปุ่ม: F8 = เปิด/ปิดโหมด, F9 = เปลี่ยนมุม (ใช้ได้ตอนเปิดโหมดเท่านั้น)
[System.Serializable]
public class CinematicShot
{
    public string name = "Shot";
    [Range(5f, 89f)] public float pitch = 38f;
    public float yaw = 40f;
    [Tooltip("ระยะกล้องจากจุดโฟกัส (หน่วยเดียวกับ IsometricCameraController.followDistance)")]
    [Min(1f)] public float distance = 13f;
    [Range(10f, 90f)] public float fov = 35f;
    public bool orthographic = false;
    [Tooltip("หมุนรอบตัวละครช้า ๆ (องศา/วินาที) 0 = นิ่ง")]
    public float orbitSpeed = 0f;

    public CinematicShot() { }

    public CinematicShot(string name, float pitch, float yaw, float distance, float fov, bool orthographic = false, float orbitSpeed = 0f)
    {
        this.name = name;
        this.pitch = pitch;
        this.yaw = yaw;
        this.distance = distance;
        this.fov = fov;
        this.orthographic = orthographic;
        this.orbitSpeed = orbitSpeed;
    }
}

[DefaultExecutionOrder(200)]
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public class CinematicCameraController : MonoBehaviour
{
    [Header("Input (เครื่องมือ dev ไม่ลงทะเบียนใน KeyBindings)")]
    public InputAction toggleAction = new InputAction("Cinematic Toggle", InputActionType.Button, "<Keyboard>/f8");
    public InputAction nextShotAction = new InputAction("Cinematic Next Shot", InputActionType.Button, "<Keyboard>/f9");
    [Tooltip("เริ่มเกมแล้วเปิดโหมดเลย")]
    public bool startActive = false;

    [Header("Target")]
    [Tooltip("ถ้าเว้นว่างจะใช้ target ของ IsometricCameraController หรือ object ที่ tag = Player")]
    public Transform target;
    [Tooltip("จุดที่มองเทียบกับ pivot ของ player")]
    public Vector3 targetOffset = new Vector3(0f, 1.2f, 0f);

    [Header("Shots")]
    public CinematicShot[] shots =
    {
        new CinematicShot("Hero 3/4",  38f, 40f, 13f, 35f),
        new CinematicShot("Low Angle", 18f, 35f, 10f, 40f),
        new CinematicShot("Orbit",     32f, 45f, 14f, 35f, false, 8f),
        new CinematicShot("Iso Wide",  35.26f, 45f, 20f, 30f, true),
    };
    [Min(0)] public int shotIndex = 0;

    [Header("Combat Framing (ดึงกล้องไปหาศัตรู)")]
    [Tooltip("ศัตรูที่อยู่ห่าง player เกินระยะนี้ (เมตร) ไม่นับ")]
    [Min(1f)] public float combatRadius = 12f;
    [Tooltip("ความแรงที่ดึงจุดมองไปทางศัตรู (0 = ตาม player ล้วน)")]
    [Range(0f, 1.5f)] public float focusBias = 1f;
    [Tooltip("จุดมองห่างจาก player ได้สูงสุด (เมตร) กัน player หลุดขอบจอ")]
    [Min(0f)] public float maxFocusShift = 4f;
    [Tooltip("ศัตรูไกลสุดอยู่ในระยะนี้ยังไม่ซูมออก")]
    [Min(0f)] public float spreadFree = 4f;
    [Tooltip("ซูมออกกี่เมตรต่อ 1 เมตรที่ศัตรูไกลเกิน Spread Free")]
    [Min(0f)] public float spreadZoom = 0.6f;
    [Min(0f)] public float maxExtraDistance = 6f;

    [Header("Smoothing (วินาที)")]
    [Min(0.01f)] public float pivotSmoothTime = 0.25f;
    [Min(0.01f)] public float zoomSmoothTime = 0.6f;
    [Min(0.01f)] public float angleSmoothTime = 0.8f;
    [Tooltip("เวลาเฟดเข้า/ออกโหมดจากกล้องปกติ")]
    [Min(0f)] public float blendTime = 0.8f;

    [Header("Hit Reaction")]
    [Tooltip("ซูมเข้า (เมตร) ตอน player ตีโดน / ฆ่า")]
    [Min(0f)] public float hitKick = 0.25f;
    [Min(0f)] public float killKick = 0.9f;
    [Min(0f)] public float maxKick = 1.5f;
    [Tooltip("ความเร็วคืนตัวของ kick (ยิ่งมากยิ่งคืนเร็ว)")]
    [Min(0.1f)] public float kickRecover = 5f;
    [Tooltip("แรงสั่นสูงสุด (องศา)")]
    [Min(0f)] public float shakeAngle = 2.5f;
    [Range(0f, 1f)] public float hitShake = 0.25f;
    [Range(0f, 1f)] public float killShake = 0.6f;
    [Range(0f, 1f)] public float hurtShake = 0.5f;
    [Min(0.1f)] public float shakeDecay = 1.8f;
    [Min(0.1f)] public float shakeFrequency = 22f;

    private Camera _camera;
    private IsometricCameraController _iso;
    private Transform _resolvedTarget;

    private bool _active;
    private float _blend;          // 0 = กล้องปกติ, 1 = cinematic เต็ม
    private bool _projectionOwned; // true = เรากำลังตั้ง fov/projection ของกล้องอยู่
    private float _savedFov;

    private Vector3 _pivot, _pivotVel;
    private float _dist, _distVel;
    private float _pitch, _pitchVel;
    private float _yaw, _yawVel;
    private float _fov, _fovVel;
    private float _orbitOffset;
    private float _kick;
    private float _trauma;

    // กล้องของมุมมองเครื่องนี้ (ฝั่งภาพ ไม่เกี่ยวกับ simulation / ไม่ผูกกับ "ผู้เล่นคนเดียว") มีได้ตัวเดียว
    // ตัวซ้ำจะถูกทำลายเอง CinematicCameraBootstrap เป็นคนใส่ให้ทุกฉากที่มีกล้อง iso
    public static CinematicCameraController Instance { get; private set; }
    // จำว่าเปิดโหมดค้างไว้ไหม ข้ามฉาก (Hideout -> map) ไม่ต้องกด F8 ใหม่ทุกฉาก
    private static bool s_wasActive;
    private bool _duplicate;

    public bool IsActive => _active;
    public CinematicShot CurrentShot => shots != null && shots.Length > 0 ? shots[Mathf.Clamp(shotIndex, 0, shots.Length - 1)] : null;

    private Transform Target
    {
        get
        {
            if (target != null) return target;
            if (_iso != null && _iso.target != null) return _iso.target;
            if (_resolvedTarget == null)
            {
                var player = GameObject.FindWithTag("Player");
                if (player != null) _resolvedTarget = player.transform;
            }
            return _resolvedTarget;
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            _duplicate = true;
            Destroy(this);
            return;
        }
        Instance = this;
        _camera = GetComponent<Camera>();
        _iso = GetComponent<IsometricCameraController>();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void OnEnable()
    {
        if (_duplicate) return;
        toggleAction.Enable();
        nextShotAction.Enable();
        Health.AnyDamaged += OnAnyDamaged;
        if (startActive || s_wasActive) SetActive(true);
    }

    private void OnDisable()
    {
        if (_duplicate) return;
        Health.AnyDamaged -= OnAnyDamaged;
        toggleAction.Disable();
        nextShotAction.Disable();
        _active = false;
        _blend = 0f;
        ReleaseProjection();
    }

    private void Update()
    {
        if (LocalInputGate.KeyboardCaptured) return; // กำลังพิมพ์ (เช่นชื่อตัวละคร)

        if (toggleAction.WasPressedThisFrame()) SetActive(!_active);
        if (_active && nextShotAction.WasPressedThisFrame()) NextShot();
    }

    [ContextMenu("Toggle Cinematic")]
    public void Toggle() => SetActive(!_active);

    public void SetActive(bool on)
    {
        if (on == _active) return;
        _active = on;
        s_wasActive = on;
        if (on) ResetState();
    }

    [ContextMenu("Next Shot")]
    public void NextShot()
    {
        if (shots == null || shots.Length == 0) return;
        shotIndex = (shotIndex + 1) % shots.Length;
        _orbitOffset = 0f; // เริ่มนับ orbit ใหม่ ค่ามุมจะไหลไปหาท่าใหม่เองผ่าน SmoothDamp
    }

    // ดันกล้องเข้า/สั่น เองจากโค้ดอื่น (เช่น cutscene, ท่าไม้ตาย)
    public void Kick(float zoomIn, float shake)
    {
        _kick = Mathf.Min(maxKick, _kick + zoomIn);
        _trauma = Mathf.Clamp01(_trauma + shake);
    }

    // เริ่มจากท่าอุดมคติของ shot ปัจจุบันเลย แล้วให้ blend เป็นตัวไล่จากกล้องปกติมาหา
    private void ResetState()
    {
        CinematicShot shot = CurrentShot;
        Transform t = Target;
        if (shot == null || t == null) return;

        _pivot = t.position + targetOffset;
        _dist = shot.distance;
        _pitch = shot.pitch;
        _yaw = shot.yaw;
        _fov = shot.fov;
        _pivotVel = Vector3.zero;
        _distVel = _pitchVel = _yawVel = _fovVel = 0f;
        _orbitOffset = 0f;
        _kick = 0f;
        _trauma = 0f;
    }

    private void OnAnyDamaged(Health victim, DamageInfo info)
    {
        if (!_active) return;
        Transform t = Target;
        if (t == null) return;

        if (info.source != null && info.source.transform.IsChildOf(t))
        {
            // player เป็นฝ่ายตี (victim.IsDead = โดนจนตายในครั้งนี้)
            bool kill = victim.IsDead;
            Kick(kill ? killKick : hitKick, kill ? killShake : hitShake);
        }
        else if (victim.transform.IsChildOf(t))
        {
            Kick(0f, hurtShake);
        }
    }

    private void LateUpdate()
    {
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
        float goal = _active ? 1f : 0f;
        _blend = blendTime <= 0f ? goal : Mathf.MoveTowards(_blend, goal, dt / blendTime);

        CinematicShot shot = CurrentShot;
        Transform t = Target;
        if (_blend <= 0f || shot == null || t == null)
        {
            ReleaseProjection();
            return;
        }

        // ---- ความต้องการของกล้องเฟรมนี้ ----
        Vector3 anchor = t.position + targetOffset;
        GatherCombat(anchor, out Vector3 shift, out float farthest);

        Vector3 desiredPivot = anchor + shift;
        float extra = Mathf.Min(maxExtraDistance, Mathf.Max(0f, farthest - spreadFree) * spreadZoom);
        float desiredDist = shot.distance + extra;

        _orbitOffset += shot.orbitSpeed * dt;

        // ---- ไหลเข้าหาเป้า ----
        _pivot = Vector3.SmoothDamp(_pivot, desiredPivot, ref _pivotVel, pivotSmoothTime, Mathf.Infinity, dt);
        _dist = Mathf.SmoothDamp(_dist, desiredDist, ref _distVel, zoomSmoothTime, Mathf.Infinity, dt);
        _pitch = Mathf.SmoothDampAngle(_pitch, shot.pitch, ref _pitchVel, angleSmoothTime, Mathf.Infinity, dt);
        _yaw = Mathf.SmoothDampAngle(_yaw, shot.yaw + _orbitOffset, ref _yawVel, angleSmoothTime, Mathf.Infinity, dt);
        _fov = Mathf.SmoothDamp(_fov, shot.fov, ref _fovVel, angleSmoothTime, Mathf.Infinity, dt);
        _kick = Mathf.Lerp(_kick, 0f, 1f - Mathf.Exp(-kickRecover * dt));
        _trauma = Mathf.Max(0f, _trauma - shakeDecay * dt);

        // ---- สั่นด้วย Perlin noise (rotation อย่างเดียว ตำแหน่งตามมาจากการหมุนรอบ pivot) ----
        float shakeAmt = _trauma * _trauma * shakeAngle;
        float n = Time.unscaledTime * shakeFrequency;
        float sp = (Mathf.PerlinNoise(n, 0.17f) * 2f - 1f) * shakeAmt;
        float sy = (Mathf.PerlinNoise(0.53f, n) * 2f - 1f) * shakeAmt;
        float sr = (Mathf.PerlinNoise(n, 7.31f) * 2f - 1f) * shakeAmt * 0.5f;

        float dist = Mathf.Max(1f, _dist - _kick);
        Quaternion rot = Quaternion.Euler(_pitch + sp, _yaw + sy, sr);
        Vector3 pos = _pivot - rot * Vector3.forward * dist;

        // ---- blend กับท่าของกล้องปกติ (iso เพิ่งตั้งไว้เฟรมนี้) ----
        float e = Mathf.SmoothStep(0f, 1f, _blend);
        transform.SetPositionAndRotation(
            Vector3.Lerp(transform.position, pos, e),
            Quaternion.Slerp(transform.rotation, rot, e));

        // projection สลับกลางทางไม่ได้ ถึงครึ่งทางค่อยสลับ (framing ที่ระนาบ pivot เท่ากันเพราะแปลงระยะ <-> ortho size เหมือน iso)
        if (e >= 0.5f)
        {
            if (!_projectionOwned)
            {
                _savedFov = _camera.fieldOfView;
                _projectionOwned = true;
            }
            _camera.fieldOfView = _fov;
            _camera.orthographic = shot.orthographic;
            if (shot.orthographic) _camera.orthographicSize = dist * Mathf.Tan(_fov * 0.5f * Mathf.Deg2Rad);
        }
        else
        {
            ReleaseProjection();
        }
    }

    // คืน fov ให้กล้องปกติ (iso ตั้ง orthographic/size เองทุกเฟรมอยู่แล้ว)
    private void ReleaseProjection()
    {
        if (!_projectionOwned) return;
        _projectionOwned = false;
        if (_camera != null) _camera.fieldOfView = _savedFov;
    }

    // รวมศัตรูที่ยังไม่ตายในระยะรอบ player: shift = ทิศ/ระยะที่ควรเลื่อนจุดมอง, farthest = ศัตรูไกลสุด (ใช้ตัดสินซูมออก)
    // ถ่วงน้ำหนักตามความใกล้ (ใกล้ = ดึงแรง) และมี "น้ำหนักของ player = 1" เสมอ จุดมองจึงไม่หลุดจาก player ไปไกลเกินจริง
    private void GatherCombat(Vector3 anchor, out Vector3 shift, out float farthest)
    {
        Vector3 sum = Vector3.zero;
        float weightSum = 0f;
        farthest = 0f;

        var all = Health.All;
        for (int i = 0; i < all.Count; i++)
        {
            Health h = all[i];
            if (h == null || h.team != Team.Enemy || h.IsDead) continue;

            Vector3 d = h.transform.position - anchor;
            d.y = 0f;
            float dist = d.magnitude;
            if (dist > combatRadius) continue;

            float w = 1f - dist / combatRadius;
            sum += d * w;
            weightSum += w;
            if (dist > farthest) farthest = dist;
        }

        shift = weightSum > 0f
            ? Vector3.ClampMagnitude(sum / (weightSum + 1f) * focusBias, maxFocusShift)
            : Vector3.zero;
    }

    private void OnDrawGizmosSelected()
    {
        Transform t = target != null ? target : null;
        if (t == null) return;
        Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.6f);
        Gizmos.DrawWireSphere(t.position, combatRadius);
    }
}

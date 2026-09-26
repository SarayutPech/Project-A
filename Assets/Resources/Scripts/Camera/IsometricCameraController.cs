using System.Collections;
using UnityEngine;

// กล้อง isometric: ตอนแผนที่กำลังโหลดจะมองจุดกลาง map / พอ MapBuildAnimator เล่นจบจะแพนไปหา player แล้วตามต่อ
// ระยะกล้องใช้ค่า distance ค่าเดียวทั้ง Perspective และ Orthographic
// (Orthographic จะแปลงเป็น orthographicSize ให้ภาพกว้างเท่ากับ perspective ที่ระยะนั้น)
[RequireComponent(typeof(Camera))]
public class IsometricCameraController : MonoBehaviour
{
    [Header("Isometric Angle")]
    [Tooltip("มุมก้มลง (องศา) isometric แท้ ~35.26")]
    [Range(10f, 89f)] public float pitch = 35.26f;
    [Tooltip("มุมหมุนรอบแกน Y (องศา)")]
    public float yaw = 45f;
    public bool orthographic = true;

    [Header("Map Overview (ระหว่างแผนที่กำลังโหลด)")]
    [Tooltip("ถ้าเว้นว่างจะใช้ ProceduralMapGenerator.Instance")]
    public ProceduralMapGenerator generator;
    [Tooltip("ถ้าเว้นว่างจะหาในฉากเอง ถ้าไม่มี animator เลยจะแพนหา player ทันที")]
    public MapBuildAnimator buildAnimator;
    [Tooltip("ปรับระยะให้เห็นทั้ง map พอดีอัตโนมัติ (ปิด = ใช้ Map View Distance)")]
    public bool fitWholeMap = true;
    [Tooltip("ระยะกล้องจากจุดกลาง map (ใช้ตอนปิด Fit Whole Map)")]
    [Min(1f)] public float mapViewDistance = 40f;
    [Tooltip("ขอบเผื่อรอบ map ตอน Fit Whole Map (world unit)")]
    [Min(0f)] public float fitPadding = 2f;

    [Header("Follow Player")]
    [Tooltip("ถ้าเว้นว่างจะใช้ PlayerMovement.Instance หรือ object ที่ tag = Player")]
    public Transform target;
    [Tooltip("ระยะกล้องจาก player")]
    [Min(1f)] public float followDistance = 18f;
    [Tooltip("จุดที่มองเทียบกับ pivot ของ player")]
    public Vector3 targetOffset = new Vector3(0f, 1f, 0f);
    [Tooltip("ความหนืดตอนตาม (วินาที) 0 = ติดแน่น")]
    [Min(0f)] public float followSmoothTime = 0.15f;

    [Header("Pan (แผนที่โหลดเสร็จ -> player)")]
    [Tooltip("รอหลังแผนที่ขึ้นครบก่อนเริ่มแพน (วินาที)")]
    [Min(0f)] public float panDelay = 0.3f;
    [Min(0.01f)] public float panDuration = 1.5f;
    public AnimationCurve panCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private enum Mode { Overview, Panning, Follow }

    private Camera _camera;
    private Mode _mode = Mode.Overview;
    private Vector3 _pivot;
    private float _distance;
    private Vector3 _followVelocity;
    private Coroutine _panRoutine;
    private ProceduralMapGenerator _subscribedGenerator;
    private MapBuildAnimator _subscribedAnimator;

    private ProceduralMapGenerator Generator => generator != null ? generator : ProceduralMapGenerator.Instance;

    private void Awake()
    {
        _camera = GetComponent<Camera>();
        _distance = mapViewDistance;
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

    // regenerate ระหว่าง Play: กลับไปมองทั้ง map แล้วรอแผนที่ขึ้นใหม่ (ถ้า animator ไม่เล่นซ้ำก็แพนกลับเลย)
    private void OnMapGenerated()
    {
        ShowMapOverview();
        if (buildAnimator == null || !buildAnimator.isActiveAndEnabled || !buildAnimator.replayOnRegenerate) OnMapBuilt();
    }

    private void Start()
    {
        if (target == null && PlayerMovement.Instance != null) target = PlayerMovement.Instance.transform;
        if (target == null)
        {
            var player = GameObject.FindWithTag("Player");
            if (player != null) target = player.transform;
        }

        if (buildAnimator == null) buildAnimator = FindFirstObjectByType<MapBuildAnimator>();
        if (buildAnimator != null)
        {
            _subscribedAnimator = buildAnimator;
            _subscribedAnimator.onBuildFinished.AddListener(OnMapBuilt);
        }

        ShowMapOverview();

        // ไม่มี animator (หรือไม่ได้ตั้งให้เล่น) = แผนที่พร้อมแล้ว แพนหา player เลย
        if (buildAnimator == null || !buildAnimator.isActiveAndEnabled || !buildAnimator.playOnStart) OnMapBuilt();
    }

    private void OnDestroy()
    {
        if (_subscribedAnimator != null) _subscribedAnimator.onBuildFinished.RemoveListener(OnMapBuilt);
    }

    // วางกล้องมองจุดกลาง map ทันที
    [ContextMenu("Show Map Overview")]
    public void ShowMapOverview()
    {
        if (_panRoutine != null) StopCoroutine(_panRoutine);
        _panRoutine = null;
        _mode = Mode.Overview;

        var gen = Generator;
        if (gen != null)
        {
            _pivot = gen.MapBounds.center;
            _distance = mapViewDistance;
            if (fitWholeMap) FitToMap(gen);
        }
        ApplyTransform();
    }

    private void OnMapBuilt()
    {
        if (_panRoutine != null) StopCoroutine(_panRoutine);
        _panRoutine = StartCoroutine(PanToTarget());
    }

    [ContextMenu("Pan To Target")]
    public void PanToTargetNow() => OnMapBuilt();

    private IEnumerator PanToTarget()
    {
        if (panDelay > 0f) yield return new WaitForSeconds(panDelay);
        if (target == null)
        {
            _panRoutine = null;
            yield break;
        }

        _mode = Mode.Panning;
        Vector3 fromPivot = _pivot;
        float fromDistance = _distance;

        for (float t = 0f; t < panDuration; t += Time.deltaTime)
        {
            float e = panCurve != null && panCurve.length > 0 ? panCurve.Evaluate(t / panDuration) : t / panDuration;
            // เป้าหมายอ่านใหม่ทุกเฟรม เผื่อ player ขยับระหว่างแพน
            _pivot = Vector3.LerpUnclamped(fromPivot, TargetPoint(), e);
            _distance = Mathf.LerpUnclamped(fromDistance, followDistance, e);
            yield return null;
        }

        _pivot = TargetPoint();
        _distance = followDistance;
        _followVelocity = Vector3.zero;
        _mode = Mode.Follow;
        _panRoutine = null;
    }

    private void LateUpdate()
    {
        if (_mode == Mode.Follow && target != null)
        {
            _pivot = followSmoothTime > 0f
                ? Vector3.SmoothDamp(_pivot, TargetPoint(), ref _followVelocity, followSmoothTime)
                : TargetPoint();
            _distance = followDistance;
        }

        ApplyTransform();
    }

    private Vector3 TargetPoint() => target.position + targetOffset;

    private void ApplyTransform()
    {
        if (_camera == null) _camera = GetComponent<Camera>();

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        transform.SetPositionAndRotation(_pivot - rotation * Vector3.forward * _distance, rotation);

        _camera.orthographic = orthographic;
        if (orthographic) _camera.orthographicSize = _distance * HalfFovTan();
    }

    // tan(ครึ่งมุมมองแนวตั้ง) ใช้แปลงระยะ <-> ขนาดภาพ
    private float HalfFovTan() => Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);

    // หา pivot + ระยะที่ทำให้ map อยู่เต็มจอพอดี:
    // เอา vertex จริงของพื้น/ใต้เกาะมาแปลงเป็นพิกัดมุมกล้อง (x = ขวา, y = ขึ้น, z = ลึก) แล้วหากรอบที่ครอบ
    // ได้ผลแน่นกว่าการใช้ bounds ของโลก เพราะ map รูปทางคดเคี้ยวหมุน 45° จะเหลือมุมว่างเยอะ และมุมก้มทำให้แกนลึกสั้นลงบนจอ
    private void FitToMap(ProceduralMapGenerator gen)
    {
        if (_camera == null) _camera = GetComponent<Camera>();

        CollectFitPoints(gen, FitPoints);
        if (FitPoints.Count == 0) return;

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Quaternion toView = Quaternion.Inverse(rotation);

        Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
        for (int i = 0; i < FitPoints.Count; i++)
        {
            Vector3 v = toView * FitPoints[i];
            FitPoints[i] = v;
            min = Vector3.Min(min, v);
            max = Vector3.Max(max, v);
        }

        // จุดกลางบนจอ ส่วนความลึกให้อยู่ระดับกลาง map เดิม (pivot จะได้อยู่ใกล้พื้น แพนไปหา player ได้เนียน)
        float pivotZ = (toView * gen.MapBounds.center).z;
        float cx = (min.x + max.x) * 0.5f;
        float cy = (min.y + max.y) * 0.5f;
        _pivot = rotation * new Vector3(cx, cy, pivotZ);

        float tanV = HalfFovTan();
        float tanH = tanV * Mathf.Max(0.01f, _camera.aspect);
        float distance;

        if (orthographic)
        {
            // ortho: ขนาดภาพไม่ขึ้นกับความลึก ใช้ครึ่งกว้าง/ครึ่งสูงของกรอบตรง ๆ
            float halfW = (max.x - min.x) * 0.5f + fitPadding;
            float halfH = (max.y - min.y) * 0.5f + fitPadding;
            distance = Mathf.Max(halfH, halfW / Mathf.Max(0.01f, _camera.aspect)) / tanV;
        }
        else
        {
            // perspective: จุดที่อยู่ใกล้กล้องกว่า pivot กินพื้นที่จอมากกว่า ต้องเช็คทีละจุด
            // จุดอยู่ในจอเมื่อ |x - cx| <= (distance + ความลึกเทียบ pivot) * tanH (แกน y เช่นกัน)
            distance = 0f;
            foreach (Vector3 v in FitPoints)
            {
                float dz = v.z - pivotZ;
                distance = Mathf.Max(distance,
                    (Mathf.Abs(v.x - cx) + fitPadding) / tanH - dz,
                    (Mathf.Abs(v.y - cy) + fitPadding) / tanV - dz);
            }
        }

        // กันจุดที่ใกล้กล้องที่สุดหลุดหลัง near clip plane
        distance = Mathf.Max(distance, pivotZ - min.z + _camera.nearClipPlane + fitPadding);
        _distance = Mathf.Max(1f, distance);
        FitPoints.Clear();
    }

    private static readonly System.Collections.Generic.List<Vector3> FitPoints = new System.Collections.Generic.List<Vector3>();
    private static readonly System.Collections.Generic.List<Vector3> MeshVertices = new System.Collections.Generic.List<Vector3>();

    // vertex (world space) ของพื้น + ใต้เกาะ ถ้ายังไม่มี mesh ใช้มุมของ MapBounds แทน
    private static void CollectFitPoints(ProceduralMapGenerator gen, System.Collections.Generic.List<Vector3> points)
    {
        points.Clear();
        AddMeshPoints(gen.GroundObject, points);
        AddMeshPoints(gen.UndersideObject, points);
        if (points.Count > 0) return;

        Bounds b = gen.MapBounds;
        points.Add(new Vector3(b.min.x, 0f, b.min.z));
        points.Add(new Vector3(b.min.x, 0f, b.max.z));
        points.Add(new Vector3(b.max.x, 0f, b.min.z));
        points.Add(new Vector3(b.max.x, 0f, b.max.z));
    }

    // MapBuildAnimator อาจกำลังปิด/เลื่อนลง/ย่อ object พวกนี้อยู่ จึงรวม object ที่ปิดอยู่ด้วย
    // และใช้ matrix ของ root (ground/underside ถูกสร้างไว้ใต้ root ที่ local identity) แทน transform ของตัวเองที่กำลังถูก animate
    private static void AddMeshPoints(GameObject obj, System.Collections.Generic.List<Vector3> points)
    {
        if (obj == null) return;
        Transform root = obj.transform.parent;
        Matrix4x4 m = root != null ? root.localToWorldMatrix : Matrix4x4.identity;

        foreach (var mf in obj.GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh mesh = mf.sharedMesh;
            if (mesh == null || !mesh.isReadable) continue;

            mesh.GetVertices(MeshVertices);
            foreach (Vector3 v in MeshVertices) points.Add(m.MultiplyPoint3x4(v));
        }
        MeshVertices.Clear();
    }
}

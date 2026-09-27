using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// เงาตามหลังตอน dash (แบบ Rockman X): ระหว่าง dash จะถ่ายท่าปัจจุบันของตัวละครเป็น mesh นิ่งทิ้งไว้เป็นระยะ แล้วค่อยๆ จางหาย
// presentation อย่างเดียว อ่านแค่ PlayerMovement.IsDashing / server headless ปิดตัวเองอัตโนมัติ
// วางบน object ที่มีโมเดล (เช่น Player/Visual) จะถ่ายทุก SkinnedMeshRenderer ข้างใต้
public class DashAfterimage : MonoBehaviour
{
    [Tooltip("ถ้าเว้นว่างจะหาจาก parent")]
    public PlayerMovement movement;
    [Tooltip("material ของเงา (shader ProjectA/Afterimage) ถ้าเว้นว่างจะสร้างให้")]
    public Material material;
    [Tooltip("สีหลักของเงา (คูณกับ Color Over Lifetime) / ความเรือง/ความทึบตรงกลาง/โหมดผสมสี ปรับที่ material")]
    public Color color = new Color(1f, 0.35f, 0.75f, 1f);
    [Tooltip("สีและความทึบตามอายุของเงา (ซ้าย = เพิ่งเกิด, ขวา = กำลังหาย) เช่น ขาว->ชมพู หรือเปลี่ยนสีระหว่างจาง")]
    public Gradient colorOverLifetime = new Gradient
    {
        colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
        alphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) },
    };

    [Tooltip("ทิ้งเงาทุกกี่วินาทีระหว่าง dash")]
    [Min(0.01f)] public float spawnInterval = 0.035f;
    [Tooltip("เงาแต่ละอันอยู่นานกี่วินาทีก่อนหายหมด")]
    [Min(0.02f)] public float lifetime = 0.25f;
    [Tooltip("ความทึบรวม (คูณกับ alpha ของ Color Over Lifetime)")]
    [Range(0f, 1f)] public float startAlpha = 0.8f;
    [Tooltip("ทิ้งเงาต่ออีกกี่วินาทีหลัง dash จบ")]
    [Min(0f)] public float trailAfterDash = 0.05f;

    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private class Ghost
    {
        public GameObject obj;
        public Mesh mesh;
        public MeshRenderer renderer;
        public float age;
    }

    private readonly List<Ghost> _ghosts = new List<Ghost>();
    private readonly List<Mesh> _bakeMeshes = new List<Mesh>();
    private SkinnedMeshRenderer[] _skins;
    private CombineInstance[] _combine;
    private MaterialPropertyBlock _block;
    private Transform _container;
    private Material _ownedMaterial;
    private float _spawnTimer;
    private float _lastDashTime = float.NegativeInfinity;
    private bool _wasEmitting;

    private void Awake()
    {
        // server headless ไม่มีอะไรให้วาด
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
        {
            enabled = false;
            return;
        }

        if (movement == null) movement = GetComponentInParent<PlayerMovement>();
        _skins = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        _combine = new CombineInstance[_skins.Length];
        for (int i = 0; i < _skins.Length; i++) _bakeMeshes.Add(new Mesh { name = "AfterimageBake" });
        _block = new MaterialPropertyBlock();

        if (material == null)
        {
            _ownedMaterial = new Material(Shader.Find("ProjectA/Afterimage")) { name = "Afterimage (auto)" };
            material = _ownedMaterial;
        }
    }

    private void OnDestroy()
    {
        ClearGhosts();
        foreach (var m in _bakeMeshes) Destroy(m);
        _bakeMeshes.Clear();
        if (_container != null) Destroy(_container.gameObject);
        if (_ownedMaterial != null) Destroy(_ownedMaterial);
    }

    private void OnDisable()
    {
        // ตอน unload scene container อาจถูกทำลายก่อน component นี้ -> ข้ามตัวที่หายไปแล้ว
        foreach (var g in _ghosts)
            if (g.obj != null) g.obj.SetActive(false);
        _wasEmitting = false;
    }

    // mesh เป็นของเราเอง ต้อง Destroy เองแม้ GameObject ของเงาจะถูกทำลายไปแล้ว
    private void ClearGhosts()
    {
        foreach (var g in _ghosts)
        {
            if (g.mesh != null) Destroy(g.mesh);
            if (g.obj != null) Destroy(g.obj);
        }
        _ghosts.Clear();
    }

    // LateUpdate: หลัง Animator อัปเดตท่าของเฟรมนี้แล้ว
    private void LateUpdate()
    {
        // container หายไปกับ scene เก่า (เช่น player ข้าม scene) -> ทิ้ง pool เดิม สร้างใหม่ตอนใช้
        if (_container == null && _ghosts.Count > 0) ClearGhosts();

        if (movement != null && movement.IsDashing) _lastDashTime = Time.time;
        bool emitting = Time.time - _lastDashTime <= trailAfterDash;

        if (emitting)
        {
            // เริ่ม dash = ทิ้งเงาอันแรกทันที
            _spawnTimer = _wasEmitting ? _spawnTimer - Time.deltaTime : 0f;
            if (_spawnTimer <= 0f)
            {
                Spawn();
                _spawnTimer += spawnInterval;
            }
        }
        _wasEmitting = emitting;

        foreach (var g in _ghosts)
        {
            if (!g.obj.activeSelf) continue;
            g.age += Time.deltaTime;
            float t = g.age / lifetime;
            if (t >= 1f)
            {
                g.obj.SetActive(false);
                continue;
            }
            SetGhostColor(g, t);
        }
    }

    private void SetGhostColor(Ghost g, float lifeT)
    {
        Color c = color * colorOverLifetime.Evaluate(lifeT);
        c.a *= startAlpha;
        _block.SetColor(ColorId, c);
        g.renderer.SetPropertyBlock(_block);
    }

    private void Spawn()
    {
        int count = 0;
        for (int i = 0; i < _skins.Length; i++)
        {
            var skin = _skins[i];
            if (skin == null || !skin.enabled || !skin.gameObject.activeInHierarchy) continue;
            skin.BakeMesh(_bakeMeshes[i], true);
            _combine[count++] = new CombineInstance { mesh = _bakeMeshes[i], transform = skin.transform.localToWorldMatrix };
        }
        if (count == 0) return;

        var combine = _combine;
        if (count < _combine.Length) // บางชิ้นถูกซ่อนอยู่ (กรณีไม่ปกติ ยอม alloc)
        {
            combine = new CombineInstance[count];
            System.Array.Copy(_combine, combine, count);
        }

        var ghost = GetGhost();
        ghost.mesh.Clear();
        ghost.mesh.CombineMeshes(combine, true, true);
        ghost.age = 0f;
        SetGhostColor(ghost, 0f);
        ghost.obj.SetActive(true);
    }

    private Ghost GetGhost()
    {
        foreach (var g in _ghosts)
            if (!g.obj.activeSelf) return g;

        // ไม่ผูกกับตัว player -> เงาค้างอยู่ที่เดิมในโลกตอนตัวพุ่งออกไป (สร้างตอนใช้ครั้งแรก ชื่อ player ตั้งเสร็จแล้ว)
        // อยู่ scene เดียวกับ player -> ถูกทำลาย/ย้ายไปพร้อม player ไม่ค้างใน scene อื่น
        if (_container == null)
        {
            var containerObj = new GameObject($"DashAfterimages ({transform.root.name})");
            SceneManager.MoveGameObjectToScene(containerObj, gameObject.scene);
            _container = containerObj.transform;
        }

        // pool ไม่พอ -> สร้างเพิ่ม (โดยปกติจะมีแค่ ~lifetime/spawnInterval อัน)
        var obj = new GameObject("Afterimage");
        obj.layer = gameObject.layer;
        obj.transform.SetParent(_container, false);
        var mesh = new Mesh { name = "Afterimage", indexFormat = IndexFormat.UInt32 };
        obj.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = obj.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        var ghost = new Ghost { obj = obj, mesh = mesh, renderer = renderer };
        _ghosts.Add(ghost);
        return ghost;
    }
}

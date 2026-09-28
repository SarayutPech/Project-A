using System.Collections.Generic;
using UnityEngine;

// ทำ static batching ให้แผนที่ที่ generate ตอน runtime (presentation อย่างเดียว server headless ไม่ต้องมี)
//
// ทำไมต้องมี: ProceduralMapGenerator ตั้ง isStatic ให้แล้ว แต่ Unity ทำ static batching ให้เฉพาะ object ที่อยู่ใน scene ตอน build
// แผนที่เรา generate ใหม่ทุกครั้งตอน Awake flag static จึงไม่มีผลตอนเล่นจริง -> ต้องเรียก StaticBatchingUtility.Combine เอง
// รอให้ MapBuildAnimator เล่นจบก่อน (combine แล้วขยับ transform ไม่ได้ ภาพจะค้างที่ตำแหน่งตอน combine)
//
// ข้าม: warp portal / marker (อาจมี animation) และ mesh ที่ไม่ได้เปิด Read/Write (combine ตอน runtime ไม่ได้)
// mesh รวมที่ได้ถูกลบเองตอน regenerate / object นี้ถูกทำลาย (กฎข้อ 10)
public class MapStaticBatcher : MonoBehaviour
{
    [Tooltip("ถ้าเว้นว่างจะหาในฉากเดียวกัน")]
    public ProceduralMapGenerator generator;
    [Tooltip("ถ้าเว้นว่างจะหาในฉากเดียวกัน ถ้าไม่มีจะ combine ทันทีหลัง generate")]
    public MapBuildAnimator buildAnimator;
    public bool batchGround = true;
    public bool batchUnderside = true;
    public bool batchWater = true;
    public bool batchScatter = true;

    private readonly List<Mesh> _combinedMeshes = new List<Mesh>();
    private ProceduralMapGenerator _gen;
    private MapBuildAnimator _anim;
    private bool _waitingForBuild;

    private void Start()
    {
        if (!Application.isPlaying) return;
        _gen = generator != null ? generator : FindInScene<ProceduralMapGenerator>();
        _anim = buildAnimator != null ? buildAnimator : FindInScene<MapBuildAnimator>();
        if (_gen == null) return;

        _gen.MapGenerated += OnMapGenerated;
        if (_anim != null) _anim.onBuildFinished.AddListener(OnBuildFinished);

        // generator สร้างแผนที่ไปแล้วตั้งแต่ Awake
        bool animating = _anim != null && _anim.isActiveAndEnabled && (_anim.IsPlaying || _anim.playOnStart);
        if (animating) _waitingForBuild = true;
        else Combine();
    }

    private void OnDestroy()
    {
        if (_gen != null) _gen.MapGenerated -= OnMapGenerated;
        if (_anim != null) _anim.onBuildFinished.RemoveListener(OnBuildFinished);
        ReleaseCombinedMeshes();
    }

    private void OnMapGenerated()
    {
        // object เก่าถูกลบไปแล้ว -> mesh รวมชุดเก่าไม่มีใครใช้
        ReleaseCombinedMeshes();
        bool animating = _anim != null && _anim.isActiveAndEnabled && _anim.replayOnRegenerate;
        if (animating) _waitingForBuild = true;
        else Combine();
    }

    private void OnBuildFinished()
    {
        if (!_waitingForBuild) return;
        _waitingForBuild = false;
        Combine();
    }

    [ContextMenu("Combine Now")]
    public void Combine()
    {
        if (_gen == null || ProceduralMapGenerator.GeneratedRoot == null) return;

        var roots = new List<GameObject>();
        if (batchGround) { roots.Add(_gen.GroundObject); roots.Add(_gen.PitObject); }
        if (batchUnderside) roots.Add(_gen.UndersideObject);
        if (batchWater) roots.Add(_gen.WaterObject);
        if (batchScatter && _gen.ScatterContainer != null) roots.Add(_gen.ScatterContainer.gameObject);

        var targets = new List<GameObject>();
        var originalMeshes = new HashSet<Mesh>();
        foreach (var root in roots)
        {
            if (root == null) continue;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
            {
                Mesh mesh = filter.sharedMesh;
                var rend = filter.GetComponent<MeshRenderer>();
                if (mesh == null || !mesh.isReadable || rend == null || !rend.enabled) continue;
                targets.Add(filter.gameObject);
                originalMeshes.Add(mesh);
            }
        }
        if (targets.Count == 0) return;

        StaticBatchingUtility.Combine(targets.ToArray(), ProceduralMapGenerator.GeneratedRoot);

        // mesh ใหม่ที่ MeshFilter ชี้หลัง combine = mesh รวมที่ Unity สร้าง ต้องลบเอง
        foreach (var go in targets)
        {
            var mesh = go.GetComponent<MeshFilter>().sharedMesh;
            if (mesh != null && !originalMeshes.Contains(mesh) && !_combinedMeshes.Contains(mesh)) _combinedMeshes.Add(mesh);
        }
    }

    private void ReleaseCombinedMeshes()
    {
        foreach (var mesh in _combinedMeshes)
            if (mesh != null) Destroy(mesh);
        _combinedMeshes.Clear();
    }

    private T FindInScene<T>() where T : Object
    {
        foreach (var obj in FindObjectsByType<T>())
            if (obj is Component c && c.gameObject.scene == gameObject.scene) return obj;
        return null;
    }
}

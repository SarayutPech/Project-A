using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

// แอนิเมชันตอนเข้า scene: ยกแต่ละ layer ของแผนที่ที่ ProceduralMapGenerator สร้าง ขึ้นมาจากข้างล่างทีละ layer
// ตามลำดับ/ความเร็วใน MapBuildAnimationConfig (ทำงานเฉพาะตอน Play ไม่ยุ่งกับแผนที่ใน Editor)
//
// หมายเหตุ: ถ้าเปิด Combine Scatter Meshes ใน generator ตัว object จะถูกรวมเป็น batch ก้อนใหญ่
// แอนิเมชันจะขึ้นทีละ batch แทนทีละตัว
public class MapBuildAnimator : MonoBehaviour
{
    [Tooltip("ถ้าเว้นว่างจะใช้ ProceduralMapGenerator.Instance")]
    public ProceduralMapGenerator generator;
    [Tooltip("ถ้าเว้นว่างจะใช้ค่าเริ่มต้น (สร้าง asset ได้จาก Create > Procedural Map > Build Animation Config)")]
    public MapBuildAnimationConfig config;

    public bool playOnStart = true;
    [Tooltip("เล่นใหม่ทุกครั้งที่แผนที่ถูก generate ใหม่ระหว่าง Play (เช่นปรับค่า generator ใน Inspector)")]
    public bool replayOnRegenerate = true;

    [Tooltip("เรียกเมื่อทุก layer ขึ้นครบ (หรือกด Skip) ใช้เปิด player / UI ต่อได้")]
    public UnityEvent onBuildFinished;

    public bool IsPlaying => _routine != null;

    private class Item
    {
        public Transform transform;
        public Vector3 position;
        public Vector3 scale;
    }

    private class StepPlan
    {
        public MapBuildStep step;
        public List<Item> items;
        public float startTime;
    }

    private readonly List<StepPlan> _plan = new List<StepPlan>();
    private Coroutine _routine;
    private ProceduralMapGenerator _subscribed;
    private MapBuildAnimationConfig _defaultConfig;

    private ProceduralMapGenerator Generator => generator != null ? generator : ProceduralMapGenerator.Instance;

    private MapBuildAnimationConfig Config
    {
        get
        {
            if (config != null) return config;
            if (_defaultConfig == null) _defaultConfig = ScriptableObject.CreateInstance<MapBuildAnimationConfig>();
            return _defaultConfig;
        }
    }

    private void OnEnable()
    {
        _subscribed = Generator;
        if (_subscribed != null) _subscribed.MapGenerated += OnMapGenerated;
    }

    private void OnDisable()
    {
        if (_subscribed != null) _subscribed.MapGenerated -= OnMapGenerated;
        _subscribed = null;

        // กันแผนที่ค้างซ่อนอยู่ครึ่งทางถ้า animator ถูกปิดระหว่างเล่น
        if (IsPlaying) Skip();
    }

    private void Start()
    {
        // generator สร้างแผนที่เสร็จตั้งแต่ Awake แล้ว (DefaultExecutionOrder -1000) จึงเริ่มได้เลย ก่อนเฟรมแรกถูก render
        if (playOnStart) Play();
    }

    private void OnMapGenerated()
    {
        if (Application.isPlaying && replayOnRegenerate && isActiveAndEnabled) Play();
    }

    [ContextMenu("Play")]
    public void Play()
    {
        if (!Application.isPlaying) return;

        var gen = Generator;
        if (gen == null || ProceduralMapGenerator.GeneratedRoot == null) return;

        if (_routine != null) StopCoroutine(_routine);
        _routine = null;

        // รอบเก่ายังค้างกลางทาง -> วางเข้าที่ก่อน ไม่งั้นรอบใหม่จะจำตำแหน่งกลางอากาศเป็นเป้าหมาย
        foreach (var plan in _plan)
            foreach (var item in plan.items)
                SetFinal(item);

        BuildPlan(gen);
        _routine = StartCoroutine(Run());
    }

    // ข้ามแอนิเมชัน วางทุกชิ้นเข้าที่ทันที
    [ContextMenu("Skip")]
    public void Skip()
    {
        if (_routine != null) StopCoroutine(_routine);
        _routine = null;

        foreach (var plan in _plan)
            foreach (var item in plan.items)
                SetFinal(item);

        _plan.Clear();
        onBuildFinished?.Invoke();
    }

    private void BuildPlan(ProceduralMapGenerator gen)
    {
        _plan.Clear();
        var cfg = Config;

        float prevStart = cfg.startDelay;
        float prevEnd = cfg.startDelay;

        foreach (var step in cfg.steps)
        {
            if (step == null) continue;

            List<Item> items = CollectItems(gen, step);
            if (items.Count == 0) continue; // layer ที่ไม่ได้สร้าง (เช่นไม่มีหลุม) ไม่ต้องกินเวลา

            float start = (step.waitForPrevious ? prevEnd : prevStart) + step.delay;
            float length = step.duration + step.itemInterval * (items.Count - 1);
            prevStart = start;
            prevEnd = start + length;

            // ซ่อนไว้ที่ตำแหน่งเริ่ม (ต่ำลงไป) จนกว่าจะถึงคิวของชิ้นนั้น
            foreach (var item in items)
            {
                Apply(item, step, 0f);
                item.transform.gameObject.SetActive(false);
            }

            _plan.Add(new StepPlan { step = step, items = items, startTime = start });
        }
    }

    private IEnumerator Run()
    {
        float elapsed = 0f;
        while (true)
        {
            bool allDone = true;
            foreach (var plan in _plan)
                if (!UpdateStep(plan, elapsed - plan.startTime)) allDone = false;

            if (allDone) break;

            yield return null;
            elapsed += Time.deltaTime * Mathf.Max(0.01f, Config.speed);
        }

        _routine = null;
        _plan.Clear();
        onBuildFinished?.Invoke();
    }

    // คืน true เมื่อทุกชิ้นใน step นี้เข้าที่แล้ว
    private static bool UpdateStep(StepPlan plan, float localTime)
    {
        MapBuildStep step = plan.step;
        bool done = true;

        for (int i = 0; i < plan.items.Count; i++)
        {
            Item item = plan.items[i];
            if (item.transform == null) continue; // ถูกลบไปแล้ว (เช่น regenerate ระหว่างเล่น)

            float t = localTime - i * step.itemInterval;
            if (t < 0f)
            {
                done = false;
                continue;
            }

            if (!item.transform.gameObject.activeSelf) item.transform.gameObject.SetActive(true);

            float p = Mathf.Clamp01(t / step.duration);
            if (p >= 1f) SetFinal(item);
            else
            {
                Apply(item, step, p);
                done = false;
            }
        }

        return done;
    }

    private static void Apply(Item item, MapBuildStep step, float p)
    {
        float e = step.curve != null && step.curve.length > 0 ? step.curve.Evaluate(p) : p;
        item.transform.localPosition = item.position + Vector3.down * (step.riseDistance * (1f - e));
        item.transform.localScale = step.scaleIn ? item.scale * Mathf.Max(0f, e) : item.scale;
    }

    private static void SetFinal(Item item)
    {
        if (item.transform == null) return;
        item.transform.localPosition = item.position;
        item.transform.localScale = item.scale;
        if (!item.transform.gameObject.activeSelf) item.transform.gameObject.SetActive(true);
    }

    private List<Item> CollectItems(ProceduralMapGenerator gen, MapBuildStep step)
    {
        var transforms = new List<Transform>();

        switch (step.layer)
        {
            case MapLayer.Ground: AddIfExists(transforms, gen.GroundObject); break;
            case MapLayer.Underside: AddIfExists(transforms, gen.UndersideObject); break;
            case MapLayer.Pits: AddIfExists(transforms, gen.PitObject); break;
            case MapLayer.Water: AddIfExists(transforms, gen.WaterObject); break;
            case MapLayer.Markers:
                AddIfExists(transforms, gen.StartMarker);
                AddIfExists(transforms, gen.EndMarker);
                AddIfExists(transforms, gen.WarpPortal);
                break;
            case MapLayer.Objects:
                if (gen.ScatterContainer != null)
                    foreach (Transform child in gen.ScatterContainer) transforms.Add(child);
                break;
        }

        SortItems(transforms, step.itemOrder, gen.StartPosition);

        var items = new List<Item>(transforms.Count);
        foreach (var t in transforms)
            items.Add(new Item { transform = t, position = t.localPosition, scale = t.localScale });
        return items;
    }

    private static void AddIfExists(List<Transform> list, GameObject go)
    {
        if (go != null) list.Add(go.transform);
    }

    private static void SortItems(List<Transform> list, MapBuildItemOrder order, Vector3 startPosition)
    {
        if (list.Count < 2) return;

        switch (order)
        {
            case MapBuildItemOrder.CenterOutward:
            {
                Vector3 center = Vector3.zero;
                foreach (var t in list) center += t.position;
                center /= list.Count;
                list.Sort((a, b) => FlatSqrDistance(a.position, center).CompareTo(FlatSqrDistance(b.position, center)));
                break;
            }
            case MapBuildItemOrder.FromStart:
                list.Sort((a, b) => FlatSqrDistance(a.position, startPosition).CompareTo(FlatSqrDistance(b.position, startPosition)));
                break;
            case MapBuildItemOrder.Random:
                for (int i = list.Count - 1; i > 0; i--)
                {
                    int j = Random.Range(0, i + 1);
                    (list[i], list[j]) = (list[j], list[i]);
                }
                break;
        }
    }

    private static float FlatSqrDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x, dz = a.z - b.z;
        return dx * dx + dz * dz;
    }
}

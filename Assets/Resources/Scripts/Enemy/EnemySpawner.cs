using System.Collections.Generic;
using UnityEngine;

// วางศัตรูเป็นกลุ่ม (pack) บนแผนที่ procedural ทุกครั้งที่ generate (ฝั่ง server เท่านั้น ภายหลังเปลี่ยน Instantiate เป็น NetworkObject.Spawn)
// - สุ่มจุดกลาง pack จาก node ของ MapNavGraph ห่างจุด Start และห่างกันเองตามที่ตั้ง
// - สมาชิกกระจายรอบจุดกลาง บนชั้นเดียวกัน (ไม่ข้ามหน้าผา) เดินเล่นรอบจุดกลางร่วมกัน ตัวหนึ่งเจอ player เพื่อนมารุม
// - บาง pack มีตัว elite (ตัวใหญ่ + เรืองแสง + HP/ดาเมจเพิ่ม)
// ทั้งหมดสุ่มด้วย System.Random(seed ของแผนที่) -> seed เดิมได้ pack/ตำแหน่ง/elite เดิม (กฎข้อ 5)
// ถ้ามี MapBuildAnimator จะรอให้แผนที่ขึ้นครบก่อน (พื้นยังเลื่อนขึ้นอยู่ ศัตรูจะร่วง) / regenerate = ลบชุดเก่าแล้วเกิดใหม่
public class EnemySpawner : MonoBehaviour
{
    [Tooltip("ถ้าเว้นว่างจะหาในฉากเดียวกัน")]
    public ProceduralMapGenerator generator;
    [Tooltip("ถ้าเว้นว่างจะหาในฉากเดียวกัน")]
    public MapBuildAnimator buildAnimator;
    public GameObject enemyPrefab;

    [Header("Packs")]
    [Min(0)] public int packCount = 6;
    [Tooltip("จำนวนตัวต่อ pack (min, max) ตั้งเท่ากันถ้าอยากได้จำนวนคงที่")]
    public Vector2Int packSizeRange = new Vector2Int(3, 5);
    [Tooltip("สมาชิกกระจายรอบจุดกลาง pack ไม่เกินรัศมีนี้")]
    [Min(0.5f)] public float packRadius = 4f;
    [Tooltip("สมาชิกใน pack ห่างกันอย่างน้อยเท่านี้")]
    [Min(0f)] public float memberSpacing = 1.4f;
    [Tooltip("จุดกลางแต่ละ pack ห่างกันอย่างน้อยเท่านี้")]
    [Min(0f)] public float minPackSpacing = 18f;
    [Tooltip("ห้ามเกิดใกล้จุด Start ของ player เกินนี้")]
    [Min(0f)] public float minDistanceFromStart = 18f;
    [Tooltip("สมาชิก pack เดินเล่นห่างจุดกลาง pack ได้ไม่เกินนี้")]
    [Min(0f)] public float packWanderRadius = 6f;

    [Header("Elite")]
    [Tooltip("โอกาสที่ pack หนึ่งจะมีตัว elite")]
    [Range(0f, 1f)] public float eliteChance = 0.5f;
    [Tooltip("จำนวน elite ใน pack ที่มี elite (min, max) ไม่เกินขนาด pack")]
    public Vector2Int eliteCountRange = new Vector2Int(1, 2);
    public EliteSettings elite = EliteSettings.Default;

    [Header("Look")]
    [Tooltip("สุ่มสีให้แต่ละ pack (ทั้ง pack สีเดียวกัน) ว่าง = ใช้สีใน prefab")]
    public Color[] packColors = new Color[0];

    private readonly List<GameObject> _spawned = new List<GameObject>();
    private ProceduralMapGenerator _gen;
    private MapBuildAnimator _anim;
    private bool _waitingForBuild;

    public int SpawnedCount => _spawned.Count;

    private void Start()
    {
        _gen = generator != null ? generator : FindInScene<ProceduralMapGenerator>();
        _anim = buildAnimator != null ? buildAnimator : FindInScene<MapBuildAnimator>();
        if (_gen == null)
        {
            Debug.LogWarning($"[{nameof(EnemySpawner)}] ไม่พบ ProceduralMapGenerator ในฉาก", this);
            return;
        }

        _gen.MapGenerated += OnMapGenerated;
        if (_anim != null) _anim.onBuildFinished.AddListener(OnBuildFinished);

        // generator สร้างแผนที่ไปแล้วตั้งแต่ Awake
        bool animating = _anim != null && _anim.isActiveAndEnabled && (_anim.IsPlaying || _anim.playOnStart);
        if (animating) _waitingForBuild = true;
        else SpawnAll();
    }

    private void OnDestroy()
    {
        if (_gen != null) _gen.MapGenerated -= OnMapGenerated;
        if (_anim != null) _anim.onBuildFinished.RemoveListener(OnBuildFinished);
    }

    private void OnMapGenerated()
    {
        DespawnAll();
        bool animating = _anim != null && _anim.isActiveAndEnabled && _anim.replayOnRegenerate;
        if (animating) _waitingForBuild = true;
        else SpawnAll();
    }

    private void OnBuildFinished()
    {
        if (!_waitingForBuild) return;
        _waitingForBuild = false;
        SpawnAll();
    }

    [ContextMenu("Respawn")]
    public void SpawnAll()
    {
        DespawnAll();
        if (!Application.isPlaying || enemyPrefab == null || _gen == null) return;

        MapNavGraph graph = _gen.NavGraph;
        if (graph == null || graph.NodeCount == 0)
        {
            Debug.LogWarning($"[{nameof(EnemySpawner)}] แผนที่ไม่มี NavGraph (เปิด Build Nav Graph ใน config)", this);
            return;
        }

        var rng = new System.Random(_gen.seed ^ 0x3E11A5);
        var centers = PickPackCenters(graph, rng);
        for (int i = 0; i < centers.Count; i++) SpawnPack(i, centers[i], graph, rng);
    }

    private List<Vector3> PickPackCenters(MapNavGraph graph, System.Random rng)
    {
        var centers = new List<Vector3>();
        Vector3 start = _gen.StartPosition;
        int attempts = packCount * 40;
        for (int i = 0; i < attempts && centers.Count < packCount; i++)
        {
            MapNavGraph.Node node = graph.GetNode(rng.Next(graph.NodeCount));
            if (node.water) continue;
            if (FlatDistance(node.position, start) < minDistanceFromStart) continue;
            if (centers.Exists(c => FlatDistance(c, node.position) < minPackSpacing)) continue;
            centers.Add(node.position);
        }
        return centers;
    }

    private void SpawnPack(int id, Vector3 center, MapNavGraph graph, System.Random rng)
    {
        int size = RandomRange(rng, packSizeRange.x, packSizeRange.y);
        int elites = 0;
        if (rng.NextDouble() < eliteChance) elites = Mathf.Min(size, RandomRange(rng, eliteCountRange.x, eliteCountRange.y));
        Color? color = packColors != null && packColors.Length > 0 ? packColors[rng.Next(packColors.Length)] : (Color?)null;

        // ตำแหน่งสมาชิก: node รอบจุดกลาง ชั้นเดียวกัน ไม่อยู่ในน้ำ ไม่ชิดกันเกิน
        var positions = new List<Vector3> { center };
        for (int attempt = 0; attempt < size * 20 && positions.Count < size; attempt++)
        {
            int n = graph.RandomNodeNear(center, packRadius, rng);
            if (n < 0) continue;
            MapNavGraph.Node node = graph.GetNode(n);
            if (node.water || Mathf.Abs(node.position.y - center.y) > 0.5f) continue;
            if (positions.Exists(p => FlatDistance(p, node.position) < memberSpacing)) continue;
            positions.Add(node.position);
        }

        var pack = new EnemyPack(id, center);
        for (int i = 0; i < positions.Count; i++)
            SpawnMember(positions[i], graph, rng, pack, i < elites, color);
    }

    private void SpawnMember(Vector3 footPosition, MapNavGraph graph, System.Random rng, EnemyPack pack, bool isElite, Color? color)
    {
        float yaw = (float)(rng.NextDouble() * 360.0);
        int aiSeed = rng.Next();

        GameObject enemy = Instantiate(enemyPrefab, footPosition, Quaternion.Euler(0f, yaw, 0f));
        enemy.name = $"{enemyPrefab.name} P{pack.Id + 1}-{pack.Members.Count + 1}{(isElite ? " (Elite)" : "")}";
        _spawned.Add(enemy);

        // แยกศัตรูเข้า scene ของแผนที่ (ไม่ใช่ scene ของ player ที่อยู่ข้าม scene) -> เปลี่ยน map แล้วหายไปด้วย
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(enemy, gameObject.scene);

        if (isElite)
        {
            var rank = enemy.GetComponent<EnemyRank>();
            if (rank == null) rank = enemy.AddComponent<EnemyRank>();
            rank.MakeElite(elite);
        }

        // teleport หลังปรับขนาด (elite ตัวใหญ่ เท้าต้องอยู่บนพื้นพอดี)
        var motor = enemy.GetComponent<EnemyMotor>();
        if (motor != null) motor.Teleport(footPosition);
        var ai = enemy.GetComponent<EnemyAI>();
        if (ai != null)
        {
            ai.wanderRadius = packWanderRadius;
            ai.Initialize(graph, aiSeed, pack);
        }

        if (color.HasValue)
        {
            var look = enemy.GetComponentInChildren<CharacterAppearance>();
            if (look != null) look.SetBaseColor(color.Value);
        }
    }

    private void DespawnAll()
    {
        foreach (var e in _spawned)
            if (e != null) Destroy(e);
        _spawned.Clear();
    }

    // สุ่มจำนวนเต็มในช่วง [min, max] รวมปลาย (สลับให้ถ้าใส่กลับด้าน)
    private static int RandomRange(System.Random rng, int min, int max)
    {
        if (max < min) (min, max) = (max, min);
        return rng.Next(Mathf.Max(0, min), Mathf.Max(0, max) + 1);
    }

    private static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

    private T FindInScene<T>() where T : Component
    {
        foreach (var obj in FindObjectsByType<T>())
            if (obj.gameObject.scene == gameObject.scene) return obj;
        return null;
    }
}

using UnityEngine;
using System.Collections.Generic;

[DefaultExecutionOrder(-1000)]
[ExecuteAlways]
public partial class ProceduralMapGenerator : MonoBehaviour
{
    [Tooltip("ค่าตั้งของแผนที่ (สร้างจาก Create > Procedural Map > Map Config) ถ้าเว้นว่างจะใช้ค่าเริ่มต้น")]
    public ProceduralMapConfig config;

    private float stepSize => Config.stepSize;
    private int fillRadius => Config.fillRadius;
    private float edgeFillChance => Config.edgeFillChance;
    private Material groundMaterial => Config.groundMaterial;
    private float textureWorldSize => Config.textureWorldSize;
    private bool addCollider => Config.addCollider;
    private int walkSteps => Config.walkSteps;
    private bool moveTransformToEnd => Config.moveTransformToEnd;
    private bool showStartEndMarkers => Config.showStartEndMarkers;
    private GameObject startMarkerPrefab => Config.startMarkerPrefab;
    private GameObject endMarkerPrefab => Config.endMarkerPrefab;
    private float markerSize => Config.markerSize;
    private GameObject warpPortalPrefab => Config.warpPortalPrefab;
    private Vector3 warpPortalOffset => Config.warpPortalOffset;
    private bool scatterObjects => Config.scatterObjects;
    private float scatterPositionJitter => Config.scatterPositionJitter;
    private List<ScatterCategory> scatterCategories => Config.scatterCategories;
    private bool smoothEdges => Config.smoothEdges;
    private int smoothIterations => Config.smoothIterations;
    private int smoothThreshold => Config.smoothThreshold;
    private bool connectIslands => Config.connectIslands;
    private bool fillEnclosedHoles => Config.fillEnclosedHoles;
    private bool terraces => Config.terraces;
    private Material cliffMaterial => Config.cliffMaterial;
    private bool addNoiseHoles => Config.addNoiseHoles;
    private int holeMeshResolution => Config.holeMeshResolution;
    private float holeDensity => Config.holeDensity;
    private Vector2 holeRadiusRange => Config.holeRadiusRange;
    private float holeEdgeJitter => Config.holeEdgeJitter;
    private bool holesAsPonds => Config.holesAsPonds;
    private float pitDepth => Config.pitDepth;
    private float waterSurfaceDepth => Config.waterSurfaceDepth;
    private Material waterMaterial => Config.waterMaterial;
    private string waterLayerName => Config.waterLayerName;
    private bool walkableWater => Config.walkableWater;
    private float waterWalkDepth => Config.waterWalkDepth;
    private float waterWalkRampWidth => Config.waterWalkRampWidth;
    private float holeClearanceAroundStartEnd => Config.holeClearanceAroundStartEnd;
    private bool blockWalkingIntoHoles => Config.blockWalkingIntoHoles;
    private float holeBlockerHeight => Config.holeBlockerHeight;
    private float holeBlockerThickness => Config.holeBlockerThickness;
    private bool buildUnderside => Config.buildUnderside;
    private UndersideType undersideType => Config.undersideType;
    private Material undersideMaterial => Config.undersideMaterial;
    private float undersideMaxDepth => Config.undersideMaxDepth;
    private float undersideEdgeLip => Config.undersideEdgeLip;
    private float undersideTaperDistance => Config.undersideTaperDistance;
    private float undersideNoise => Config.undersideNoise;
    private float undersideNoiseScale => Config.undersideNoiseScale;
    private int undersideResolution => Config.undersideResolution;
    private bool undersideCollider => Config.undersideCollider;
    private bool buildBoundaryWalls => Config.buildBoundaryWalls;
    private float boundaryWallHeight => Config.boundaryWallHeight;
    private float boundaryWallThickness => Config.boundaryWallThickness;
    private bool markGeneratedStatic => Config.markGeneratedStatic;
    private bool combineScatterMeshes => Config.combineScatterMeshes;
    private bool disableCollidersAfterCombine => Config.disableCollidersAfterCombine;

    private ProceduralMapConfig _defaultConfig;

    // ค่าทั้งหมดอ่านผ่าน property ด้านบนที่ชื่อเดียวกับฟิลด์ใน config (โค้ดสร้าง map ข้างล่างจึงไม่ต้องแก้)
    public ProceduralMapConfig Config
    {
        get
        {
            if (config != null) return config;
            if (_defaultConfig == null)
            {
                _defaultConfig = ScriptableObject.CreateInstance<ProceduralMapConfig>();
                _defaultConfig.hideFlags = HideFlags.DontSave;
            }
            return _defaultConfig;
        }
    }

    [Header("Randomization")]
    [Tooltip("ปิด = สุ่ม seed ใหม่ทุกครั้งที่ generate (ทุกครั้งที่เข้า scene) แล้วเขียนค่าลงช่อง Seed ให้ copy ไปใช้ซ้ำได้")]
    public bool useFixedSeed = false;
    public int seed = 12345;

    private static readonly Vector2Int[] Directions8 =
    {
        new Vector2Int(1, 0), new Vector2Int(-1, 0),
        new Vector2Int(0, 1), new Vector2Int(0, -1),
        new Vector2Int(1, 1), new Vector2Int(1, -1),
        new Vector2Int(-1, 1), new Vector2Int(-1, -1),
    };

    private static readonly Vector2Int[] Directions4 =
    {
        new Vector2Int(1, 0), new Vector2Int(-1, 0),
        new Vector2Int(0, 1), new Vector2Int(0, -1),
    };

    private struct HoleCircle
    {
        public Vector2 center;
        public float radius;
        public float jitterSeed;
    }

    private readonly HashSet<Vector2Int> _cells = new HashSet<Vector2Int>();
    private readonly List<HoleCircle> _holes = new List<HoleCircle>();
    private readonly HashSet<Vector2Int> _pathVisited = new HashSet<Vector2Int>();
    private readonly List<Vector2Int> _pathStack = new List<Vector2Int>();
    private readonly HashSet<Vector2Int> _holeSubCells = new HashSet<Vector2Int>(); // sub-cell ที่เป็นรู/บ่อ รอบล่าสุด
    private TerraceLayout _terrace;
    private const string RootName = "Generated Map";

    // ความสูงชั้น/ทางลาดของรอบล่าสุด (พื้นเรียบถ้าปิด terraces)
    public TerraceLayout Terrace => _terrace ?? (_terrace = TerraceLayout.Flat(stepSize));

    private float PitFloorY => -pitDepth;
    private float WaterY => -Mathf.Min(waterSurfaceDepth, pitDepth * 0.9f);
    private Material _fallbackWaterMaterial;
    private Material _fallbackUndersideMaterial;

    // Singleton: script นี้มีได้ตัวเดียวในฉาก
    public static ProceduralMapGenerator Instance { get; private set; }

    // Singleton: group รวมทุก object ที่สร้าง (ground, markers, scatter) มีได้ group เดียวเสมอ
    public static GameObject GeneratedRoot { get; private set; }

    // อ้างอิงแต่ละ layer ที่สร้างรอบล่าสุด (null ถ้า layer นั้นไม่ได้สร้าง) ให้ script อื่นเช่น MapBuildAnimator ใช้
    public GameObject GroundObject { get; private set; }
    public GameObject PitObject { get; private set; }
    public GameObject WaterObject { get; private set; }
    public GameObject UndersideObject { get; private set; }
    public GameObject BoundaryObject { get; private set; }
    public GameObject HoleBlockerObject { get; private set; }
    public GameObject StartMarker { get; private set; }
    public GameObject EndMarker { get; private set; }
    public GameObject WarpPortal { get; private set; }
    public Transform ScatterContainer { get; private set; }
    public Vector3 StartPosition { get; private set; }
    public Vector3 EndPosition { get; private set; }
    // ขอบเขตผิวพื้นทั้งแผนที่ (y = 0) ไว้ให้กล้องหาจุดกลาง/ขนาด map
    public Bounds MapBounds { get; private set; }
    // กราฟเดินของ AI รอบล่าสุด (null ถ้าปิด buildNavGraph) อ้างผ่าน generator ของแผนที่นั้น ไม่ใช่ static (กฎข้อ 6)
    public MapNavGraph NavGraph { get; private set; }

    // เรียกหลัง generate เสร็จทุกครั้ง (ทั้งใน Editor และตอน Play)
    public event System.Action MapGenerated;

    private void Awake()
    {
        if (!RegisterInstance()) return;
        GenerateMap();
    }

    private void OnEnable()
    {
        RegisterInstance();
#if UNITY_EDITOR
        ProceduralMapConfig.Changed += OnConfigChanged;
#endif
    }

    private void OnDisable()
    {
        if (Instance == this) Instance = null;
#if UNITY_EDITOR
        ProceduralMapConfig.Changed -= OnConfigChanged;
#endif
    }

    private void OnDestroy()
    {
        // config ค่าเริ่มต้นสร้างตอน runtime ไม่ใช่ asset ต้องลบเองไม่งั้นค้างใน memory
        if (_defaultConfig == null) return;
        if (Application.isPlaying) Destroy(_defaultConfig);
        else DestroyImmediate(_defaultConfig);
    }

    private bool RegisterInstance()
    {
        if (Instance == null || Instance == this)
        {
            Instance = this;
            return true;
        }

        Debug.LogWarning($"[{nameof(ProceduralMapGenerator)}] มีตัวอยู่แล้วที่ '{Instance.name}' จึงปิด '{name}' (Singleton)", this);
        enabled = false;
        if (Application.isPlaying) Destroy(this);
        return false;
    }

    private void OnValidate()
    {
        // ให้เห็นผลทันทีตอนปรับค่าใน Inspector โดยไม่ต้องกด Play
        // (delayCall กันปัญหาเรียก AddComponent/DestroyImmediate ระหว่าง OnValidate ซึ่ง Unity ไม่อนุญาต)
#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this == null) return; // object อาจถูกลบไปแล้วก่อน delayCall ทำงาน
            if (!RegisterInstance()) return;
            GenerateMap();
        };
#endif
    }

#if UNITY_EDITOR
    // แก้ค่าใน config asset ที่ใช้อยู่ -> generate ใหม่เหมือนแก้ค่าบน component
    private void OnConfigChanged(ProceduralMapConfig changed)
    {
        if (changed == config) OnValidate();
    }
#endif

    [ContextMenu("Generate Now")]
    private void GenerateNow()
    {
        if (!RegisterInstance()) return;
        GenerateMap();
    }

    public void GenerateMap()
    {
        if (Instance != this) return;
        if (!useFixedSeed) seed = System.Environment.TickCount ^ System.Guid.NewGuid().GetHashCode();
        Random.InitState(seed);

        _cells.Clear();
        _pathVisited.Clear();
        _pathStack.Clear();

        Vector2Int startCell = WorldToCell(transform.position);
        Vector2Int cell = startCell;
        _pathVisited.Add(cell);
        _pathStack.Add(cell);
        MarkAround(cell);

        // self-avoiding walk: เดินเฉพาะ cell ที่ยังไม่เคยเดิน ถ้าตันหมดทุกทิศให้ backtrack กลับไปจุดก่อนหน้า
        int stepsWalked = 0;
        int safetyCounter = 0;
        int maxSafety = walkSteps * 20; // กันลูปค้างถ้า backtrack วนไม่จบ

        List<Vector2Int> unvisitedNeighbors = new List<Vector2Int>();

        while (stepsWalked < walkSteps && _pathStack.Count > 0 && safetyCounter < maxSafety)
        {
            safetyCounter++;
            unvisitedNeighbors.Clear();

            foreach (var d in Directions8)
            {
                Vector2Int n = cell + d;
                if (!_pathVisited.Contains(n)) unvisitedNeighbors.Add(n);
            }

            if (unvisitedNeighbors.Count > 0)
            {
                cell = unvisitedNeighbors[Random.Range(0, unvisitedNeighbors.Count)];
                _pathVisited.Add(cell);
                _pathStack.Add(cell);
                MarkAround(cell);
                stepsWalked++;
            }
            else
            {
                // ตันทุกทิศ -> ถอยกลับไป cell ก่อนหน้าแล้วลองหาทางใหม่จากตรงนั้น
                _pathStack.RemoveAt(_pathStack.Count - 1);
                if (_pathStack.Count > 0) cell = _pathStack[_pathStack.Count - 1];
            }
        }

        Vector2Int endCell = cell;

        if (smoothEdges) SmoothEdges(smoothIterations, smoothThreshold);
        if (connectIslands) ConnectIsolatedIslands();
        if (fillEnclosedHoles) FillEnclosedHoles();

        GenerateNoiseHoleSeeds(startCell, endCell);

        // ใช้ System.Random จาก seed แยกของตัวเอง ไม่แตะลำดับ UnityEngine.Random -> รูปร่าง/บ่อของ seed เดิมไม่เปลี่ยน
        // พื้นใต้บ่อแต่ละบ่อถูกบังคับให้อยู่ชั้นเดียวกัน (บ่อใหญ่แค่ไหนก็อยู่บนที่ราบเสมอ ไม่คร่อมหน้าผา)
        _terrace = terraces
            ? TerraceLayout.Build(_cells, startCell, endCell, new TerraceLayout.Settings
            {
                stepSize = stepSize,
                levels = Config.terraceLevels,
                levelHeight = Config.terraceHeight,
                regionSize = Config.terraceRegionSize,
                flatChance = Config.terraceFlatChance,
            }, seed, HoleFootprints())
            : TerraceLayout.Flat(stepSize);

        Transform root = ResetGeneratedRoot();

        GroundObject = PitObject = WaterObject = UndersideObject = BoundaryObject = HoleBlockerObject = StartMarker = EndMarker = WarpPortal = null;
        ScatterContainer = null;
        StartPosition = SurfacePoint(CellToWorld(startCell));
        EndPosition = SurfacePoint(CellToWorld(endCell));
        MapBounds = ComputeMapBounds();

        BuildGroundMesh(root);
        if (buildUnderside) BuildUnderside(root);
        if (buildBoundaryWalls)
            BoundaryObject = MapBoundaryBuilder.Build(_cells, stepSize, Terrace.MaxHeight + boundaryWallHeight, boundaryWallThickness, root);
        if (blockWalkingIntoHoles && !walkableWater) BuildHoleBlockers(root);

        if (showStartEndMarkers) PlaceMarkers(root, startCell, endCell);
        PlaceWarpPortal(root, startCell);
        ScatterObjects(root, startCell, endCell);
        NavGraph = Config.buildNavGraph ? BuildNavGraph() : null;

        // ย้าย transform เฉพาะตอนกด Play จริง กัน object กระโดดตำแหน่งตอน tune ค่าใน Editor
        if (moveTransformToEnd && Application.isPlaying)
        {
            Vector3 endPos = CellToWorld(endCell);
            endPos.y = transform.position.y;
            transform.position = endPos;
        }

        // ไม่เซฟแผนที่ที่ generate ใน Editor ลงไฟล์ scene (mesh ใหญ่มากจน .unity เกิน 100MB push GitHub ไม่ได้)
        // ไม่เสียอะไรเพราะ [ExecuteAlways] + Awake จะ generate ใหม่จาก config + seed ทุกครั้งที่เปิด scene / กด Play
        if (!Application.isPlaying) MarkDontSaveRecursive(root.gameObject);

        MapGenerated?.Invoke();
    }

    private static void MarkDontSaveRecursive(GameObject go)
    {
        go.hideFlags |= HideFlags.DontSaveInEditor;
        foreach (Transform child in go.transform) MarkDontSaveRecursive(child.gameObject);
    }

    private Bounds ComputeMapBounds()
    {
        if (_cells.Count == 0) return new Bounds(Vector3.zero, Vector3.zero);

        int minX = int.MaxValue, maxX = int.MinValue, minZ = int.MaxValue, maxZ = int.MinValue;
        foreach (var c in _cells)
        {
            minX = Mathf.Min(minX, c.x); maxX = Mathf.Max(maxX, c.x);
            minZ = Mathf.Min(minZ, c.y); maxZ = Mathf.Max(maxZ, c.y);
        }

        float half = stepSize * 0.5f;
        var bounds = new Bounds();
        bounds.SetMinMax(new Vector3(minX * stepSize - half, 0f, minZ * stepSize - half),
                         new Vector3(maxX * stepSize + half, 0f, maxZ * stepSize + half));
        return bounds;
    }

    // ---------- Generated Root (Singleton group) ----------

    // ลบ group เก่าทั้งหมด (รวมตัวที่ตกค้างหลัง domain reload) แล้วสร้าง group ใหม่เพียงอันเดียว
    private Transform ResetGeneratedRoot()
    {
        DestroyObject(GeneratedRoot);

        GameObject stray;
        for (int i = 0; i < 100 && (stray = GameObject.Find(RootName)) != null; i++)
            DestroyObject(stray);

        GeneratedRoot = new GameObject(RootName);
        GeneratedRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        return GeneratedRoot.transform;
    }

    private static void DestroyObject(GameObject go)
    {
        if (go == null) return;

        if (Application.isPlaying)
        {
            go.SetActive(false); // Destroy ทำงานท้ายเฟรม ปิดไว้ก่อนกัน GameObject.Find เจอซ้ำ
            Destroy(go);
        }
        else
        {
            DestroyImmediate(go);
        }
    }
}

using UnityEngine;
using System.Collections.Generic;

[DefaultExecutionOrder(-1000)]
[ExecuteAlways]
public class ProceduralMapGenerator : MonoBehaviour
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
    private const string RootName = "Generated Map";

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

        Transform root = ResetGeneratedRoot();

        GroundObject = PitObject = WaterObject = UndersideObject = BoundaryObject = HoleBlockerObject = StartMarker = EndMarker = WarpPortal = null;
        ScatterContainer = null;
        StartPosition = CellToWorld(startCell);
        EndPosition = CellToWorld(endCell);
        MapBounds = ComputeMapBounds();

        BuildGroundMesh(root);
        if (buildUnderside) BuildUnderside(root);
        if (buildBoundaryWalls)
            BoundaryObject = MapBoundaryBuilder.Build(_cells, stepSize, boundaryWallHeight, boundaryWallThickness, root);
        if (blockWalkingIntoHoles && !walkableWater) BuildHoleBlockers(root);

        if (showStartEndMarkers) PlaceMarkers(root, startCell, endCell);
        PlaceWarpPortal(root, startCell);
        ScatterObjects(root, startCell, endCell);

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

    // ---------- Phase 1: กำหนดรูปทรง ----------

    private void MarkAround(Vector2Int cell)
    {
        for (int dz = -fillRadius; dz <= fillRadius; dz++)
        {
            for (int dx = -fillRadius; dx <= fillRadius; dx++)
            {
                Vector2Int neighbor = new Vector2Int(cell.x + dx, cell.y + dz);
                if (_cells.Contains(neighbor)) continue;

                int chebyshev = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz));
                if (fillRadius > 0 && chebyshev == fillRadius && Random.value > edgeFillChance)
                    continue;

                _cells.Add(neighbor);
            }
        }
    }

    private void SmoothEdges(int iterations, int fillThreshold)
    {
        for (int iter = 0; iter < iterations; iter++)
        {
            HashSet<Vector2Int> candidates = new HashSet<Vector2Int>();
            foreach (var cell in _cells)
            {
                foreach (var d in Directions8)
                {
                    Vector2Int n = cell + d;
                    if (!_cells.Contains(n)) candidates.Add(n);
                }
            }

            List<Vector2Int> toFill = new List<Vector2Int>();
            foreach (var cand in candidates)
            {
                int count = 0;
                foreach (var d in Directions8)
                    if (_cells.Contains(cand + d)) count++;

                if (count >= fillThreshold) toFill.Add(cand);
            }

            foreach (var cell in toFill) _cells.Add(cell);
        }
    }

    private void ConnectIsolatedIslands()
    {
        List<List<Vector2Int>> islands = FindConnectedComponents();
        if (islands.Count <= 1) return;

        islands.Sort((a, b) => b.Count.CompareTo(a.Count));
        List<Vector2Int> main = new List<Vector2Int>(islands[0]);

        for (int i = 1; i < islands.Count; i++)
        {
            List<Vector2Int> island = islands[i];

            Vector2Int bestMain = main[0], bestIsland = island[0];
            int bestDist = int.MaxValue;

            foreach (var m in main)
                foreach (var isl in island)
                {
                    int d = Mathf.Abs(m.x - isl.x) + Mathf.Abs(m.y - isl.y);
                    if (d < bestDist) { bestDist = d; bestMain = m; bestIsland = isl; }
                }

            BridgeLine(bestMain, bestIsland);
            main.AddRange(island);
        }
    }

    private List<List<Vector2Int>> FindConnectedComponents()
    {
        var visited = new HashSet<Vector2Int>();
        var result = new List<List<Vector2Int>>();

        foreach (var start in _cells)
        {
            if (visited.Contains(start)) continue;

            var component = new List<Vector2Int>();
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(start);
            visited.Add(start);

            while (queue.Count > 0)
            {
                Vector2Int current = queue.Dequeue();
                component.Add(current);

                foreach (var d in Directions8)
                {
                    Vector2Int n = current + d;
                    if (_cells.Contains(n) && !visited.Contains(n))
                    {
                        visited.Add(n);
                        queue.Enqueue(n);
                    }
                }
            }

            result.Add(component);
        }

        return result;
    }

    private void BridgeLine(Vector2Int from, Vector2Int to)
    {
        int x0 = from.x, y0 = from.y;
        int x1 = to.x, y1 = to.y;

        int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;

        while (true)
        {
            _cells.Add(new Vector2Int(x0, y0));

            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    private void FillEnclosedHoles()
    {
        if (_cells.Count == 0) return;

        int minX = int.MaxValue, maxX = int.MinValue, minZ = int.MaxValue, maxZ = int.MinValue;
        foreach (var c in _cells)
        {
            minX = Mathf.Min(minX, c.x); maxX = Mathf.Max(maxX, c.x);
            minZ = Mathf.Min(minZ, c.y); maxZ = Mathf.Max(maxZ, c.y);
        }
        minX--; maxX++; minZ--; maxZ++;

        var reachable = new HashSet<Vector2Int>();
        var queue = new Queue<Vector2Int>();

        for (int x = minX; x <= maxX; x++)
        {
            TryEnqueueEmpty(new Vector2Int(x, minZ), reachable, queue);
            TryEnqueueEmpty(new Vector2Int(x, maxZ), reachable, queue);
        }
        for (int z = minZ; z <= maxZ; z++)
        {
            TryEnqueueEmpty(new Vector2Int(minX, z), reachable, queue);
            TryEnqueueEmpty(new Vector2Int(maxX, z), reachable, queue);
        }

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            foreach (var d in Directions4)
            {
                var n = cur + d;
                if (n.x < minX || n.x > maxX || n.y < minZ || n.y > maxZ) continue;
                TryEnqueueEmpty(n, reachable, queue);
            }
        }

        for (int x = minX; x <= maxX; x++)
            for (int z = minZ; z <= maxZ; z++)
            {
                var cell = new Vector2Int(x, z);
                if (_cells.Contains(cell)) continue;
                if (!reachable.Contains(cell)) _cells.Add(cell);
            }
    }

    private void TryEnqueueEmpty(Vector2Int cell, HashSet<Vector2Int> reachable, Queue<Vector2Int> queue)
    {
        if (_cells.Contains(cell) || reachable.Contains(cell)) return;
        reachable.Add(cell);
        queue.Enqueue(cell);
    }

    // ---------- Noise Holes: สุ่มตำแหน่ง+ขนาดรู ไม่ผูกกับ stepSize ----------
    private void GenerateNoiseHoleSeeds(Vector2Int startCell, Vector2Int endCell)
    {
        _holes.Clear();
        if (!addNoiseHoles) return;

        Vector3 startWorld = CellToWorld(startCell);
        Vector3 endWorld = CellToWorld(endCell);
        var start = new Vector2(startWorld.x, startWorld.z);
        var end = new Vector2(endWorld.x, endWorld.z);

        foreach (var cell in _cells)
        {
            if (Random.value > holeDensity) continue;

            Vector3 cellCenter = CellToWorld(cell);
            Vector2 jitteredCenter = new Vector2(
                cellCenter.x + Random.Range(-stepSize, stepSize) * 0.3f,
                cellCenter.z + Random.Range(-stepSize, stepSize) * 0.3f
            );

            var hole = new HoleCircle
            {
                center = jitteredCenter,
                radius = Random.Range(holeRadiusRange.x, holeRadiusRange.y),
                jitterSeed = Random.Range(0f, 1000f)
            };

            // สุ่มครบทุกค่าก่อนค่อยทิ้ง ลำดับ Random จะได้ไม่เพี้ยนต่อ seed เดิมเมื่อปรับ clearance
            if (HoleTooClose(hole, start) || HoleTooClose(hole, end)) continue;
            _holes.Add(hole);
        }
    }

    // ขอบรูที่บานสุด (รวม jitter) เข้ามาใกล้จุดนี้เกิน clearance
    private bool HoleTooClose(HoleCircle hole, Vector2 point)
    {
        float maxRadius = hole.radius * (1f + holeEdgeJitter);
        return Vector2.Distance(hole.center, point) < maxRadius + holeClearanceAroundStartEnd;
    }

    // ความสูงผิวที่ยืน/วางของได้ ณ ตำแหน่งนี้ (local ของ GeneratedRoot): พื้นปกติ = 0 / ในบ่อ = ผิวน้ำ / รูทะลุ = ก้นใต้พื้นไม่มี ใช้ 0
    public float GetSurfaceHeight(Vector3 worldPos)
    {
        float rootY = GeneratedRoot != null ? GeneratedRoot.transform.position.y : 0f;
        bool inPond = addNoiseHoles && holesAsPonds && pitDepth > 0f && IsInsideHole(worldPos);
        return rootY + (inPond ? WaterY : 0f);
    }

    // มุม + Perlin noise ตามมุม ทำให้ขอบรูเป็นก้อนเบี้ยวๆ แทนที่จะเป็นวงกลม/สี่เหลี่ยมเป๊ะ
    private bool IsInsideHole(Vector3 worldPos)
    {
        for (int i = 0; i < _holes.Count; i++)
        {
            HoleCircle hole = _holes[i];
            float dx = worldPos.x - hole.center.x;
            float dz = worldPos.z - hole.center.y;
            float dist = Mathf.Sqrt(dx * dx + dz * dz);

            if (dist > hole.radius * (1f + holeEdgeJitter)) continue;

            float angle = Mathf.Atan2(dz, dx);
            float edgeNoise = Mathf.PerlinNoise(Mathf.Cos(angle) * 2f + hole.jitterSeed, Mathf.Sin(angle) * 2f + hole.jitterSeed);
            float effectiveRadius = hole.radius * (1f + (edgeNoise - 0.5f) * holeEdgeJitter);

            if (dist <= effectiveRadius) return true;
        }
        return false;
    }

    // ---------- Phase 2: สร้าง mesh เดียวรวมทุก cell ----------

    private void BuildGroundMesh(Transform root)
    {
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        _holeSubCells.Clear();
        var pitVertices = new List<Vector3>();
        var pitUvs = new List<Vector2>();
        var pitTriangles = new List<int>();
        var waterVertices = new List<Vector3>();
        var waterUvs = new List<Vector2>();
        var waterTriangles = new List<int>();

        if (addNoiseHoles && holeMeshResolution > 1 && _holes.Count > 0)
            BuildSubdividedGeometry(vertices, uvs, triangles, pitVertices, pitUvs, pitTriangles, waterVertices, waterUvs, waterTriangles);
        else
            BuildSimpleGeometry(vertices, uvs, triangles);

        GroundObject = CreateGroundPart(root, "Generated Ground", vertices, uvs, triangles);

        // ก้นหลุม + ผนังแยกเป็นอีก object (material เดียวกับพื้น) จะได้ animate/ซ่อนแยกจากผิวพื้นได้
        if (pitVertices.Count > 0) PitObject = CreateGroundPart(root, "Pond Pits", pitVertices, pitUvs, pitTriangles);

        if (waterVertices.Count > 0)
        {
            BuildWater(root, waterVertices, waterUvs, waterTriangles);
            if (walkableWater && addCollider) BuildWaterWalkSurface(root);
        }
    }

    private GameObject CreateGroundPart(Transform root, string objName, List<Vector3> vertices, List<Vector2> uvs, List<int> triangles)
    {
        Mesh mesh = CreateMesh(objName, vertices, uvs, triangles);

        var obj = new GameObject(objName);
        obj.transform.SetParent(root, false);

        obj.AddComponent<MeshFilter>().sharedMesh = mesh;
        var meshRenderer = obj.AddComponent<MeshRenderer>();
        if (groundMaterial != null) meshRenderer.sharedMaterial = groundMaterial;

        if (addCollider) obj.AddComponent<MeshCollider>().sharedMesh = mesh;

        if (markGeneratedStatic) obj.isStatic = true;
        return obj;
    }

    private static Mesh CreateMesh(string meshName, List<Vector3> vertices, List<Vector2> uvs, List<int> triangles)
    {
        Mesh mesh = new Mesh { name = meshName };
        if (vertices.Count > 65535)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        return mesh;
    }

    // ---------- Hole Blockers: กำแพงล่องหนล้อมบ่อ/รู ตาม sub-cell ที่ BuildSubdividedGeometry เจาะไว้ ----------

    private void BuildHoleBlockers(Transform root)
    {
        if (_holeSubCells.Count == 0) return;

        int res = Mathf.Max(1, holeMeshResolution);
        float subSize = stepSize / res;
        float originOffset = subSize * 0.5f - stepSize * 0.5f; // กลาง sub-cell (0,0) เทียบกับกลาง cell (0,0)

        HoleBlockerObject = MapBoundaryBuilder.Build(_holeSubCells, new MapBoundaryBuilder.Settings
        {
            name = "Hole Blockers",
            cellSize = subSize,
            origin = new Vector2(originOffset, originOffset),
            bottomY = Mathf.Min(PitFloorY, 0f) - 0.5f,
            topY = holeBlockerHeight,
            thickness = holeBlockerThickness,
        }, root);
    }

    // ---------- Island Underside: รายละเอียดการสร้าง mesh อยู่ใน IslandUndersideBuilder ----------

    private void BuildUnderside(Transform root)
    {
        float lip = undersideEdgeLip;
        if (addNoiseHoles && holesAsPonds) lip = Mathf.Max(lip, pitDepth + 0.3f); // กันก้นหลุมโผล่ทะลุใต้เกาะ

        Mesh mesh = IslandUndersideBuilder.Build(_cells, new IslandUndersideBuilder.Settings
        {
            shape = undersideType,
            stepSize = stepSize,
            resolution = undersideResolution,
            maxDepth = undersideMaxDepth,
            edgeLip = lip,
            taperDistance = undersideTaperDistance,
            noiseAmount = undersideNoise,
            noiseScale = undersideNoiseScale,
            noiseOffset = Random.Range(0f, 1000f),
            uvScale = textureWorldSize,
        });
        if (mesh == null) return;

        var obj = new GameObject("Island Underside");
        obj.transform.SetParent(root, false);

        obj.AddComponent<MeshFilter>().sharedMesh = mesh;
        obj.AddComponent<MeshRenderer>().sharedMaterial = GetUndersideMaterial();

        if (undersideCollider) obj.AddComponent<MeshCollider>().sharedMesh = mesh;

        if (markGeneratedStatic) obj.isStatic = true;
        UndersideObject = obj;
    }

    private Material GetUndersideMaterial()
    {
        if (undersideMaterial != null) return undersideMaterial;
        if (_fallbackUndersideMaterial != null) return _fallbackUndersideMaterial;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) return null;

        var mat = new Material(shader) { name = "Island Underside (auto)", color = new Color(0.36f, 0.27f, 0.2f, 1f) };
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.05f);

        _fallbackUndersideMaterial = mat;
        return mat;
    }

    // ---------- Pond Water: ผิวน้ำ (ไม่มี collider ตัน) ----------
    // walkableWater = พื้นลุยน้ำล่องหน (BuildWaterWalkSurface) / ไม่งั้น = trigger box ต่อรู ตกลงไปแล้ว PlayerMovement จะ respawn
    private void BuildWater(Transform root, List<Vector3> vertices, List<Vector2> uvs, List<int> triangles)
    {
        Mesh mesh = CreateMesh("Generated Water", vertices, uvs, triangles);

        var waterObj = new GameObject("Pond Water");
        waterObj.transform.SetParent(root, false);

        int layer = LayerMask.NameToLayer(waterLayerName);
        if (layer >= 0) waterObj.layer = layer;

        waterObj.AddComponent<MeshFilter>().sharedMesh = mesh;
        var rend = waterObj.AddComponent<MeshRenderer>();
        rend.sharedMaterial = GetWaterMaterial();
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        if (markGeneratedStatic) waterObj.isStatic = true;
        WaterObject = waterObj;

        if (walkableWater) return; // ลุยน้ำได้ ไม่ต้องมี trigger ตกน้ำ -> respawn

        // trigger ครอบแต่ละรู ตั้งแต่ก้นหลุมถึงผิวน้ำ (MeshCollider แบบ trigger ต้อง convex จึงใช้ box แทน)
        float top = WaterY;
        float bottom = PitFloorY;
        foreach (var hole in _holes)
        {
            if (!_cells.Contains(WorldToCell(new Vector3(hole.center.x, 0f, hole.center.y)))) continue;

            float r = hole.radius * (1f + holeEdgeJitter * 0.5f);
            var box = waterObj.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(hole.center.x, (top + bottom) * 0.5f, hole.center.y);
            box.size = new Vector3(r * 2f, top - bottom, r * 2f);
        }
    }

    // พื้นลุยน้ำ (collider อย่างเดียว ไม่มี renderer) ปิดบ่อตาม sub-cell ที่เจาะไว้
    // ขอบบ่อสูง 0 เท่าผิวพื้น แล้วลาดลงถึง waterWalkDepth ภายในระยะ waterWalkRampWidth
    // -> เดินลงน้ำตัวค่อยๆ จม เดินขึ้นฝั่งเองได้โดยไม่ต้องมีระบบก้าวขั้น (ผนัง/ก้นหลุมจริงยังอยู่ข้างล่างเป็นแค่ภาพ)
    private void BuildWaterWalkSurface(Transform root)
    {
        if (_holeSubCells.Count == 0) return;

        int res = Mathf.Max(1, holeMeshResolution);
        float subSize = stepSize / res;
        float cellHalf = stepSize * 0.5f;
        float depth = Mathf.Min(waterWalkDepth, pitDepth * 0.95f);

        // ระยะ (จำนวน sub-cell) จากขอบบ่อ: sub-cell ที่ติดพื้นปกติ = 1 แล้ว BFS เข้าไปข้างใน
        var dist = new Dictionary<Vector2Int, int>();
        var queue = new Queue<Vector2Int>();
        foreach (var g in _holeSubCells)
        {
            foreach (var d in Directions4)
            {
                if (_holeSubCells.Contains(g + d)) continue;
                dist[g] = 1;
                queue.Enqueue(g);
                break;
            }
        }
        while (queue.Count > 0)
        {
            var g = queue.Dequeue();
            foreach (var d in Directions4)
            {
                var n = g + d;
                if (!_holeSubCells.Contains(n) || dist.ContainsKey(n)) continue;
                dist[n] = dist[g] + 1;
                queue.Enqueue(n);
            }
        }

        // ความสูงต่อมุม (ใช้ vertex ร่วมกัน พื้นจะต่อเนื่องไม่มีรอยขั้นระหว่าง sub-cell)
        var cornerIndex = new Dictionary<Vector2Int, int>();
        var vertices = new List<Vector3>();
        var triangles = new List<int>();

        int Corner(Vector2Int c)
        {
            if (cornerIndex.TryGetValue(c, out int index)) return index;

            // มุมที่แตะ sub-cell พื้นปกติ = ระดับผิวพื้นพอดี / นอกนั้นลึกตามระยะจากขอบ
            int level = int.MaxValue;
            for (int oz = -1; oz <= 0; oz++)
            {
                for (int ox = -1; ox <= 0; ox++)
                {
                    var cell = new Vector2Int(c.x + ox, c.y + oz);
                    level = dist.TryGetValue(cell, out int k) ? Mathf.Min(level, k) : 0;
                    if (level == 0) break;
                }
                if (level == 0) break;
            }

            float y = -depth * Mathf.Clamp01(level * subSize / waterWalkRampWidth);
            index = vertices.Count;
            vertices.Add(new Vector3(-cellHalf + c.x * subSize, y, -cellHalf + c.y * subSize));
            cornerIndex[c] = index;
            return index;
        }

        foreach (var g in _holeSubCells)
        {
            // winding เดียวกับ AddQuad (v0,v1,v2 / v2,v1,v3) ให้ normal หันขึ้น
            int v0 = Corner(g);
            int v1 = Corner(new Vector2Int(g.x, g.y + 1));
            int v2 = Corner(new Vector2Int(g.x + 1, g.y));
            int v3 = Corner(new Vector2Int(g.x + 1, g.y + 1));
            triangles.Add(v0); triangles.Add(v1); triangles.Add(v2);
            triangles.Add(v2); triangles.Add(v1); triangles.Add(v3);
        }

        var mesh = new Mesh { name = "Generated Water Walk Surface" };
        if (vertices.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();

        // แยก object จาก Pond Water กัน MapBuildAnimator ขยับ collider ตามตอน animate ผิวน้ำ
        var obj = new GameObject("Pond Walk Surface");
        obj.transform.SetParent(root, false);
        obj.AddComponent<MeshCollider>().sharedMesh = mesh;

        if (markGeneratedStatic) obj.isStatic = true;
    }

    private Material GetWaterMaterial()
    {
        if (waterMaterial != null) return waterMaterial;
        if (_fallbackWaterMaterial != null) return _fallbackWaterMaterial;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) return null;

        var mat = new Material(shader) { name = "Pond Water (auto)", color = new Color(0.1f, 0.4f, 0.7f, 0.7f) };
        if (mat.HasProperty("_Surface")) // URP Lit -> transparent
        {
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.9f);

        _fallbackWaterMaterial = mat;
        return mat;
    }

    // ---------- Start / End Markers ----------

    private void PlaceMarkers(Transform root, Vector2Int startCell, Vector2Int endCell)
    {
        Vector3 startPos = CellToWorld(startCell);
        Vector3 endPos = CellToWorld(endCell);
        startPos.y = GetSurfaceHeight(startPos);
        endPos.y = GetSurfaceHeight(endPos);

        StartMarker = CreateMarker(root, "Start Marker", startMarkerPrefab, startPos, new Color(0.2f, 0.9f, 0.3f));
        EndMarker = CreateMarker(root, "End Marker", endMarkerPrefab, endPos, new Color(0.9f, 0.25f, 0.25f));
    }

    // วาง portal ไว้ใต้ GeneratedRoot จึงถูกลบพร้อมแผนที่เก่าทุกครั้งที่ generate ใหม่
    private void PlaceWarpPortal(Transform root, Vector2Int startCell)
    {
        if (warpPortalPrefab == null) return;

        // Y = ผิวพื้นจริง ณ จุดตั้ง (ไม่ใช่ความสูง transform ของ generator) offset จึงเป็นแค่ระยะ pivot->ฐานของ prefab
        Vector3 pos = CellToWorld(startCell);
        pos.y = GetSurfaceHeight(pos);
        WarpPortal = Instantiate(warpPortalPrefab, pos + warpPortalOffset, warpPortalPrefab.transform.rotation, root);
        WarpPortal.name = "Warp Portal";
    }

    private GameObject CreateMarker(Transform root, string markerName, GameObject prefab, Vector3 position, Color fallbackColor)
    {
        GameObject marker;
        if (prefab != null)
        {
            marker = Instantiate(prefab, position, Quaternion.identity, root);
        }
        else
        {
            marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.transform.SetParent(root, false);
            marker.transform.position = position;
            marker.transform.localScale = Vector3.one * markerSize;

            var col = marker.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }

            var rend = marker.GetComponent<Renderer>();
            if (rend != null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                if (shader != null)
                {
                    var mat = new Material(shader) { color = fallbackColor };
                    rend.sharedMaterial = mat;
                }
            }
        }

        marker.name = markerName;
        return marker;
    }

    // ---------- Object Scatter Preview ----------

    private void ScatterObjects(Transform root, Vector2Int startCell, Vector2Int endCell)
    {
        if (!scatterObjects || scatterCategories == null || scatterCategories.Count == 0) return;

        var scatterContainer = new GameObject("Scattered Objects");
        scatterContainer.transform.SetParent(root, false);
        ScatterContainer = scatterContainer.transform;

        // เก็บ cell ที่ใช้ไม่ได้ไว้ก่อน (จุด start/end กับ cell ที่โดน noise hole)
        HashSet<Vector2Int> excluded = new HashSet<Vector2Int> { startCell, endCell };

        List<Vector2Int> availableCells = new List<Vector2Int>();
        foreach (var c in _cells)
        {
            if (excluded.Contains(c)) continue;
            if (addNoiseHoles && IsInsideHole(CellToWorld(c))) continue;
            availableCells.Add(c);
        }
        ShuffleCells(availableCells);

        // material 1 ตัวต่อ 1 category (ไม่ใช่ 1 ต่อ object) กัน object เยอะแล้ว alloc material ซ้ำเป็นพันตัว
        // และจำเป็นสำหรับให้ Unity batch (SRP Batcher / GPU Instancing) ก้อน cube fallback เหล่านี้ได้
        var fallbackMaterials = new Dictionary<ScatterCategory, Material>();

        int cursor = 0;
        foreach (var category in scatterCategories)
        {
            if (category == null) continue;

            // กรอง null ออกจาก prefabVariants ไว้ล่วงหน้าครั้งเดียวต่อ category กันสุ่มเจอ slot ว่างซ้ำๆ ระหว่าง loop
            GameObject[] validPrefabs = category.prefabVariants != null
                ? System.Array.FindAll(category.prefabVariants, p => p != null)
                : null;

            int placed = 0;
            while (placed < category.count && cursor < availableCells.Count)
            {
                Vector2Int cell = availableCells[cursor];
                cursor++;

                Vector3 pos = CellToWorld(cell);
                pos.x += Random.Range(-stepSize, stepSize) * 0.5f * scatterPositionJitter;
                pos.z += Random.Range(-stepSize, stepSize) * 0.5f * scatterPositionJitter;
                pos.y = transform.position.y;

                CreateScatterObject(scatterContainer.transform, category, pos, fallbackMaterials, validPrefabs);
                placed++;
            }
        }

        if (combineScatterMeshes) ScatterMeshCombiner.Combine(scatterContainer.transform, !disableCollidersAfterCombine);
    }

    private void CreateScatterObject(Transform container, ScatterCategory category, Vector3 position,
        Dictionary<ScatterCategory, Material> fallbackMaterials, GameObject[] validPrefabs)
    {
        Quaternion rotation = category.randomizeRotation
            ? Quaternion.Euler(0f, Random.Range(category.rotationRange.x, category.rotationRange.y), 0f)
            : Quaternion.identity;
        float scaleMul = category.randomizeSize
            ? Random.Range(category.sizeRange.x, category.sizeRange.y)
            : 1f;
        // ไม่ได้เปิด Randomize Height แยก -> แกน Y ใช้ตัวคูณเดียวกับ X/Z (พฤติกรรมเดิม)
        float heightMul = category.randomizeHeight
            ? Random.Range(category.heightRange.x, category.heightRange.y)
            : scaleMul;
        Vector3 scaleVector = new Vector3(scaleMul, heightMul, scaleMul);

        GameObject prefab = (validPrefabs != null && validPrefabs.Length > 0)
            ? validPrefabs[Random.Range(0, validPrefabs.Length)] // สุ่มเลือก 1 แบบจากหลาย prefab ต่อ category
            : null;

        GameObject obj;
        if (prefab != null)
        {
            obj = Instantiate(prefab, position, rotation, container);
            if (scaleVector != Vector3.one) obj.transform.localScale = Vector3.Scale(obj.transform.localScale, scaleVector);
        }
        else
        {
            obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obj.transform.SetParent(container, false);
            obj.transform.SetPositionAndRotation(position, rotation);
            obj.transform.localScale = category.cubeScale * scaleVector;

            var rend = obj.GetComponent<Renderer>();
            if (rend != null)
            {
                if (!fallbackMaterials.TryGetValue(category, out var mat))
                {
                    Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                    if (shader == null) shader = Shader.Find("Standard");
                    if (shader != null)
                    {
                        mat = new Material(shader) { color = category.color };
                        mat.enableInstancing = true; // material ของเราเอง เปิด GPU Instancing ได้เต็มที่
                    }
                    fallbackMaterials[category] = mat;
                }
                if (mat != null) rend.sharedMaterial = mat;
            }
        }

        obj.name = category.categoryName;
        if (markGeneratedStatic) MarkStaticRecursive(obj);
    }

    // ตั้ง static ทั้งลูกหลาน เพราะ prefab ต้นไม้/ก้อนหินมักมี mesh หลายชิ้นแยกเป็น child object (canopy, LOD proxy ฯลฯ)
    private static void MarkStaticRecursive(GameObject go)
    {
        go.isStatic = true;
        foreach (Transform child in go.transform) MarkStaticRecursive(child.gameObject);
    }

    private void ShuffleCells(List<Vector2Int> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    // path เร็ว: 1 quad เต็มขนาด stepSize ต่อ cell (ใช้ตอนไม่มี noise holes)
    private void BuildSimpleGeometry(List<Vector3> vertices, List<Vector2> uvs, List<int> triangles)
    {
        float half = stepSize * 0.5f;

        foreach (var cell in _cells)
        {
            Vector3 center = CellToWorld(cell);
            AddQuad(vertices, uvs, triangles, center, half);
        }
    }

    // path ละเอียด: แบ่งแต่ละ cell เป็น sub-quad ย่อย เพื่อให้ตัดรูขนาดเล็กกว่า stepSize ได้
    // ถ้าเปิด holesAsPonds จะเติมพื้นก้นหลุม + ผนัง (ลง pit lists) และผิวน้ำ (ลง water lists) แทนการเจาะทะลุ
    private void BuildSubdividedGeometry(List<Vector3> vertices, List<Vector2> uvs, List<int> triangles,
        List<Vector3> pitVertices, List<Vector2> pitUvs, List<int> pitTriangles,
        List<Vector3> waterVertices, List<Vector2> waterUvs, List<int> waterTriangles)
    {
        int res = Mathf.Max(1, holeMeshResolution);
        float subSize = stepSize / res;
        float subHalf = subSize * 0.5f;
        float cellHalf = stepSize * 0.5f;
        bool ponds = holesAsPonds && pitDepth > 0f;

        // sub-cell index แบบ global (ต่อเนื่องข้าม cell) จะได้เช็คเพื่อนบ้านข้ามขอบ cell ได้
        Vector3 SubToWorld(Vector2Int g) =>
            new Vector3(-cellHalf + g.x * subSize + subHalf, 0f, -cellHalf + g.y * subSize + subHalf);

        var subCells = new List<Vector2Int>();
        var holeSubs = new HashSet<Vector2Int>();

        foreach (var cell in _cells)
        {
            for (int sz = 0; sz < res; sz++)
            {
                for (int sx = 0; sx < res; sx++)
                {
                    var g = new Vector2Int(cell.x * res + sx, cell.y * res + sz);
                    subCells.Add(g);
                    if (IsInsideHole(SubToWorld(g))) { holeSubs.Add(g); _holeSubCells.Add(g); }
                }
            }
        }

        foreach (var g in subCells)
        {
            Vector3 center = SubToWorld(g);

            if (!holeSubs.Contains(g))
            {
                AddQuad(vertices, uvs, triangles, center, subHalf);
                continue;
            }

            if (!ponds) continue; // เจาะทะลุเหมือนเดิม

            center.y = PitFloorY;
            AddQuad(pitVertices, pitUvs, pitTriangles, center, subHalf);

            center.y = WaterY;
            AddQuad(waterVertices, waterUvs, waterTriangles, center, subHalf);

            // ผนังเฉพาะด้านที่ติดพื้นปกติ (หรือขอบแผนที่)
            foreach (var d in Directions4)
            {
                if (holeSubs.Contains(g + d)) continue;
                AddWall(pitVertices, pitUvs, pitTriangles, SubToWorld(g), d, subHalf, pitDepth);
            }
        }
    }

    // ผนังหลุมที่ขอบด้าน dir ของ sub-quad ในรู หันหน้าเข้าหาข้างในรู
    private void AddWall(List<Vector3> vertices, List<Vector2> uvs, List<int> triangles,
        Vector3 holeCenter, Vector2Int dir, float half, float depth)
    {
        Vector3 outward = new Vector3(dir.x, 0f, dir.y);
        Vector3 tangent = new Vector3(dir.y, 0f, dir.x);
        Vector3 edge = holeCenter + outward * half;

        Vector3 tl = edge - tangent * half;
        Vector3 tr = edge + tangent * half;
        Vector3 bl = tl + Vector3.down * depth;
        Vector3 br = tr + Vector3.down * depth;

        // winding เดียวกับ AddQuad (v0,v1,v2 / v2,v1,v3) สลับซ้ายขวาถ้า normal ไม่หันเข้ารู
        if (Vector3.Dot(Vector3.Cross(tl - bl, br - bl), -outward) < 0f)
        {
            (bl, br) = (br, bl);
            (tl, tr) = (tr, tl);
        }

        int baseIndex = vertices.Count;
        vertices.Add(bl); vertices.Add(tl); vertices.Add(br); vertices.Add(tr);

        Vector3 along = new Vector3(Mathf.Abs(dir.y), 0f, Mathf.Abs(dir.x));
        foreach (var p in new[] { bl, tl, br, tr })
            uvs.Add(new Vector2(Vector3.Dot(p, along) / textureWorldSize, p.y / textureWorldSize));

        triangles.Add(baseIndex + 0);
        triangles.Add(baseIndex + 1);
        triangles.Add(baseIndex + 2);
        triangles.Add(baseIndex + 2);
        triangles.Add(baseIndex + 1);
        triangles.Add(baseIndex + 3);
    }

    private void AddQuad(List<Vector3> vertices, List<Vector2> uvs, List<int> triangles, Vector3 center, float half)
    {
        int baseIndex = vertices.Count;

        Vector3 v0 = center + new Vector3(-half, 0, -half);
        Vector3 v1 = center + new Vector3(-half, 0, half);
        Vector3 v2 = center + new Vector3(half, 0, -half);
        Vector3 v3 = center + new Vector3(half, 0, half);

        vertices.Add(v0); vertices.Add(v1); vertices.Add(v2); vertices.Add(v3);

        uvs.Add(new Vector2(v0.x / textureWorldSize, v0.z / textureWorldSize));
        uvs.Add(new Vector2(v1.x / textureWorldSize, v1.z / textureWorldSize));
        uvs.Add(new Vector2(v2.x / textureWorldSize, v2.z / textureWorldSize));
        uvs.Add(new Vector2(v3.x / textureWorldSize, v3.z / textureWorldSize));

        triangles.Add(baseIndex + 0);
        triangles.Add(baseIndex + 1);
        triangles.Add(baseIndex + 2);
        triangles.Add(baseIndex + 2);
        triangles.Add(baseIndex + 1);
        triangles.Add(baseIndex + 3);
    }

    private Vector2Int WorldToCell(Vector3 worldPos)
    {
        return new Vector2Int(Mathf.RoundToInt(worldPos.x / stepSize), Mathf.RoundToInt(worldPos.z / stepSize));
    }

    private Vector3 CellToWorld(Vector2Int cell)
    {
        return new Vector3(cell.x * stepSize, 0f, cell.y * stepSize);
    }
}
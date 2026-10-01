using UnityEngine;
using System.Collections.Generic;

// Phase 2: สร้าง mesh (chunk พื้น/ทางลาด/หน้าผา, กำแพงรอบรู, ใต้เกาะ, geometry ละเอียด) (partial ของ ProceduralMapGenerator ไฟล์หลัก = ProceduralMapGenerator.cs)
public partial class ProceduralMapGenerator
{
    // ---------- Phase 2: สร้าง mesh แบ่งเป็น chunk ----------

    // mesh ของ 1 ส่วนใน 1 chunk
    private class MeshLists
    {
        public readonly List<Vector3> vertices = new List<Vector3>();
        public readonly List<Vector2> uvs = new List<Vector2>();
        public readonly List<int> triangles = new List<int>();
    }

    // พื้น/ทางลาด/หน้าผา แบ่งเป็น chunk ละ terrainChunkSize x terrainChunkSize cell แล้วแยกย่อยตามชั้น (key = x, z, level)
    // -> OcclusionOutlineController fade เฉพาะชั้นที่สูงกว่า player ใน chunk ที่บังได้ + mesh collider ไม่ใหญ่เกิน (physics midphase)
    private readonly Dictionary<Vector3Int, MeshLists> _groundChunks = new Dictionary<Vector3Int, MeshLists>();
    private readonly Dictionary<Vector3Int, MeshLists> _rampChunks = new Dictionary<Vector3Int, MeshLists>();
    private readonly Dictionary<Vector3Int, MeshLists> _cliffChunks = new Dictionary<Vector3Int, MeshLists>();
    private readonly List<Transform> _terrainChunks = new List<Transform>();

    // กลุ่มพื้นรอบล่าสุด 1 ตัว = 1 ชั้นใน 1 chunk (ลูก Ground / Ramps / Cliffs ของชั้นนั้น)
    // หน้าผานับเป็นของชั้นบน ทางลาดนับเป็นของชั้นที่หัวลาดขึ้นไปถึง ให้ระบบอื่นเช่นตัว fade ตอนบังใช้
    public IReadOnlyList<Transform> TerrainChunks => _terrainChunks;

    private Vector3Int ChunkOf(Vector2Int cell)
    {
        int size = Mathf.Max(1, Config.terrainChunkSize);
        return new Vector3Int(Mathf.FloorToInt(cell.x / (float)size), Mathf.FloorToInt(cell.y / (float)size), Terrace.GetLevel(cell));
    }

    private static MeshLists GetChunk(Dictionary<Vector3Int, MeshLists> chunks, Vector3Int key)
    {
        if (!chunks.TryGetValue(key, out var lists)) chunks[key] = lists = new MeshLists();
        return lists;
    }

    private void BuildGroundMesh(Transform root)
    {
        _holeSubCells.Clear();
        _groundChunks.Clear(); _rampChunks.Clear(); _cliffChunks.Clear();
        _terrainChunks.Clear();
        var pitVertices = new List<Vector3>();
        var pitUvs = new List<Vector2>();
        var pitTriangles = new List<int>();
        var waterVertices = new List<Vector3>();
        var waterUvs = new List<Vector2>();
        var waterTriangles = new List<int>();

        if (addNoiseHoles && holeMeshResolution > 1 && _holes.Count > 0)
            BuildSubdividedGeometry(pitVertices, pitUvs, pitTriangles, waterVertices, waterUvs, waterTriangles);
        else
            BuildSimpleGeometry();
        BuildCliffs();

        // Generated Ground = กล่องรวม chunk (MapBuildAnimator ยกทั้งก้อนขึ้นพร้อมกัน)
        GroundObject = new GameObject("Generated Ground");
        GroundObject.transform.SetParent(root, false);
        if (markGeneratedStatic) GroundObject.isStatic = true;
        BuildTerrainChunks(GroundObject.transform);

        // ก้นหลุม + ผนังแยกเป็นอีก object (material เดียวกับพื้น) จะได้ animate/ซ่อนแยกจากผิวพื้นได้
        if (pitVertices.Count > 0) PitObject = CreateGroundPart(root, "Pond Pits", pitVertices, pitUvs, pitTriangles, Config.pondPhysicsMaterial);

        if (waterVertices.Count > 0)
        {
            BuildWater(root, waterVertices, waterUvs, waterTriangles);
            if (walkableWater && addCollider) BuildWaterWalkSurface(root);
        }
    }

    private void BuildTerrainChunks(Transform parent)
    {
        var keys = new HashSet<Vector3Int>(_groundChunks.Keys);
        keys.UnionWith(_rampChunks.Keys);
        keys.UnionWith(_cliffChunks.Keys);
        var sorted = new List<Vector3Int>(keys);
        sorted.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y != b.y ? a.y.CompareTo(b.y) : a.z.CompareTo(b.z));

        Material cliffRender = cliffMaterial != null ? cliffMaterial : GetUndersideMaterial();
        var chunkObjects = new Dictionary<Vector2Int, Transform>();
        foreach (var key in sorted)
        {
            var xz = new Vector2Int(key.x, key.y);
            if (!chunkObjects.TryGetValue(xz, out Transform chunk))
            {
                var chunkObj = new GameObject($"Chunk {key.x},{key.y}");
                chunkObj.transform.SetParent(parent, false);
                if (markGeneratedStatic) chunkObj.isStatic = true;
                chunkObjects[xz] = chunk = chunkObj.transform;
            }

            var level = new GameObject($"Level {key.z}");
            level.transform.SetParent(chunk, false);
            if (markGeneratedStatic) level.isStatic = true;

            if (_groundChunks.TryGetValue(key, out var g))
                CreateGroundPart(level.transform, "Ground", g.vertices, g.uvs, g.triangles, Config.groundPhysicsMaterial);
            if (_rampChunks.TryGetValue(key, out var r))
                CreateGroundPart(level.transform, "Ramps", r.vertices, r.uvs, r.triangles, Config.rampPhysicsMaterial);
            if (_cliffChunks.TryGetValue(key, out var c))
                CreateGroundPart(level.transform, "Cliffs", c.vertices, c.uvs, c.triangles, Config.cliffPhysicsMaterial, cliffRender);

            _terrainChunks.Add(level.transform);
        }
    }

    private GameObject CreateGroundPart(Transform root, string objName, List<Vector3> vertices, List<Vector2> uvs, List<int> triangles,
        PhysicsMaterial physicsMaterial, Material renderMaterial = null)
    {
        Mesh mesh = CreateMesh(objName, vertices, uvs, triangles);

        var obj = new GameObject(objName);
        obj.transform.SetParent(root, false);

        obj.AddComponent<MeshFilter>().sharedMesh = mesh;
        var meshRenderer = obj.AddComponent<MeshRenderer>();
        Material mat = renderMaterial != null ? renderMaterial : groundMaterial;
        if (mat != null) meshRenderer.sharedMaterial = mat;

        if (addCollider)
        {
            var col = obj.AddComponent<MeshCollider>();
            col.sharedMesh = mesh;
            col.sharedMaterial = physicsMaterial;
        }

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

    // ---------- Cliffs: ผนังตรงขอบ cell ที่สูงไม่เท่ากัน (หน้าผาระหว่างชั้น ข้างทางลาด และขอบแผนที่ที่สูงกว่า 0) ----------

    private void BuildCliffs()
    {
        const float eps = 0.001f;
        float half = stepSize * 0.5f;

        foreach (var cell in _cells)
        {
            Vector3 center = CellToWorld(cell);
            foreach (var d in Directions4)
            {
                Vector2Int n = cell + d;
                bool hasNeighbor = _cells.Contains(n);

                // ปลายขอบทั้งสองข้าง (local เทียบกลาง cell นี้ และกลาง cell ข้างๆ)
                var tangent = new Vector2(d.y, d.x);
                Vector2 pLocal = (Vector2)d * half - tangent * half;
                Vector2 qLocal = (Vector2)d * half + tangent * half;
                Vector2 offset = (Vector2)d * stepSize;

                float topP = Terrace.HeightAt(cell, pLocal), topQ = Terrace.HeightAt(cell, qLocal);
                // ขอบแผนที่: ลงไปถึงใต้ 0 นิดหน่อย ให้ต่อกับขอบบนของ underside สนิท
                float botP = hasNeighbor ? Terrace.HeightAt(n, pLocal - offset) : -0.1f;
                float botQ = hasNeighbor ? Terrace.HeightAt(n, qLocal - offset) : -0.1f;

                // สร้างเฉพาะฝั่งที่สูงกว่า กันผนังซ้อนสองชั้น
                if (topP <= botP + eps && topQ <= botQ + eps) continue;
                if (!hasNeighbor && topP <= eps && topQ <= eps) continue;
                botP = Mathf.Min(botP, topP);
                botQ = Mathf.Min(botQ, topQ);

                Vector3 p = center + new Vector3(pLocal.x, 0f, pLocal.y);
                Vector3 q = center + new Vector3(qLocal.x, 0f, qLocal.y);
                // ผนังอยู่ chunk เดียวกับ cell ฝั่งสูง (fade พร้อมที่ราบที่มันเป็นขอบ)
                MeshLists lists = GetChunk(_cliffChunks, ChunkOf(cell));
                AddCliffQuad(lists.vertices, lists.uvs, lists.triangles,
                    new Vector3(p.x, topP, p.z), new Vector3(q.x, topQ, q.z),
                    new Vector3(p.x, botP, p.z), new Vector3(q.x, botQ, q.z),
                    new Vector3(d.x, 0f, d.y));
            }
        }
    }

    // ผนังตั้งจากขอบบน (pTop,qTop) ถึงขอบล่าง หันหน้าไปทาง outward (ฝั่งที่ต่ำกว่า)
    private void AddCliffQuad(List<Vector3> vertices, List<Vector2> uvs, List<int> triangles,
        Vector3 pTop, Vector3 qTop, Vector3 pBot, Vector3 qBot, Vector3 outward)
    {
        // winding เดียวกับ AddQuad (v0,v1,v2 / v2,v1,v3) สลับ p/q ถ้า normal ไม่หันออก
        // ใช้แกน up แทน pTop - pBot เพราะผนังข้างทางลาดเป็นสามเหลี่ยม ฝั่งหนึ่งสูง 0 จะได้ cross = 0 แล้วไม่สลับ (หน้าหันผิด มองทะลุ)
        if (Vector3.Dot(Vector3.Cross(Vector3.up, qBot - pBot), outward) < 0f)
        {
            (pTop, qTop) = (qTop, pTop);
            (pBot, qBot) = (qBot, pBot);
        }

        int baseIndex = vertices.Count;
        vertices.Add(pBot); vertices.Add(pTop); vertices.Add(qBot); vertices.Add(qTop);

        Vector3 along = new Vector3(Mathf.Abs(outward.z), 0f, Mathf.Abs(outward.x));
        uvs.Add(new Vector2(Vector3.Dot(pBot, along) / textureWorldSize, pBot.y / textureWorldSize));
        uvs.Add(new Vector2(Vector3.Dot(pTop, along) / textureWorldSize, pTop.y / textureWorldSize));
        uvs.Add(new Vector2(Vector3.Dot(qBot, along) / textureWorldSize, qBot.y / textureWorldSize));
        uvs.Add(new Vector2(Vector3.Dot(qTop, along) / textureWorldSize, qTop.y / textureWorldSize));

        triangles.Add(baseIndex + 0); triangles.Add(baseIndex + 1); triangles.Add(baseIndex + 2);
        triangles.Add(baseIndex + 2); triangles.Add(baseIndex + 1); triangles.Add(baseIndex + 3);
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
            topY = Terrace.MaxHeight + holeBlockerHeight, // บ่ออยู่ได้ทุกชั้น (บ่ออยู่กลางที่ราบ กำแพงสูงเกินไม่ไปขวางชั้นอื่น)
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

    // path เร็ว: 1 quad เต็มขนาด stepSize ต่อ cell (ใช้ตอนไม่มี noise holes)
    private void BuildSimpleGeometry()
    {
        float half = stepSize * 0.5f;

        foreach (var cell in _cells)
        {
            Vector3 center = CellToWorld(cell);
            AddTerrainQuad(cell, center, half);
        }
    }

    // sub-cell index แบบ global -> cell ที่มันอยู่ (หารปัดลง รองรับค่าติดลบ)
    private static Vector2Int SubToCell(Vector2Int g, int res) =>
        new Vector2Int(Mathf.FloorToInt(g.x / (float)res), Mathf.FloorToInt(g.y / (float)res));

    // เหมือน AddQuad แต่ความสูงแต่ละมุมตามชั้น/ทางลาดของ cell (center.y ไม่ใช้)
    // ลง mesh ของ chunk ที่ cell อยู่ ทางลาดแยกเป็นส่วน Ramps (ทางลาดไม่มีรู/บ่อ เพราะ TerraceLayout กันไว้)
    private void AddTerrainQuad(Vector2Int cell, Vector3 center, float half)
    {
        MeshLists lists = GetChunk(Terrace.IsRamp(cell) ? _rampChunks : _groundChunks, ChunkOf(cell));
        var vertices = lists.vertices;
        var uvs = lists.uvs;
        var triangles = lists.triangles;

        int baseIndex = vertices.Count;
        AddQuad(vertices, uvs, triangles, center, half);

        Vector3 cellCenter = CellToWorld(cell);
        for (int i = baseIndex; i < vertices.Count; i++)
        {
            Vector3 v = vertices[i];
            v.y = Terrace.HeightAt(cell, new Vector2(v.x - cellCenter.x, v.z - cellCenter.z));
            vertices[i] = v;
        }
    }

    // path ละเอียด: แบ่งแต่ละ cell เป็น sub-quad ย่อย เพื่อให้ตัดรูขนาดเล็กกว่า stepSize ได้
    // ถ้าเปิด holesAsPonds จะเติมพื้นก้นหลุม + ผนัง (ลง pit lists) และผิวน้ำ (ลง water lists) แทนการเจาะทะลุ
    private void BuildSubdividedGeometry(
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

        // บ่อลุยน้ำได้: ก้นบ่อที่มองเห็น = พื้นที่เท้าเหยียบจริง (ลาดจากขอบ) ไม่งั้นเงาตกบนก้นบ่อลึกกว่าเท้า ดูตัวลอย
        var pondFloorY = ponds && walkableWater ? CreatePondFloorHeight() : null;

        foreach (var g in subCells)
        {
            Vector3 center = SubToWorld(g);
            Vector2Int cell = SubToCell(g, res);

            if (!holeSubs.Contains(g))
            {
                AddTerrainQuad(cell, center, subHalf);
                continue;
            }

            if (!ponds) continue; // เจาะทะลุเหมือนเดิม

            // บ่ออยู่บนที่ราบชั้นเดียวเสมอ (GenerateNoiseHoleSeeds กันไว้) ความสูงทุกส่วนของบ่ออิงระดับชั้นของ cell
            float baseY = Terrace.BaseHeight(cell);

            center.y = baseY + WaterY;
            AddQuad(waterVertices, waterUvs, waterTriangles, center, subHalf);

            if (pondFloorY != null)
            {
                // ขอบลาดชนผิวพื้นพอดี ไม่ต้องมีผนัง
                AddPondFloorQuad(pitVertices, pitUvs, pitTriangles, g, subSize, cellHalf, pondFloorY);
                continue;
            }

            center.y = baseY + PitFloorY;
            AddQuad(pitVertices, pitUvs, pitTriangles, center, subHalf);

            // ผนังเฉพาะด้านที่ติดพื้นปกติ (หรือขอบแผนที่)
            foreach (var d in Directions4)
            {
                if (holeSubs.Contains(g + d)) continue;
                AddWall(pitVertices, pitUvs, pitTriangles, SubToWorld(g) + Vector3.up * baseY, d, subHalf, pitDepth);
            }
        }
    }

    // sub-quad ก้นบ่อที่ความสูงแต่ละมุมตาม floorY (ลำดับ/winding เดียวกับ AddQuad)
    private void AddPondFloorQuad(List<Vector3> vertices, List<Vector2> uvs, List<int> triangles,
        Vector2Int g, float subSize, float cellHalf, System.Func<Vector2Int, float> floorY)
    {
        int baseIndex = vertices.Count;
        foreach (var c in new[] { g, new Vector2Int(g.x, g.y + 1), new Vector2Int(g.x + 1, g.y), new Vector2Int(g.x + 1, g.y + 1) })
        {
            var v = new Vector3(-cellHalf + c.x * subSize, floorY(c), -cellHalf + c.y * subSize);
            vertices.Add(v);
            uvs.Add(new Vector2(v.x / textureWorldSize, v.z / textureWorldSize));
        }

        triangles.Add(baseIndex + 0);
        triangles.Add(baseIndex + 1);
        triangles.Add(baseIndex + 2);
        triangles.Add(baseIndex + 2);
        triangles.Add(baseIndex + 1);
        triangles.Add(baseIndex + 3);
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
}

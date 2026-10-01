using UnityEngine;
using System.Collections.Generic;

// บ่อน้ำ: ผิวน้ำ, ก้นบ่อ, พื้นลุยน้ำล่องหน (partial ของ ProceduralMapGenerator ไฟล์หลัก = ProceduralMapGenerator.cs)
public partial class ProceduralMapGenerator
{
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
            float baseY = Terrace.BaseHeight(WorldToCell(new Vector3(hole.center.x, 0f, hole.center.y)));
            var box = waterObj.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(hole.center.x, baseY + (top + bottom) * 0.5f, hole.center.y);
            box.size = new Vector3(r * 2f, top - bottom, r * 2f);
        }
    }

    // พื้นลุยน้ำ (collider อย่างเดียว ไม่มี renderer) ปิดบ่อตาม sub-cell ที่เจาะไว้
    // ความสูงพื้นบ่อลุยน้ำที่มุม sub-cell (มุม c = มุมล่างซ้ายของ sub-cell c ใน index แบบ global)
    // ขอบบ่อสูงเท่าผิวพื้น แล้วลาดลงถึง waterWalkDepth ภายในระยะ waterWalkRampWidth
    // ใช้ทั้ง collider (BuildWaterWalkSurface) และก้นบ่อที่มองเห็น (BuildSubdividedGeometry) -> ภาพตรงกับที่เท้าเหยียบ เงาไม่ลอย
    // ต้องเรียกหลัง _holeSubCells ครบแล้ว
    private System.Func<Vector2Int, float> CreatePondFloorHeight()
    {
        int res = Mathf.Max(1, holeMeshResolution);
        float subSize = stepSize / res;
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

        // บ่อทั้งบ่ออยู่ชั้นเดียว อิงชั้นจาก sub-cell บ่อที่แตะมุมนี้ (ทุกมุมที่ถูกเรียกแตะ sub-cell บ่ออย่างน้อย 1 อัน)
        float PondBaseHeight(Vector2Int c)
        {
            for (int oz = -1; oz <= 0; oz++)
                for (int ox = -1; ox <= 0; ox++)
                {
                    var sub = new Vector2Int(c.x + ox, c.y + oz);
                    if (_holeSubCells.Contains(sub)) return Terrace.BaseHeight(SubToCell(sub, res));
                }
            return 0f;
        }

        return c =>
        {
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
            return PondBaseHeight(c) - depth * Mathf.Clamp01(level * subSize / waterWalkRampWidth);
        };
    }

    // พื้นล่องหนสำหรับลุยน้ำ (collider) -> เดินลงน้ำตัวค่อยๆ จม เดินขึ้นฝั่งเองได้โดยไม่ต้องมีระบบก้าวขั้น
    private void BuildWaterWalkSurface(Transform root)
    {
        if (_holeSubCells.Count == 0) return;

        int res = Mathf.Max(1, holeMeshResolution);
        float subSize = stepSize / res;
        float cellHalf = stepSize * 0.5f;
        var floorY = CreatePondFloorHeight();

        // ความสูงต่อมุม (ใช้ vertex ร่วมกัน พื้นจะต่อเนื่องไม่มีรอยขั้นระหว่าง sub-cell)
        var cornerIndex = new Dictionary<Vector2Int, int>();
        var vertices = new List<Vector3>();
        var triangles = new List<int>();

        int Corner(Vector2Int c)
        {
            if (cornerIndex.TryGetValue(c, out int index)) return index;

            float y = floorY(c);
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
        var col = obj.AddComponent<MeshCollider>();
        col.sharedMesh = mesh;
        col.sharedMaterial = Config.pondPhysicsMaterial;

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
}

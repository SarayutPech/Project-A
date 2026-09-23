using System.Collections.Generic;
using UnityEngine;

public enum UndersideType
{
    Island, // ก้อนหินเรียวลงด้านล่าง
    Cube,   // ฐานทรงกล่อง: ยก footprint ลงตรงๆ ผนังตั้งฉาก ก้นเรียบ
}

// สร้าง mesh ใต้พื้นให้ดูเป็นเกาะลอย แยกออกมาจาก ProceduralMapGenerator
// Island: heightfield ที่ลึกขึ้นเรื่อยๆ ตามระยะห่างจากขอบ footprint + noise ให้ผิวไม่เรียบ
public static class IslandUndersideBuilder
{
    public struct Settings
    {
        public UndersideType shape;
        public float stepSize;
        public int resolution;        // quad ต่อ 1 cell
        public float maxDepth;        // ความลึกสูงสุดใต้ผิวพื้น
        public float edgeLip;         // ความหนาผนังตรงขอบ (ลึกอย่างน้อยเท่านี้ทุกจุด)
        public float taperDistance;   // ยิ่งน้อยยิ่งชัน (ระยะจากขอบที่ลึกถึง ~63% ของ maxDepth)
        public float noiseAmount;     // ความสูงต่ำของ noise (world unit)
        public float noiseScale;
        public float noiseOffset;
        public float uvScale;         // world unit ต่อ 1 UV
    }

    // vertex color.r = ความลึก 0..1 (0 = ขอบบน, 1 = ลึกสุด) ไว้ blend หญ้า/ดิน/หินใน Shader Graph
    public static Mesh Build(HashSet<Vector2Int> cells, Settings s)
    {
        if (cells == null || cells.Count == 0) return null;

        int res = Mathf.Max(1, s.resolution);
        float sub = s.stepSize / res;
        float origin = -s.stepSize * 0.5f;
        float lip = Mathf.Max(0f, s.edgeLip);
        float maxDepth = Mathf.Max(s.maxDepth, lip + 0.1f);
        float taper = Mathf.Max(0.01f, s.taperDistance);

        int minX = int.MaxValue, maxX = int.MinValue, minZ = int.MaxValue, maxZ = int.MinValue;
        foreach (var c in cells)
        {
            minX = Mathf.Min(minX, c.x); maxX = Mathf.Max(maxX, c.x);
            minZ = Mathf.Min(minZ, c.y); maxZ = Mathf.Max(maxZ, c.y);
        }

        int W = (maxX - minX + 1) * res;
        int H = (maxZ - minZ + 1) * res;

        var quad = new bool[W, H];
        foreach (var c in cells)
            for (int sz = 0; sz < res; sz++)
                for (int sx = 0; sx < res; sx++)
                    quad[(c.x - minX) * res + sx, (c.y - minZ) * res + sz] = true;

        bool Q(int x, int z) => x >= 0 && z >= 0 && x < W && z < H && quad[x, z];

        if (s.shape == UndersideType.Cube)
            return BuildCube(quad, W, H, origin + minX * res * sub, origin + minZ * res * sub, sub, maxDepth, s.uvScale);

        // vertex grid: vertex (vx,vz) เป็นมุมของ quad (vx-1..vx, vz-1..vz)
        int VW = W + 1, VH = H + 1;
        var inFoot = new bool[VW, VH];
        var dist = new float[VW, VH];

        for (int vz = 0; vz < VH; vz++)
        {
            for (int vx = 0; vx < VW; vx++)
            {
                bool a = Q(vx - 1, vz - 1), b = Q(vx, vz - 1), c = Q(vx - 1, vz), d = Q(vx, vz);
                inFoot[vx, vz] = a || b || c || d;
                bool boundary = inFoot[vx, vz] && !(a && b && c && d);
                dist[vx, vz] = boundary ? 0f : float.PositiveInfinity;
            }
        }

        ChamferDistance(dist, inFoot, VW, VH);

        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var colors = new List<Color>();
        var triangles = new List<int>();
        var idx = new int[VW, VH];

        for (int vz = 0; vz < VH; vz++)
        {
            for (int vx = 0; vx < VW; vx++)
            {
                idx[vx, vz] = -1;
                if (!inFoot[vx, vz]) continue;

                float x = origin + (minX * res + vx) * sub;
                float z = origin + (minZ * res + vz) * sub;
                float d = dist[vx, vz] * sub;

                float inner = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(d / taper));
                float depth = lip + (maxDepth - lip) * (1f - Mathf.Exp(-d / taper));

                float n1 = (Mathf.PerlinNoise(x * s.noiseScale + s.noiseOffset, z * s.noiseScale + s.noiseOffset) - 0.5f) * 2f;
                float n2 = Mathf.Pow(Mathf.PerlinNoise(x * s.noiseScale * 2.3f + s.noiseOffset + 50f, z * s.noiseScale * 2.3f + s.noiseOffset + 50f), 3f);
                depth += (n1 + n2 * 1.5f) * s.noiseAmount * inner;
                depth = Mathf.Max(depth, lip);

                idx[vx, vz] = vertices.Count;
                vertices.Add(new Vector3(x, -depth, z));
                uvs.Add(new Vector2(x / s.uvScale, z / s.uvScale));
                colors.Add(new Color(Mathf.Clamp01(depth / maxDepth), 0f, 0f, 1f));
            }
        }

        for (int z = 0; z < H; z++)
        {
            for (int x = 0; x < W; x++)
            {
                if (!quad[x, z]) continue;

                int v00 = idx[x, z], v10 = idx[x + 1, z], v01 = idx[x, z + 1], v11 = idx[x + 1, z + 1];

                // winding กลับด้านกับพื้นด้านบน เพื่อให้ normal หันลงล่าง
                triangles.Add(v00); triangles.Add(v10); triangles.Add(v01);
                triangles.Add(v01); triangles.Add(v10); triangles.Add(v11);

                if (lip <= 0f) continue;

                if (!Q(x - 1, z)) AddWall(vertices, uvs, colors, triangles, s, idx[x, z], idx[x, z + 1], new Vector3(-1f, 0f, 0f));
                if (!Q(x + 1, z)) AddWall(vertices, uvs, colors, triangles, s, idx[x + 1, z], idx[x + 1, z + 1], new Vector3(1f, 0f, 0f));
                if (!Q(x, z - 1)) AddWall(vertices, uvs, colors, triangles, s, idx[x, z], idx[x + 1, z], new Vector3(0f, 0f, -1f));
                if (!Q(x, z + 1)) AddWall(vertices, uvs, colors, triangles, s, idx[x, z + 1], idx[x + 1, z + 1], new Vector3(0f, 0f, 1f));
            }
        }

        return CreateMesh(vertices, uvs, colors, triangles);
    }

    private static Mesh CreateMesh(List<Vector3> vertices, List<Vector2> uvs, List<Color> colors, List<int> triangles)
    {
        var mesh = new Mesh { name = "Island Underside" };
        if (vertices.Count > 65535)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        return mesh;
    }

    // ฐานทรงกล่อง: ก้นเรียบ + ผนังตั้งฉากตามขอบ footprint ทุก vertex แยกกัน เพื่อให้ขอบคมไม่ถูก smooth
    private static Mesh BuildCube(bool[,] quad, int W, int H, float x0, float z0, float sub, float depth, float uvScale)
    {
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var colors = new List<Color>();
        var triangles = new List<int>();

        bool Q(int x, int z) => x >= 0 && z >= 0 && x < W && z < H && quad[x, z];

        for (int z = 0; z < H; z++)
        {
            for (int x = 0; x < W; x++)
            {
                if (!quad[x, z]) continue;

                float xa = x0 + x * sub, xb = xa + sub;
                float za = z0 + z * sub, zb = za + sub;

                // ก้น: winding กลับด้านกับพื้นด้านบน normal หันลง
                int b = vertices.Count;
                AddVertex(vertices, uvs, colors, new Vector3(xa, -depth, za), uvScale, 1f);
                AddVertex(vertices, uvs, colors, new Vector3(xb, -depth, za), uvScale, 1f);
                AddVertex(vertices, uvs, colors, new Vector3(xa, -depth, zb), uvScale, 1f);
                AddVertex(vertices, uvs, colors, new Vector3(xb, -depth, zb), uvScale, 1f);
                triangles.Add(b); triangles.Add(b + 1); triangles.Add(b + 2);
                triangles.Add(b + 2); triangles.Add(b + 1); triangles.Add(b + 3);

                if (!Q(x - 1, z)) AddCubeWall(vertices, uvs, colors, triangles, new Vector3(xa, 0f, za), new Vector3(xa, 0f, zb), new Vector3(-1f, 0f, 0f), depth, uvScale);
                if (!Q(x + 1, z)) AddCubeWall(vertices, uvs, colors, triangles, new Vector3(xb, 0f, za), new Vector3(xb, 0f, zb), new Vector3(1f, 0f, 0f), depth, uvScale);
                if (!Q(x, z - 1)) AddCubeWall(vertices, uvs, colors, triangles, new Vector3(xa, 0f, za), new Vector3(xb, 0f, za), new Vector3(0f, 0f, -1f), depth, uvScale);
                if (!Q(x, z + 1)) AddCubeWall(vertices, uvs, colors, triangles, new Vector3(xa, 0f, zb), new Vector3(xb, 0f, zb), new Vector3(0f, 0f, 1f), depth, uvScale);
            }
        }

        return CreateMesh(vertices, uvs, colors, triangles);
    }

    private static void AddCubeWall(List<Vector3> vertices, List<Vector2> uvs, List<Color> colors, List<int> triangles,
        Vector3 topA, Vector3 topB, Vector3 outward, float depth, float uvScale)
    {
        Vector3 bottomA = topA + Vector3.down * depth;
        Vector3 bottomB = topB + Vector3.down * depth;

        // front normal = Cross(tl - bl, br - bl) สลับซ้ายขวาถ้าไม่หันออกนอก
        if (Vector3.Dot(Vector3.Cross(topA - bottomA, bottomB - bottomA), outward) < 0f)
        {
            (bottomA, bottomB) = (bottomB, bottomA);
            (topA, topB) = (topB, topA);
        }

        // UV: แนวนอนตามความยาวผนัง แนวตั้งตามความสูง (ผนังไม่ยืดเหมือนใช้ XZ)
        Vector3 along = new Vector3(Mathf.Abs(outward.z), 0f, Mathf.Abs(outward.x));
        int b = vertices.Count;
        foreach (var p in new[] { bottomA, topA, bottomB, topB })
        {
            vertices.Add(p);
            uvs.Add(new Vector2(Vector3.Dot(p, along) / uvScale, p.y / uvScale));
            colors.Add(new Color(Mathf.Clamp01(-p.y / depth), 0f, 0f, 1f));
        }

        triangles.Add(b); triangles.Add(b + 1); triangles.Add(b + 2);
        triangles.Add(b + 2); triangles.Add(b + 1); triangles.Add(b + 3);
    }

    private static void AddVertex(List<Vector3> vertices, List<Vector2> uvs, List<Color> colors, Vector3 p, float uvScale, float depth01)
    {
        vertices.Add(p);
        uvs.Add(new Vector2(p.x / uvScale, p.z / uvScale));
        colors.Add(new Color(depth01, 0f, 0f, 1f));
    }

    // ผนังตรงขอบ: จุดล่างใช้ vertex ของผิว underside ร่วมกัน (normal จึงต่อเนื่อง) จุดบนสร้างใหม่ที่ y = 0
    private static void AddWall(List<Vector3> vertices, List<Vector2> uvs, List<Color> colors, List<int> triangles,
        Settings s, int bottomA, int bottomB, Vector3 outward)
    {
        Vector3 pa = vertices[bottomA], pb = vertices[bottomB];
        Vector3 ta = new Vector3(pa.x, 0f, pa.z);
        Vector3 tb = new Vector3(pb.x, 0f, pb.z);

        int topA = vertices.Count;
        vertices.Add(ta); uvs.Add(new Vector2(ta.x / s.uvScale, ta.z / s.uvScale)); colors.Add(new Color(0f, 0f, 0f, 1f));
        int topB = vertices.Count;
        vertices.Add(tb); uvs.Add(new Vector2(tb.x / s.uvScale, tb.z / s.uvScale)); colors.Add(new Color(0f, 0f, 0f, 1f));

        // winding: front normal = Cross(tl - bl, br - bl) สลับซ้ายขวาถ้าไม่หันออกนอก
        if (Vector3.Dot(Vector3.Cross(ta - pa, pb - pa), outward) < 0f)
        {
            (bottomA, bottomB) = (bottomB, bottomA);
            (topA, topB) = (topB, topA);
        }

        triangles.Add(bottomA); triangles.Add(topA); triangles.Add(bottomB);
        triangles.Add(bottomB); triangles.Add(topA); triangles.Add(topB);
    }

    // ระยะจากขอบ (หน่วย = จำนวน vertex) แบบ 2-pass chamfer ได้ทรงกลมกว่า BFS 4 ทิศ
    private static void ChamferDistance(float[,] dist, bool[,] inFoot, int VW, int VH)
    {
        const float diag = 1.4142f;

        for (int vz = 0; vz < VH; vz++)
        {
            for (int vx = 0; vx < VW; vx++)
            {
                if (!inFoot[vx, vz]) continue;
                Relax(dist, vx, vz, vx - 1, vz, 1f, VW, VH);
                Relax(dist, vx, vz, vx, vz - 1, 1f, VW, VH);
                Relax(dist, vx, vz, vx - 1, vz - 1, diag, VW, VH);
                Relax(dist, vx, vz, vx + 1, vz - 1, diag, VW, VH);
            }
        }

        for (int vz = VH - 1; vz >= 0; vz--)
        {
            for (int vx = VW - 1; vx >= 0; vx--)
            {
                if (!inFoot[vx, vz]) continue;
                Relax(dist, vx, vz, vx + 1, vz, 1f, VW, VH);
                Relax(dist, vx, vz, vx, vz + 1, 1f, VW, VH);
                Relax(dist, vx, vz, vx + 1, vz + 1, diag, VW, VH);
                Relax(dist, vx, vz, vx - 1, vz + 1, diag, VW, VH);
            }
        }
    }

    private static void Relax(float[,] dist, int vx, int vz, int nx, int nz, float w, int VW, int VH)
    {
        if (nx < 0 || nz < 0 || nx >= VW || nz >= VH) return;
        float candidate = dist[nx, nz] + w;
        if (candidate < dist[vx, vz]) dist[vx, vz] = candidate;
    }
}

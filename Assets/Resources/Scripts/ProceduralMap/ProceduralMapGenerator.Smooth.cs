using UnityEngine;
using System.Collections.Generic;

// Smooth slopes: พื้นลาดเอียงต่อเนื่อง + ขอบเกาะโค้งมน (partial ของ ProceduralMapGenerator ไฟล์หลัก = ProceduralMapGenerator.cs)
// ความสูงมาจาก height field ใน TerraceLayout.BuildSmooth ส่วนรูปร่างขอบมาจาก marching squares บน occupancy ที่ blur แล้ว
// ผิวพื้นเป็น mesh เดียวกับโหมดเดิม (ลง _groundChunks / _cliffChunks) ระบบ chunk / fade ตอนบัง / collider จึงใช้ต่อได้เลย
public partial class ProceduralMapGenerator
{
    // sub-cell ต่อด้านของ 1 cell: มีบ่อ = ต้องตรงกับ holeMeshResolution (ขอบบ่อกับผิวพื้นจะได้ชนกันพอดี)
    private int SmoothResolution =>
        (addNoiseHoles && holeMeshResolution > 1 && _holes.Count > 0) ? holeMeshResolution : Config.slopeResolution;

    private void BuildSmoothHeightField(List<List<Vector2Int>> holeFootprints)
    {
        int res = Mathf.Max(1, SmoothResolution);
        var fixedCells = new HashSet<Vector2Int>();
        foreach (var group in holeFootprints)
            foreach (var c in group) fixedCells.Add(c);

        _terrace.BuildSmooth(_cells, fixedCells, new TerraceLayout.SmoothSettings
        {
            res = res,
            minAngle = Config.slopeMinAngle,
            maxAngle = Config.slopeMaxAngle,
            noiseScale = Config.slopeNoiseScale,
            pad = res + 2,
        }, seed);
    }

    // footprint ขอบโค้งมนระดับละเอียด (ใช้สร้างใต้เกาะ) / เส้นขอบด้านนอก (ใช้สร้างกำแพงล่องหน)
    private readonly HashSet<Vector2Int> _smoothFootprint = new HashSet<Vector2Int>();
    private float _smoothFootprintSize;   // ขนาดช่องของ footprint (world unit)
    private float _smoothFootprintShift;  // ค่า originShift ที่ IslandUndersideBuilder ต้องใช้กับ footprint นี้
    private readonly List<(Vector2 a, Vector2 b)> _smoothOutline = new List<(Vector2 a, Vector2 b)>();

    private static Vector2Int SubIndexToCell(int gx, int gz, int res) =>
        new Vector2Int(Mathf.FloorToInt(gx / (float)res), Mathf.FloorToInt(gz / (float)res));

    // จุดของ polygon ที่ตัดจาก 1 ช่อง marching squares: kind 0 = มุมที่อยู่ข้างใน, 1 = จุดตัดขาออก, 2 = จุดตัดขาเข้า
    private struct SmoothPoint
    {
        public Vector2 xz;
        public int kind;
        public bool outer; // จุดตัดที่ติดด้านนอกเกาะจริง (ไม่ใช่ขอบบ่อ)
    }

    private void BuildSmoothSurface()
    {
        _smoothFootprint.Clear();
        _smoothOutline.Clear();
        int res = Terrace.SmoothRes;
        float sub = stepSize / res, half = stepSize * 0.5f;

        int minCx = int.MaxValue, minCz = int.MaxValue, maxCx = int.MinValue, maxCz = int.MinValue;
        foreach (var c in _cells)
        {
            minCx = Mathf.Min(minCx, c.x); maxCx = Mathf.Max(maxCx, c.x);
            minCz = Mathf.Min(minCz, c.y); maxCz = Mathf.Max(maxCz, c.y);
        }
        if (minCx == int.MaxValue) return;

        // รัศมี blur จำกัด <= res-1 เพื่อไม่ให้ทางกว้าง 1 cell ความหนาแน่นตกต่ำกว่า 0.5 แล้วขาด
        int radius = Mathf.Clamp(Mathf.RoundToInt(Config.edgeRoundness * (res - 1)), 0, res - 1);
        int pad = radius + 2;
        int x0 = minCx * res - pad, z0 = minCz * res - pad;
        int W = (maxCx - minCx + 1) * res + pad * 2, H = (maxCz - minCz + 1) * res + pad * 2;

        var occ = new float[W * H];
        foreach (var c in _cells)
            for (int sz = 0; sz < res; sz++)
                for (int sx = 0; sx < res; sx++)
                    occ[(c.x * res + sx - x0) + (c.y * res + sz - z0) * W] = 1f;

        // box blur ด้วย integral image
        int iw = W + 1;
        var integral = new double[iw * (H + 1)];
        for (int z = 0; z < H; z++)
            for (int x = 0; x < W; x++)
            {
                int k = (x + 1) + (z + 1) * iw;
                integral[k] = occ[x + z * W] + integral[k - 1] + integral[k - iw] - integral[k - iw - 1];
            }

        bool HoleAt(int x, int z) => _holeSubCells.Contains(new Vector2Int(x + x0, z + z0));

        var density = new float[W * H];
        var blurred = new float[W * H]; // ความหนาแน่นของขอบนอกเกาะ (ไม่นับรูบ่อ)
        for (int z = 0; z < H; z++)
            for (int x = 0; x < W; x++)
            {
                int i = x + z * W;
                int xa = Mathf.Max(0, x - radius), xb = Mathf.Min(W, x + radius + 1);
                int za = Mathf.Max(0, z - radius), zb = Mathf.Min(H, z + radius + 1);
                double sum = integral[xb + zb * iw] - integral[xa + zb * iw] - integral[xb + za * iw] + integral[xa + za * iw];
                blurred[i] = (float)(sum / ((xb - xa) * (zb - za)));

                if (HoleAt(x, z)) { density[i] = 0f; continue; }

                if (occ[i] > 0f && _holeSubCells.Count > 0 &&
                    (HoleAt(x + 1, z) || HoleAt(x - 1, z) || HoleAt(x, z + 1) || HoleAt(x, z - 1)))
                {
                    density[i] = 1f; // ขอบบ่อ: ให้ตัดตรงกลางระหว่างกลาง sub-cell พอดี (ตรงกับ sub-quad ก้นบ่อ)
                    continue;
                }
                density[i] = blurred[i];
            }

        _rimBlur = blurred; _rimW = W; _rimH = H; _rimX0 = x0; _rimZ0 = z0; _rimSub = stepSize / res;
        BuildSmoothFootprint(blurred, W, H, x0, z0, res);
        if (SmoothCubeBase) BuildSmoothCap(blurred, W, H, x0, z0, res);

        var poly = new List<SmoothPoint>(8);
        var corner = new int[4];
        var cornerXz = new Vector2[4];
        int[] dxs = { 0, 1, 1, 0 }, dzs = { 0, 0, 1, 1 };

        for (int z = 0; z < H - 1; z++)
            for (int x = 0; x < W - 1; x++)
            {
                int inCount = 0;
                for (int k = 0; k < 4; k++)
                {
                    int cx = x + dxs[k], cz = z + dzs[k];
                    corner[k] = cx + cz * W;
                    cornerXz[k] = new Vector2(-half + (x0 + cx + 0.5f) * sub, -half + (z0 + cz + 0.5f) * sub);
                    if (density[corner[k]] >= 0.5f) inCount++;
                }
                if (inCount == 0) continue;

                poly.Clear();
                for (int k = 0; k < 4; k++)
                {
                    int n = (k + 1) & 3;
                    float dk = density[corner[k]], dn = density[corner[n]];
                    bool ink = dk >= 0.5f, inn = dn >= 0.5f;
                    if (ink) poly.Add(new SmoothPoint { xz = cornerXz[k], kind = 0 });
                    if (ink == inn) continue;

                    float t = Mathf.Clamp01((0.5f - dk) / (dn - dk));
                    int outsideCorner = ink ? n : k;
                    bool outer = !HoleAt((x + dxs[outsideCorner]), (z + dzs[outsideCorner]));
                    poly.Add(new SmoothPoint { xz = Vector2.Lerp(cornerXz[k], cornerXz[n], t), kind = ink ? 1 : 2, outer = outer });
                }
                if (poly.Count < 3) continue;

                // cell ที่ใช้จัดลง chunk: มุมแรกที่อยู่ใน cell จริง ไม่งั้นใช้ cell ของกลางช่อง
                Vector2Int chunkCell = WorldToCell(new Vector3((cornerXz[0].x + cornerXz[2].x) * 0.5f, 0f, (cornerXz[0].y + cornerXz[2].y) * 0.5f));
                for (int k = 0; k < 4; k++)
                {
                    if (occ[corner[k]] <= 0f || density[corner[k]] < 0.5f) continue;
                    chunkCell = SubIndexToCell(x + dxs[k] + x0, z + dzs[k] + z0, res);
                    break;
                }

                AddSmoothPolygon(poly, chunkCell);
                AddSmoothSkirts(poly, chunkCell);
            }
    }

    // ฐานแบบ Cube ในโหมด smooth: ผนังมาจาก skirt ของขอบโค้งแล้ว underside เหลือแค่ฝาก้นที่เป็น polygon ตามขอบโค้งเดียวกัน
    private bool CurvedRim => Config.smoothSlopes && Config.curvedRim;
    private bool SmoothCubeBase => buildUnderside && undersideType == UndersideType.Cube && !CurvedRim;

    private float SmoothCubeDepth
    {
        get
        {
            float lip = undersideEdgeLip;
            if (addNoiseHoles && holesAsPonds) lip = Mathf.Max(lip, pitDepth + 0.3f);
            return Mathf.Max(undersideMaxDepth, lip + 0.1f);
        }
    }

    private Mesh _smoothCapMesh;

    // density ขอบนอกของรอบล่าสุด (ไว้หาทิศเข้าเกาะตอนทำผนังเอียง)
    private float[] _rimBlur;
    private int _rimW, _rimH, _rimX0, _rimZ0;
    private float _rimSub;

    private float SampleRimBlur(float wx, float wz)
    {
        float half = stepSize * 0.5f;
        float u = Mathf.Clamp((wx + half) / _rimSub - 0.5f - _rimX0, 0f, _rimW - 1.001f);
        float v = Mathf.Clamp((wz + half) / _rimSub - 0.5f - _rimZ0, 0f, _rimH - 1.001f);
        int ix = (int)u, iz = (int)v; float fx = u - ix, fz = v - iz; int i = ix + iz * _rimW;
        return Mathf.Lerp(Mathf.Lerp(_rimBlur[i], _rimBlur[i + 1], fx), Mathf.Lerp(_rimBlur[i + _rimW], _rimBlur[i + _rimW + 1], fx), fz);
    }

    // ทิศชี้เข้าเกาะ (ตามความชันของ density) หน่วย
    private Vector2 RimInward(float wx, float wz)
    {
        float e = _rimSub * 3f; // กว้างหน่อยให้ทิศนิ่ง ไม่สั่นตามขั้นของ grid
        var g = new Vector2(SampleRimBlur(wx + e, wz) - SampleRimBlur(wx - e, wz), SampleRimBlur(wx, wz + e) - SampleRimBlur(wx, wz - e));
        return g.sqrMagnitude > 1e-8f ? g.normalized : Vector2.zero;
    }

    // ความลึกของผนังข้าง ณ ตำแหน่งนี้ (สุ่มเป็นลอนจาก noise ตำแหน่ง จึงต่อเนื่องข้าม segment)
    private float RimDepthAt(Vector2 p)
    {
        float t = TerraceLayout.Noise01(p.x * Config.rimNoiseScale + 71.3f, p.y * Config.rimNoiseScale + 19.7f);
        return Mathf.Lerp(Config.rimDepthMin, Mathf.Max(Config.rimDepthMin, Config.rimDepthMax), t);
    }

    // quad ผนังเอียง: winding ตัดสินจากขอบบน (e -> n หันออกนอกอยู่แล้ว) ไม่ใช่ขอบล่าง เพราะตรงโค้งนูนแหลมจุดล่างสองจุดอาจไขว้กัน
    // ซึ่ง AddCliffQuad จะเข้าใจว่าหันผิดด้านแล้วสลับ winding จน quad ถูก cull หายไปเป็นรอยแยก
    private void AddRimQuad(MeshLists lists, Vector3 pTop, Vector3 qTop, Vector3 pBot, Vector3 qBot, Vector3 outward)
    {
        int b = lists.vertices.Count;
        lists.vertices.Add(pBot); lists.vertices.Add(pTop); lists.vertices.Add(qBot); lists.vertices.Add(qTop);
        // vertex color.r = ความทึบของผนัง: ขอบบน = 1 ไล่ลงไปจน 0 ที่ก้น (shader ใช้ fade แบบ dither) ตั้งตามความสูงจริงของแต่ละจุด
        lists.colors.Add(new Color(0f, 0f, 0f, 1f)); lists.colors.Add(new Color(1f, 0f, 0f, 1f));
        lists.colors.Add(new Color(0f, 0f, 0f, 1f)); lists.colors.Add(new Color(1f, 0f, 0f, 1f));
        Vector3 along = new Vector3(Mathf.Abs(outward.z), 0f, Mathf.Abs(outward.x));
        foreach (var v in new[] { pBot, pTop, qBot, qTop })
            lists.uvs.Add(new Vector2(Vector3.Dot(v, along) / textureWorldSize, v.y / textureWorldSize));
        lists.triangles.Add(b); lists.triangles.Add(b + 1); lists.triangles.Add(b + 2);
        lists.triangles.Add(b + 2); lists.triangles.Add(b + 1); lists.triangles.Add(b + 3);
    }

    // จุดล่างของผนังเอียง: ลงไปที่ y = -ความลึก และขยับเข้าเกาะตาม rimTaper * ความสูงผนัง
    private Vector3 RimBottom(Vector2 p, float topY)
    {
        float depth = RimDepthAt(p);
        Vector2 inward = RimInward(p.x, p.y) * Mathf.Min(Config.rimTaper * (Mathf.Max(topY, 0f) + depth), 1.8f); // จำกัดไม่ให้เลยเข้าไปชนฝั่งตรงข้ามของเกาะแคบ
        return new Vector3(p.x + inward.x, -depth, p.y + inward.y);
    }

    // mesh ฝาก้น (y = -depth) จาก marching squares บน density ขอบนอก (ไม่นับรูบ่อ) normal หันลง
    private void BuildSmoothCap(float[] blurred, int W, int H, int x0, int z0, int res)
    {
        float sub = stepSize / res, half = stepSize * 0.5f, y = -SmoothCubeDepth;
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var cols = new List<Color>();
        var tris = new List<int>();
        var pts = new List<Vector2>(8);
        int[] dxs = { 0, 1, 1, 0 }, dzs = { 0, 0, 1, 1 };
        int runStart = -1;
        bool FullSquare(int sx, int sz) =>
            blurred[sx + sz * W] >= 0.5f && blurred[sx + 1 + sz * W] >= 0.5f &&
            blurred[sx + (sz + 1) * W] >= 0.5f && blurred[sx + 1 + (sz + 1) * W] >= 0.5f;

        for (int z = 0; z < H - 1; z++)
            for (int x = 0; x < W - 1; x++)
            {
                if (x == 0) runStart = -1;
                pts.Clear();
                // ช่องที่อยู่ข้างในล้วนต่อกันเป็นแถวแล้วรวมเป็น quad เดียว (ฝาก้นแบนอยู่ใต้เกาะ ไม่ต้องละเอียดเท่าผิวบน)
                if (FullSquare(x, z))
                {
                    if (runStart < 0) runStart = x;
                    if (x + 1 < W - 1 && FullSquare(x + 1, z)) continue;
                    float xa = -half + (x0 + runStart + 0.5f) * sub, xb = -half + (x0 + x + 1 + 0.5f) * sub;
                    float za = -half + (z0 + z + 0.5f) * sub, zb = za + sub;
                    pts.Add(new Vector2(xa, za)); pts.Add(new Vector2(xb, za)); pts.Add(new Vector2(xb, zb)); pts.Add(new Vector2(xa, zb));
                    runStart = -1;
                }
                else
                for (int k = 0; k < 4; k++)
                {
                    int n = (k + 1) & 3;
                    int ik = (x + dxs[k]) + (z + dzs[k]) * W, inx = (x + dxs[n]) + (z + dzs[n]) * W;
                    float dk = blurred[ik], dn = blurred[inx];
                    var pk = new Vector2(-half + (x0 + x + dxs[k] + 0.5f) * sub, -half + (z0 + z + dzs[k] + 0.5f) * sub);
                    var pn = new Vector2(-half + (x0 + x + dxs[n] + 0.5f) * sub, -half + (z0 + z + dzs[n] + 0.5f) * sub);
                    bool ink = dk >= 0.5f, inn = dn >= 0.5f;
                    if (ink) pts.Add(pk);
                    if (ink != inn) pts.Add(Vector2.Lerp(pk, pn, Mathf.Clamp01((0.5f - dk) / (dn - dk))));
                }
                if (pts.Count < 3) continue;

                int b = verts.Count;
                foreach (var p in pts)
                {
                    verts.Add(new Vector3(p.x, y, p.y));
                    uvs.Add(new Vector2(p.x / textureWorldSize, p.y / textureWorldSize));
                    cols.Add(new Color(1f, 0f, 0f, 1f));
                }
                for (int i = 1; i < pts.Count - 1; i++)
                {
                    // หันลง: cross.y < 0
                    int a = b, bb = b + i, c = b + i + 1;
                    if (Vector3.Cross(verts[bb] - verts[a], verts[c] - verts[a]).y > 0f) (bb, c) = (c, bb);
                    tris.Add(a); tris.Add(bb); tris.Add(c);
                }
            }

        _smoothCapMesh = null;
        if (verts.Count == 0) return;
        var mesh = new Mesh { name = "Island Underside" };
        if (verts.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetColors(cols); mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds(); mesh.RecalculateTangents();
        _smoothCapMesh = mesh;
    }

    // footprint ของเกาะที่ขอบโค้งมน ที่ความละเอียด 2 เท่าของ sub-cell (ให้ฐานใต้เกาะไม่เห็นเป็นขั้นบันได) สุ่ม density แบบ bilinear
    private void BuildSmoothFootprint(float[] blurred, int W, int H, int x0, int z0, int res)
    {
        // ละเอียดขึ้น 2 เท่าถ้าเกาะไม่ใหญ่เกินไป (ใต้เกาะแบบ Cube สร้าง quad ต่อช่อง จำนวนช่องโตตามกำลังสอง)
        int fineRes = _cells.Count * (res * 2) * (res * 2) <= 60000 ? res * 2 : res;
        float sub = stepSize / res, fine = stepSize / fineRes, half = stepSize * 0.5f;
        _smoothFootprintSize = fine;
        _smoothFootprintShift = -half + fine * 0.5f; // ให้ center ของช่อง index i อยู่ที่ -half + (i + 0.5) * fine

        int f = fineRes / res;
        int gx0 = x0 * f, gz0 = z0 * f, gw = W * f, gh = H * f;
        for (int gz = 0; gz < gh; gz++)
            for (int gx = 0; gx < gw; gx++)
            {
                float wx = -half + (gx + gx0 + 0.5f) * fine, wz = -half + (gz + gz0 + 0.5f) * fine;
                float u = Mathf.Clamp((wx + half) / sub - 0.5f - x0, 0f, W - 1.001f);
                float v = Mathf.Clamp((wz + half) / sub - 0.5f - z0, 0f, H - 1.001f);
                int ix = (int)u, iz = (int)v;
                float fx = u - ix, fz = v - iz;
                int i = ix + iz * W;
                float d = Mathf.Lerp(Mathf.Lerp(blurred[i], blurred[i + 1], fx), Mathf.Lerp(blurred[i + W], blurred[i + W + 1], fx), fz);
                if (d >= 0.5f) _smoothFootprint.Add(new Vector2Int(gx + gx0, gz + gz0));
            }
    }

    // กำแพงล่องหนตามขอบเกาะโค้งมน: ต่อเส้นขอบเป็นสายแล้วรวมช่วงที่เกือบตรงเป็น BoxCollider อันเดียว วางเยื้องออกนอกเกาะ
    private GameObject BuildSmoothBoundary(Transform root, float topY, float bottomY, float thickness)
    {
        if (_smoothOutline.Count == 0) return null;

        var obj = new GameObject("Map Boundary");
        obj.transform.SetParent(root, false);

        // จุดปลายของแต่ละ segment (ปัดเป็นจำนวนเต็มกันความคลาดเคลื่อน float) -> segment ที่เริ่มตรงนั้น
        Vector2Int Key(Vector2 p) => new Vector2Int(Mathf.RoundToInt(p.x * 200f), Mathf.RoundToInt(p.y * 200f));
        var byStart = new Dictionary<Vector2Int, int>();
        for (int i = 0; i < _smoothOutline.Count; i++) byStart[Key(_smoothOutline[i].a)] = i;

        var used = new bool[_smoothOutline.Count];
        var pts = new List<Vector2>();
        for (int s = 0; s < _smoothOutline.Count; s++)
        {
            if (used[s]) continue;

            // ไล่ตามสายจนกลับมาจุดเริ่มหรือขาด
            pts.Clear();
            int cur = s;
            pts.Add(_smoothOutline[cur].a);
            while (cur >= 0 && !used[cur])
            {
                used[cur] = true;
                pts.Add(_smoothOutline[cur].b);
                cur = byStart.TryGetValue(Key(_smoothOutline[cur].b), out int next) ? next : -1;
            }
            AddBoundaryRuns(obj.transform, pts, topY, bottomY, thickness);
        }
        return obj;
    }

    private static void AddBoundaryRuns(Transform parent, List<Vector2> pts, float topY, float bottomY, float thickness)
    {
        const float maxTurnDeg = 12f, maxRun = 8f;
        int start = 0;
        while (start < pts.Count - 1)
        {
            int end = start + 1;
            Vector2 runDir = (pts[end] - pts[start]).normalized;
            while (end < pts.Count - 1)
            {
                Vector2 nextDir = (pts[end + 1] - pts[end]).normalized;
                Vector2 total = pts[end + 1] - pts[start];
                if (Vector2.Angle(runDir, nextDir) > maxTurnDeg || Vector2.Angle(runDir, total) > maxTurnDeg || total.magnitude > maxRun) break;
                end++;
            }

            Vector2 a = pts[start], b = pts[end], d = b - a;
            float len = d.magnitude;
            if (len > 1e-4f)
            {
                Vector2 dir = d / len;
                var outward = new Vector2(dir.y, -dir.x); // เนื้อเกาะอยู่ซ้าย -> ด้านนอกอยู่ขวา
                Vector2 mid = (a + b) * 0.5f + outward * (thickness * 0.5f);

                var go = new GameObject("Boundary Wall");
                go.transform.SetParent(parent, false);
                go.transform.position = new Vector3(mid.x, 0f, mid.y);
                go.transform.rotation = Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.y), Vector3.up);
                var box = go.AddComponent<BoxCollider>();
                // ยาวเกินปลายละ thickness/2 ให้ซ้อนกับกำแพงข้างๆ ตรงมุมโค้งไม่มีช่องโหว่
                box.size = new Vector3(thickness, topY - bottomY, len + thickness);
                box.center = new Vector3(0f, (topY + bottomY) * 0.5f, 0f);
            }
            start = end;
        }
    }

    private void AddSmoothPolygon(List<SmoothPoint> poly, Vector2Int chunkCell)
    {
        MeshLists lists = GetChunk(_groundChunks, ChunkOf(chunkCell));
        int baseIndex = lists.vertices.Count;
        foreach (var p in poly)
        {
            lists.vertices.Add(new Vector3(p.xz.x, Terrace.SmoothHeightAt(p.xz.x, p.xz.y), p.xz.y));
            lists.uvs.Add(new Vector2(p.xz.x / textureWorldSize, p.xz.y / textureWorldSize));
            lists.normals.Add(Terrace.SmoothNormalAt(p.xz.x, p.xz.y));
        }

        for (int i = 1; i < poly.Count - 1; i++)
        {
            int a = baseIndex, b = baseIndex + i, c = baseIndex + i + 1;
            // ต้องหันหน้าขึ้น (cross.y > 0 ตามระบบ clockwise ของ Unity) ไม่งั้นสลับ
            Vector3 va = lists.vertices[a], vb = lists.vertices[b], vc = lists.vertices[c];
            if (Vector3.Cross(vb - va, vc - va).y < 0f) (b, c) = (c, b);
            lists.triangles.Add(a); lists.triangles.Add(b); lists.triangles.Add(c);
        }
    }

    // ผนังหินใต้ขอบเกาะ: ส่วนของ polygon ที่ปิดด้วยเส้นขอบจริง (จุดตัดขาออก -> ขาเข้า) ที่ติดด้านนอกเกาะ
    private void AddSmoothSkirts(List<SmoothPoint> poly, Vector2Int chunkCell)
    {
        // ฐานแบบ Cube: ผนังเดียวกับขอบโค้งลงไปถึงก้นเลย (ไม่ใช้ผนังของ underside ที่เป็นขั้นบันไดตาม sub-cell) ไม่งั้นลงแค่ -0.1 ต่อกับ underside
        bool cubeBase = SmoothCubeBase;
        float bottomY = cubeBase ? -SmoothCubeDepth : -0.1f;
        for (int i = 0; i < poly.Count; i++)
        {
            SmoothPoint e = poly[i], n = poly[(i + 1) % poly.Count];
            if (e.kind != 1 || n.kind != 2 || !e.outer || !n.outer) continue;

            Vector2 dir = n.xz - e.xz;
            if (dir.sqrMagnitude < 1e-8f) continue;
            _smoothOutline.Add((e.xz, n.xz)); // เก็บไว้สร้างกำแพงล่องหนตามขอบโค้ง (เนื้อเกาะอยู่ซ้ายของเส้น)

            float hp = Terrace.SmoothHeightAt(e.xz.x, e.xz.y), hq = Terrace.SmoothHeightAt(n.xz.x, n.xz.y);
            if (!cubeBase && !CurvedRim && hp <= 0.001f && hq <= 0.001f) continue;
            // polygon วนทวนเข็ม เนื้อเกาะอยู่ซ้ายของเส้น -> นอกเกาะอยู่ขวา
            var outward = new Vector3(dir.y, 0f, -dir.x).normalized;

            MeshLists lists = GetChunk(_cliffChunks, ChunkOf(chunkCell));
            if (CurvedRim)
            {
                AddRimQuad(lists, new Vector3(e.xz.x, hp, e.xz.y), new Vector3(n.xz.x, hq, n.xz.y),
                    RimBottom(e.xz, hp), RimBottom(n.xz, hq), outward);
                continue;
            }
            AddCliffQuad(lists.vertices, lists.uvs, lists.triangles,
                new Vector3(e.xz.x, hp, e.xz.y), new Vector3(n.xz.x, hq, n.xz.y),
                new Vector3(e.xz.x, Mathf.Min(bottomY, hp), e.xz.y), new Vector3(n.xz.x, Mathf.Min(bottomY, hq), n.xz.y),
                outward);
        }
    }
}

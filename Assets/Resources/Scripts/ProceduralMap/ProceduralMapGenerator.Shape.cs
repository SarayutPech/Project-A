using UnityEngine;
using System.Collections.Generic;

// Phase 1: รูปทรงแผนที่ (cell, ทำขอบ, เชื่อมเกาะ, รู/บ่อ) + query ความสูงพื้น (partial ของ ProceduralMapGenerator ไฟล์หลัก = ProceduralMapGenerator.cs)
public partial class ProceduralMapGenerator
{
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

    // cell ที่แต่ละบ่อทับ (รวมขอบบ่อที่บานสุด + กำแพงรอบบ่อ) ส่งให้ TerraceLayout บังคับเป็นชั้นเดียว ไม่มีทางลาด
    // -> ก้นบ่อ ผิวน้ำ ผนังบ่อ อิงความสูงเดียวได้เสมอ
    private List<List<Vector2Int>> HoleFootprints()
    {
        var footprints = new List<List<Vector2Int>>();
        if (!addNoiseHoles) return footprints;

        foreach (var hole in _holes)
        {
            float r = hole.radius * (1f + holeEdgeJitter) + holeBlockerThickness + 0.1f;
            var min = WorldToCell(new Vector3(hole.center.x - r, 0f, hole.center.y - r));
            var max = WorldToCell(new Vector3(hole.center.x + r, 0f, hole.center.y + r));

            var cells = new List<Vector2Int>();
            for (int x = min.x; x <= max.x; x++)
                for (int z = min.y; z <= max.y; z++)
                    cells.Add(new Vector2Int(x, z));
            footprints.Add(cells);
        }
        return footprints;
    }

    // ขอบรูที่บานสุด (รวม jitter) เข้ามาใกล้จุดนี้เกิน clearance
    private bool HoleTooClose(HoleCircle hole, Vector2 point)
    {
        float maxRadius = hole.radius * (1f + holeEdgeJitter);
        return Vector2.Distance(hole.center, point) < maxRadius + holeClearanceAroundStartEnd;
    }

    // ความสูงผิวที่ยืน/วางของได้ ณ ตำแหน่งนี้: พื้น = ความสูงชั้น/ทางลาด / ในบ่อ = ผิวน้ำ / รูทะลุ = ไม่มีพื้น ใช้ระดับพื้นรอบรู
    public float GetSurfaceHeight(Vector3 worldPos)
    {
        float rootY = GeneratedRoot != null ? GeneratedRoot.transform.position.y : 0f;
        bool inPond = addNoiseHoles && holesAsPonds && pitDepth > 0f && IsInsideHole(worldPos);
        return rootY + Terrace.HeightAtWorld(worldPos) + (inPond ? WaterY : 0f);
    }

    private Vector3 SurfacePoint(Vector3 worldPos)
    {
        worldPos.y = GetSurfaceHeight(worldPos);
        return worldPos;
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
}

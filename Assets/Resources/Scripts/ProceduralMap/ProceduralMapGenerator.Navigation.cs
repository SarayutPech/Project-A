using UnityEngine;
using System.Collections.Generic;

// Navigation graph ของ AI + แปลง cell <-> world (partial ของ ProceduralMapGenerator ไฟล์หลัก = ProceduralMapGenerator.cs)
public partial class ProceduralMapGenerator
{
    // ---------- Navigation Graph (AI) ----------

    // พื้นของแผนที่ในมุมมองของ MapNavGraph (ความสูงชั้น/ทางลาด + บ่อ)
    private class NavSurface : MapNavGraph.ISurface
    {
        public ProceduralMapGenerator gen;

        public float HeightAt(Vector3 worldPos) => gen.GetSurfaceHeight(worldPos);
        public bool IsWater(Vector3 worldPos) => gen.addNoiseHoles && gen.IsInsideHole(worldPos);

        public bool SameSurface(Vector2Int cellA, Vector2Int cellB, Vector3 boundaryPoint)
        {
            Vector3 a = gen.CellToWorld(cellA), b = gen.CellToWorld(cellB);
            float ha = gen.Terrace.HeightAt(cellA, new Vector2(boundaryPoint.x - a.x, boundaryPoint.z - a.z));
            float hb = gen.Terrace.HeightAt(cellB, new Vector2(boundaryPoint.x - b.x, boundaryPoint.z - b.z));
            return Mathf.Abs(ha - hb) < 0.05f;
        }
    }

    private MapNavGraph BuildNavGraph()
    {
        var cfg = Config;
        int res = Mathf.Max(1, cfg.navNodesPerCell);
        float spacing = stepSize / res;
        float half = stepSize * 0.5f;
        Vector2Int ToGrid(Vector3 p) => new Vector2Int(Mathf.FloorToInt((p.x + half) / spacing), Mathf.FloorToInt((p.z + half) / spacing));

        // สิ่งกีดขวาง = collider ตันของ object ที่ scatter (ต้นไม้/หิน) ระบายลงตาราง node ครั้งเดียว ไม่ต้องเช็คทีละคู่
        var blocked = new HashSet<Vector2Int>();
        if (ScatterContainer != null)
        {
            Physics.SyncTransforms(); // collider เพิ่งสร้าง/ปรับ scale ในเฟรมนี้ bounds ต้อง sync ก่อน
            float pad = cfg.navObstaclePadding;
            foreach (var col in ScatterContainer.GetComponentsInChildren<Collider>())
            {
                if (!col.enabled || col.isTrigger) continue;
                Bounds b = col.bounds;
                Vector2Int min = ToGrid(new Vector3(b.min.x - pad, 0f, b.min.z - pad));
                Vector2Int max = ToGrid(new Vector3(b.max.x + pad, 0f, b.max.z + pad));
                for (int x = min.x; x <= max.x; x++)
                    for (int z = min.y; z <= max.y; z++)
                        blocked.Add(new Vector2Int(x, z));
            }
        }

        // เรียง cell ให้ลำดับ node เหมือนกันทุกเครื่อง (HashSet ไม่รับประกันลำดับ)
        var sorted = new List<Vector2Int>(_cells);
        sorted.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));

        // บ่อ: walkable water = ลุยได้ / ไม่งั้น (มีกำแพงกั้นรอบบ่อ หรือเป็นรูทะลุ) ตัด node ทิ้ง
        bool holesWalkable = holesAsPonds && walkableWater;

        return MapNavGraph.Build(sorted, new NavSurface { gen = this }, holesWalkable,
            p => blocked.Contains(ToGrid(p)),
            new MapNavGraph.Settings
            {
                stepSize = stepSize,
                nodesPerCell = res,
                maxJumpUp = cfg.navMaxJumpUp,
                maxJumpDown = cfg.navMaxJumpDown,
                waterCost = cfg.navWaterCost,
                jumpCost = cfg.navJumpCost,
            });
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

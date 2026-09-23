using System.Collections.Generic;
using UnityEngine;

// กำแพงล่องหน (BoxCollider) ล้อมรอบกลุ่ม cell แยกออกมาจาก ProceduralMapGenerator
// ใช้ทั้งกำแพงขอบ map (กันตก) และกำแพงรอบบ่อน้ำ/รู (กันเดินลงไป)
// ขอบ cell ที่อยู่แนวเดียวกันและต่อกันจะถูกรวมเป็น BoxCollider ยาวอันเดียว ลดจำนวน collider
public static class MapBoundaryBuilder
{
    private static readonly Vector2Int[] Directions4 =
    {
        new Vector2Int(1, 0), new Vector2Int(-1, 0),
        new Vector2Int(0, 1), new Vector2Int(0, -1),
    };

    public struct Settings
    {
        public string name;
        public float cellSize;
        public Vector2 origin;   // ตำแหน่งกลาง cell (0,0) บนแกน X/Z
        public float bottomY;
        public float topY;
        public float thickness;  // กำแพงอยู่นอกกลุ่ม cell ทั้งหมด (ฝั่งที่ไม่มี cell)
    }

    // กำแพงขอบ map: cell ของ generator (กลาง cell (0,0) อยู่ที่ world origin)
    public static GameObject Build(HashSet<Vector2Int> cells, float stepSize, float height, float thickness, Transform parent)
    {
        return Build(cells, new Settings
        {
            name = "Map Boundary",
            cellSize = stepSize,
            origin = Vector2.zero,
            bottomY = -1f, // ยื่นลงใต้ผิวพื้นนิดหน่อย กันลอดใต้กำแพง
            topY = height,
            thickness = thickness,
        }, parent);
    }

    public static GameObject Build(HashSet<Vector2Int> cells, Settings s, Transform parent)
    {
        if (cells == null || cells.Count == 0) return null;

        var obj = new GameObject(s.name);
        obj.transform.SetParent(parent, false);

        foreach (var dir in Directions4)
            AddWallsForDirection(obj, cells, dir, s);

        return obj;
    }

    private static void AddWallsForDirection(GameObject obj, HashSet<Vector2Int> cells, Vector2Int dir, Settings s)
    {
        bool alongZ = dir.x != 0; // ผนังด้าน ±X วางยาวตามแกน Z / ผนังด้าน ±Z วางยาวตามแกน X

        // จัดกลุ่มขอบที่เปิดออกนอกกลุ่ม cell ตามแนวเส้น (line) แล้วเรียงตามแกนที่ผนังวางยาว
        var lines = new Dictionary<int, List<int>>();
        foreach (var c in cells)
        {
            if (cells.Contains(c + dir)) continue;

            int line = alongZ ? c.x : c.y;
            int along = alongZ ? c.y : c.x;
            if (!lines.TryGetValue(line, out var list))
            {
                list = new List<int>();
                lines[line] = list;
            }
            list.Add(along);
        }

        float outward = s.cellSize * 0.5f + s.thickness * 0.5f;
        float centerY = (s.topY + s.bottomY) * 0.5f;
        float sizeY = s.topY - s.bottomY;
        float lineOrigin = alongZ ? s.origin.x : s.origin.y;
        float alongOrigin = alongZ ? s.origin.y : s.origin.x;

        foreach (var kv in lines)
        {
            List<int> alongs = kv.Value;
            alongs.Sort();

            int runStart = alongs[0];
            for (int i = 1; i <= alongs.Count; i++)
            {
                if (i < alongs.Count && alongs[i] == alongs[i - 1] + 1) continue;

                int runEnd = alongs[i - 1];
                float mid = alongOrigin + (runStart + runEnd) * 0.5f * s.cellSize;
                float length = (runEnd - runStart + 1) * s.cellSize;
                float linePos = lineOrigin + kv.Key * s.cellSize + (alongZ ? dir.x : dir.y) * outward;

                var box = obj.AddComponent<BoxCollider>();
                box.center = alongZ ? new Vector3(linePos, centerY, mid) : new Vector3(mid, centerY, linePos);
                box.size = alongZ ? new Vector3(s.thickness, sizeY, length) : new Vector3(length, sizeY, s.thickness);

                if (i < alongs.Count) runStart = alongs[i];
            }
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

// ความสูงแบบขั้นบันได (terrace) ของแผนที่: แบ่ง cell เป็นภูมิภาค แต่ละภูมิภาคมี level (ชั้น) ของตัวเอง
// ภูมิภาคติดกันต่างกันไม่เกิน 1 ชั้น และทุกภูมิภาคเดินถึงจากจุด Start ได้ผ่านทางลาด (ramp) ยาว 1 cell
// ขอบที่ต่างชั้นกันโดยไม่มีทางลาด = หน้าผา (generator สร้างผนัง + collider ให้)
//
// เป็น logic ล้วน ไม่พึ่ง Unity object / UnityEngine.Random ใช้ System.Random จาก seed ของตัวเอง
// -> สร้างซ้ำได้เหมือนเดิมจาก (config + seed) และรันบน server ได้ (กฎข้อ 5 ใน CLAUDE.md)
public class TerraceLayout
{
    public struct Settings
    {
        public float stepSize;
        public int levels;         // จำนวนชั้นทั้งหมด (1 = พื้นเรียบ)
        public float levelHeight;  // ความสูงต่อชั้น (world unit)
        public int regionSize;     // จำนวน cell โดยประมาณต่อ 1 ภูมิภาค
        public float flatChance;   // โอกาสที่ภูมิภาคถัดไปอยู่ชั้นเดียวกัน
    }

    public struct Ramp
    {
        public Vector2Int cell;
        public Vector2Int dir;  // ชี้จากฝั่งต่ำไปฝั่งสูง
        public int lowLevel;
    }

    private static readonly Vector2Int[] Directions4 =
    {
        new Vector2Int(1, 0), new Vector2Int(-1, 0),
        new Vector2Int(0, 1), new Vector2Int(0, -1),
    };

    private readonly Dictionary<Vector2Int, int> _levels = new Dictionary<Vector2Int, int>();
    private readonly Dictionary<Vector2Int, Ramp> _ramps = new Dictionary<Vector2Int, Ramp>();
    private readonly HashSet<Vector2Int> _rampAccess = new HashSet<Vector2Int>(); // ramp + ปากทางลาดทั้งสองฝั่ง ห้ามวางของ/บ่อ
    private float _stepSize;
    private float _levelHeight;

    public float MaxHeight { get; private set; }
    public IReadOnlyDictionary<Vector2Int, Ramp> Ramps => _ramps;

    // พื้นเรียบทั้งแผนที่ (ปิด terraces)
    public static TerraceLayout Flat(float stepSize) => new TerraceLayout { _stepSize = stepSize };

    // flatGroups = กลุ่ม cell ที่ต้องอยู่ชั้นเดียวกันและห้ามมีทางลาด (เช่นพื้นที่ใต้บ่อ/ทะเลสาบ) กลุ่มละ 1 list
    public static TerraceLayout Build(HashSet<Vector2Int> cells, Vector2Int startCell, Vector2Int endCell, Settings s, int seed,
        List<List<Vector2Int>> flatGroups = null)
    {
        var layout = new TerraceLayout { _stepSize = s.stepSize, _levelHeight = s.levelHeight };
        if (cells.Count == 0 || s.levels <= 1 || s.levelHeight <= 0f) return layout;

        var rng = new System.Random(seed);

        // เรียงก่อนเสมอ ลำดับวน HashSet ไม่รับประกันว่าตรงกันทุกเครื่อง
        var sorted = new List<Vector2Int>(cells);
        sorted.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));

        // cell -> กลุ่มที่ต้องอยู่ภูมิภาคเดียวกัน (กลุ่มที่ทับกันรวมเป็นกลุ่มเดียว)
        Dictionary<Vector2Int, List<Vector2Int>> groupOf = MergeFlatGroups(cells, flatGroups);

        Dictionary<Vector2Int, int> regionOf = AssignRegions(sorted, cells, groupOf, s.regionSize, rng, out int regionCount);

        // cell ของแต่ละภูมิภาค + ภูมิภาคที่ติดกัน (เรียงตาม id ให้ลำดับคงที่)
        var regionCells = new List<List<Vector2Int>>();
        var neighbors = new List<SortedSet<int>>();
        for (int i = 0; i < regionCount; i++) { regionCells.Add(new List<Vector2Int>()); neighbors.Add(new SortedSet<int>()); }
        foreach (var c in sorted)
        {
            int r = regionOf[c];
            regionCells[r].Add(c);
            foreach (var d in Directions4)
                if (regionOf.TryGetValue(c + d, out int nr) && nr != r) neighbors[r].Add(nr);
        }

        // ไล่กำหนดชั้นแบบ BFS จากภูมิภาคของจุด Start ภูมิภาคลูกต่างจากแม่ได้ไม่เกิน 1 ชั้น
        // และต้องวางทางลาดเชื่อมได้ ไม่งั้นให้อยู่ชั้นเดียวกับแม่ -> เดินถึงทุกที่เสมอ
        var regionLevel = new int[regionCount];
        var assigned = new bool[regionCount];
        var queue = new Queue<int>();
        int startRegion = regionOf[startCell];
        regionLevel[startRegion] = 0;
        assigned[startRegion] = true;
        queue.Enqueue(startRegion);

        var blocked = new HashSet<Vector2Int> { startCell, endCell };
        blocked.UnionWith(groupOf.Keys); // ห้ามทางลาด/ปากทางลาดอยู่ใต้บ่อ
        int maxLevel = s.levels - 1;

        while (queue.Count > 0)
        {
            int parent = queue.Dequeue();
            foreach (int child in neighbors[parent])
            {
                if (assigned[child]) continue;
                assigned[child] = true;
                queue.Enqueue(child);

                int pl = regionLevel[parent];
                int level = pl;
                if (rng.NextDouble() >= s.flatChance)
                {
                    bool canUp = pl < maxLevel, canDown = pl > 0;
                    if (canUp && canDown) level = rng.Next(2) == 0 ? pl + 1 : pl - 1;
                    else if (canUp) level = pl + 1;
                    else if (canDown) level = pl - 1;
                }

                if (level != pl)
                {
                    int low = level < pl ? child : parent;
                    int high = level < pl ? parent : child;
                    if (!layout.TryPlaceRamp(regionCells[high], regionOf, low, high, Mathf.Min(level, pl), blocked, rng))
                        level = pl;
                }
                regionLevel[child] = level;
            }
        }

        foreach (var c in sorted)
            layout._levels[c] = regionLevel[regionOf[c]];

        // FixUnreachable อาจเปลี่ยนชั้นของปากทางลาด -> ซ่อม ramp แล้ววนเช็คใหม่จนนิ่ง
        for (int i = 0; i < 8; i++)
        {
            layout.FixUnreachable(sorted, startCell, groupOf);
            if (!layout.RepairRamps()) break;
        }

        foreach (var level in layout._levels.Values)
            layout.MaxHeight = Mathf.Max(layout.MaxHeight, level * s.levelHeight);
        return layout;
    }

    // กันหลุด: ภูมิภาคที่ไม่ต่อกันเป็นก้อน (เช่นกลุ่มใต้บ่อที่คร่อมช่องว่าง) อาจเหลือ cell ที่ไม่มีทางลาดขึ้นถึง
    // ไล่ BFS จาก Start (ข้าม cell ได้ถ้าความสูงขอบร่วมเท่ากัน) แล้วดึง cell ที่ไปไม่ถึงให้เท่าชั้นเพื่อนบ้านที่ไปถึงแล้ว
    // cell ในกลุ่มบ่อ -> ย้ายทั้งกลุ่มพร้อมกัน ให้บ่อยังอยู่ชั้นเดียว
    private void FixUnreachable(List<Vector2Int> sorted, Vector2Int startCell, Dictionary<Vector2Int, List<Vector2Int>> groupOf)
    {
        var reached = new HashSet<Vector2Int>();
        var queue = new Queue<Vector2Int>();

        void Flood(Vector2Int from)
        {
            if (!reached.Add(from)) return;
            queue.Enqueue(from);
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                foreach (var d in Directions4)
                {
                    var n = c + d;
                    if (reached.Contains(n) || !_levels.ContainsKey(n) || !EdgeMatches(c, d)) continue;
                    reached.Add(n);
                    queue.Enqueue(n);
                }
            }
        }

        Flood(startCell);
        for (int guard = 0; guard < sorted.Count; guard++)
        {
            bool changed = false;
            foreach (var c in sorted)
            {
                if (reached.Contains(c) || _ramps.ContainsKey(c)) continue;
                foreach (var d in Directions4)
                {
                    var n = c + d;
                    if (!reached.Contains(n)) continue;

                    // เพื่อนบ้านเป็นทางลาด: ต่อได้เฉพาะฝั่งหัวลาด/ตีนลาด (ข้างลาดความสูงไม่คงที่ ต่อไม่ได้)
                    int level;
                    if (_ramps.TryGetValue(n, out Ramp ramp))
                    {
                        if (d == ramp.dir) level = ramp.lowLevel;           // c อยู่ตีนลาด
                        else if (d == -ramp.dir) level = ramp.lowLevel + 1; // c อยู่หัวลาด
                        else continue;
                    }
                    else level = _levels[n];
                    if (groupOf.TryGetValue(c, out var group))
                        foreach (var gc in group) _levels[gc] = level;
                    else
                        _levels[c] = level;

                    Flood(c);
                    changed = true;
                    break;
                }
            }
            if (!changed) break;
        }
    }

    // ทางลาดที่ชั้นตีนลาด/หัวลาดไม่ตรงกับที่ตั้งไว้แล้ว: กลับทิศถ้ายังต่างกัน 1 ชั้น / ต่างกันแบบอื่นเปลี่ยนเป็นพื้นเรียบ
    private bool RepairRamps()
    {
        bool changed = false;
        var keys = new List<Vector2Int>(_ramps.Keys);
        keys.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));

        foreach (var cell in keys)
        {
            Ramp r = _ramps[cell];
            int bottom = GetLevel(cell - r.dir), top = GetLevel(cell + r.dir);
            if (bottom == r.lowLevel && top == r.lowLevel + 1) continue;

            changed = true;
            if (top == bottom - 1)
            {
                _ramps[cell] = new Ramp { cell = cell, dir = -r.dir, lowLevel = top };
                _levels[cell] = bottom;
            }
            else
            {
                _ramps.Remove(cell);
                _levels[cell] = bottom;
            }
        }
        return changed;
    }

    // เดินข้ามขอบระหว่าง cell กับ cell+dir ได้ไหม (ความสูงกลางขอบของทั้งสองฝั่งเท่ากัน)
    private bool EdgeMatches(Vector2Int cell, Vector2Int dir)
    {
        Vector2 mid = (Vector2)dir * (_stepSize * 0.5f);
        float a = HeightAt(cell, mid);
        float b = HeightAt(cell + dir, mid - (Vector2)dir * _stepSize);
        return Mathf.Abs(a - b) < 0.001f;
    }

    // รวมกลุ่มที่มี cell ซ้ำกันเป็นกลุ่มเดียว (union-find แบบง่าย) ตัด cell ที่ไม่อยู่ในแผนที่ออก
    private static Dictionary<Vector2Int, List<Vector2Int>> MergeFlatGroups(HashSet<Vector2Int> cells, List<List<Vector2Int>> flatGroups)
    {
        var groupOf = new Dictionary<Vector2Int, List<Vector2Int>>();
        if (flatGroups == null) return groupOf;

        foreach (var group in flatGroups)
        {
            var merged = new List<Vector2Int>();
            foreach (var c in group)
            {
                if (!cells.Contains(c)) continue;
                if (groupOf.TryGetValue(c, out var other))
                {
                    if (other == merged) continue;
                    foreach (var oc in other) groupOf[oc] = merged;
                    merged.AddRange(other);
                }
                else
                {
                    groupOf[c] = merged;
                    merged.Add(c);
                }
            }
        }
        return groupOf;
    }

    // multi-source BFS (4 ทิศ) จาก cell สุ่มเป็นจุดตั้งต้น -> ภูมิภาคต่อกันเป็นก้อนเสมอ
    // cell ที่ต่อกับที่อื่นแค่แนวทแยง (BFS 4 ทิศไปไม่ถึง) จะกลายเป็นภูมิภาคของตัวเอง
    // cell ใน flat group: พอ BFS ไปถึง cell ไหนของกลุ่ม ทั้งกลุ่มเข้าภูมิภาคนั้นทันที
    private static Dictionary<Vector2Int, int> AssignRegions(List<Vector2Int> sorted, HashSet<Vector2Int> cells,
        Dictionary<Vector2Int, List<Vector2Int>> groupOf, int regionSize, System.Random rng, out int regionCount)
    {
        var regionOf = new Dictionary<Vector2Int, int>();
        var queue = new Queue<Vector2Int>();
        regionCount = 0;

        void Claim(Vector2Int c, int region)
        {
            if (groupOf.TryGetValue(c, out var group))
            {
                foreach (var gc in group)
                {
                    if (regionOf.ContainsKey(gc)) continue;
                    regionOf[gc] = region;
                    queue.Enqueue(gc);
                }
            }
            else
            {
                regionOf[c] = region;
                queue.Enqueue(c);
            }
        }

        int seedCount = Mathf.Max(1, Mathf.RoundToInt(sorted.Count / (float)Mathf.Max(1, regionSize)));
        var shuffled = new List<Vector2Int>(sorted);
        for (int i = shuffled.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }
        for (int i = 0; i < seedCount && i < shuffled.Count; i++)
        {
            if (regionOf.ContainsKey(shuffled[i])) continue; // อยู่ในกลุ่มที่ seed อื่นเอาไปแล้ว
            Claim(shuffled[i], regionCount++);
        }

        while (true)
        {
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                foreach (var d in Directions4)
                {
                    var n = c + d;
                    if (!cells.Contains(n) || regionOf.ContainsKey(n)) continue;
                    Claim(n, regionOf[c]);
                }
            }

            int leftover = sorted.FindIndex(c => !regionOf.ContainsKey(c));
            if (leftover < 0) break;
            Claim(sorted[leftover], regionCount++);
        }
        return regionOf;
    }

    // หา cell ในภูมิภาคสูงที่ติดภูมิภาคต่ำ แล้วทำเป็นทางลาด: ต้องมีพื้นชั้นบนต่อจากหัวลาด และไม่ชน ramp อื่น
    private bool TryPlaceRamp(List<Vector2Int> highCells, Dictionary<Vector2Int, int> regionOf,
        int lowRegion, int highRegion, int lowLevel, HashSet<Vector2Int> blocked, System.Random rng)
    {
        var candidates = new List<Ramp>();
        foreach (var b in highCells)
        {
            if (blocked.Contains(b) || _rampAccess.Contains(b)) continue;
            foreach (var d in Directions4)
            {
                Vector2Int a = b - d;    // ตีนลาด (ชั้นต่ำ)
                Vector2Int top = b + d;  // หัวลาด (ชั้นสูง)
                if (!regionOf.TryGetValue(a, out int ar) || ar != lowRegion) continue;
                if (!regionOf.TryGetValue(top, out int tr) || tr != highRegion) continue;
                if (_rampAccess.Contains(a) || _rampAccess.Contains(top)) continue;

                // ข้างทางลาดห้ามเป็นทางลาดอีกอัน (ผนังข้างลาดจะซ้อนกัน)
                var side = new Vector2Int(d.y, d.x);
                if (_ramps.ContainsKey(b + side) || _ramps.ContainsKey(b - side)) continue;
                // cell ข้างลาดที่มีทางออกทางเดียวคือตัวลาด จะกลายเป็นซอกที่เดินเข้าไม่ได้
                if (IsDeadEndBeside(b + side, b, regionOf) || IsDeadEndBeside(b - side, b, regionOf)) continue;

                candidates.Add(new Ramp { cell = b, dir = d, lowLevel = lowLevel });
            }
        }
        if (candidates.Count == 0) return false;

        Ramp ramp = candidates[rng.Next(candidates.Count)];
        _ramps[ramp.cell] = ramp;
        _rampAccess.Add(ramp.cell);
        _rampAccess.Add(ramp.cell - ramp.dir);
        _rampAccess.Add(ramp.cell + ramp.dir);
        return true;
    }

    private static bool IsDeadEndBeside(Vector2Int cell, Vector2Int rampCell, Dictionary<Vector2Int, int> regionOf)
    {
        if (!regionOf.ContainsKey(cell)) return false;
        foreach (var d in Directions4)
        {
            var n = cell + d;
            if (n != rampCell && regionOf.ContainsKey(n)) return false;
        }
        return true;
    }

    // ---------- Query ----------

    public int GetLevel(Vector2Int cell) => _levels.TryGetValue(cell, out int l) ? l : 0;
    public bool IsRamp(Vector2Int cell) => _ramps.ContainsKey(cell);
    public bool IsRampAccess(Vector2Int cell) => _rampAccess.Contains(cell);
    public float BaseHeight(Vector2Int cell) => GetLevel(cell) * _levelHeight;

    // local = ตำแหน่งเทียบกับกลาง cell (x, z) อยู่ในช่วง ±stepSize/2
    public float HeightAt(Vector2Int cell, Vector2 local)
    {
        if (!_ramps.TryGetValue(cell, out Ramp ramp)) return BaseHeight(cell);

        float t = Mathf.Clamp01(Vector2.Dot(local, ramp.dir) / _stepSize + 0.5f);
        return (ramp.lowLevel + t) * _levelHeight;
    }

    public Vector2Int WorldToCell(Vector3 worldPos) =>
        new Vector2Int(Mathf.RoundToInt(worldPos.x / _stepSize), Mathf.RoundToInt(worldPos.z / _stepSize));

    public float HeightAtWorld(Vector3 worldPos)
    {
        Vector2Int cell = WorldToCell(worldPos);
        return HeightAt(cell, new Vector2(worldPos.x - cell.x * _stepSize, worldPos.z - cell.y * _stepSize));
    }
}

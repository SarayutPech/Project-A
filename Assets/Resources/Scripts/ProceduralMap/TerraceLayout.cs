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

    // ---------- Smooth slopes: height field ต่อเนื่องแทนขั้นบันได ----------
    // ความสูงเก็บเป็นตารางที่กลาง sub-cell (res x res ต่อ 1 cell) ได้จาก level ของ cell เฉลี่ยด้วย box blur ที่รัศมีแปรตามมุมลาดของแต่ละจุด
    // (มุมสุ่มในช่วง min..max จาก noise) แล้วบังคับความชันไม่เกิน max อีกรอบ cell ใน fixedCells (ใต้บ่อ) คงความสูงชั้นเดิมเป๊ะ
    public struct SmoothSettings
    {
        public int res;            // sub-cell ต่อด้านของ 1 cell
        public float minAngle;     // องศา: ลาดที่เกือบราบที่สุด
        public float maxAngle;     // องศา: ลาดชันที่สุด (เดินได้)
        public float noiseScale;   // ความถี่ noise (1/world unit) ที่สุ่มมุมลาดระหว่าง min..max
        public int pad;            // ขอบตารางที่เผื่อรอบ cell (sub-cell)
    }

    private float[] _sm;
    private int _smRes, _smX0, _smZ0, _smW, _smH;
    private float _smSub;

    public bool IsSmooth => _sm != null;
    public int SmoothRes => _smRes;

    public void BuildSmooth(HashSet<Vector2Int> cells, HashSet<Vector2Int> fixedCells, SmoothSettings s, int seed)
    {
        if (cells.Count == 0) return;
        int res = Mathf.Max(1, s.res);
        _smRes = res;
        _smSub = _stepSize / res;

        int minCx = int.MaxValue, minCz = int.MaxValue, maxCx = int.MinValue, maxCz = int.MinValue;
        foreach (var c in cells)
        {
            minCx = Mathf.Min(minCx, c.x); maxCx = Mathf.Max(maxCx, c.x);
            minCz = Mathf.Min(minCz, c.y); maxCz = Mathf.Max(maxCz, c.y);
        }
        int pad = Mathf.Max(1, s.pad);
        _smX0 = minCx * res - pad; _smZ0 = minCz * res - pad;
        _smW = (maxCx - minCx + 1) * res + pad * 2;
        _smH = (maxCz - minCz + 1) * res + pad * 2;
        int n = _smW * _smH;

        var occ = new bool[n];
        var fix = new bool[n];
        var stepped = new float[n];
        foreach (var c in cells)
        {
            float h = BaseHeight(c);
            bool isFixed = fixedCells != null && fixedCells.Contains(c);
            for (int sz = 0; sz < res; sz++)
                for (int sx = 0; sx < res; sx++)
                {
                    int i = (c.x * res + sx - _smX0) + (c.y * res + sz - _smZ0) * _smW;
                    occ[i] = true; fix[i] = isFixed; stepped[i] = h;
                }
        }

        // integral image ของ (ความสูง x occ) และ occ -> box average รัศมีใดก็ได้ใน O(1)
        int iw = _smW + 1;
        var sumH = new double[iw * (_smH + 1)];
        var sumO = new double[iw * (_smH + 1)];
        for (int z = 0; z < _smH; z++)
            for (int x = 0; x < _smW; x++)
            {
                int i = x + z * _smW, k = (x + 1) + (z + 1) * iw;
                double o = occ[i] ? 1.0 : 0.0, hh = occ[i] ? stepped[i] : 0.0;
                sumO[k] = o + sumO[k - 1] + sumO[k - iw] - sumO[k - iw - 1];
                sumH[k] = hh + sumH[k - 1] + sumH[k - iw] - sumH[k - iw - 1];
            }

        float minA = Mathf.Clamp(s.minAngle, 1f, 80f) * Mathf.Deg2Rad;
        float maxA = Mathf.Clamp(Mathf.Max(s.maxAngle, s.minAngle), 1f, 80f) * Mathf.Deg2Rad;
        var rng = new System.Random(seed ^ 0x51F0);
        float ox = (float)rng.NextDouble() * 1000f, oz = (float)rng.NextDouble() * 1000f;

        var h2 = new float[n];
        for (int z = 0; z < _smH; z++)
            for (int x = 0; x < _smW; x++)
            {
                int i = x + z * _smW;
                if (!occ[i]) continue;
                if (fix[i] || _levelHeight <= 0f) { h2[i] = stepped[i]; continue; }

                float wx = SubCenter(x + _smX0), wz = SubCenter(z + _smZ0);
                float t = ValueNoise(wx * s.noiseScale + ox, wz * s.noiseScale + oz);
                float angle = Mathf.Lerp(minA, maxA, t);
                // box blur กว้าง W เปลี่ยนขั้นสูง h เป็นลาดชัน h/W -> W = h / tan(angle)
                float width = _levelHeight / Mathf.Tan(angle);
                int r = Mathf.Max(0, Mathf.RoundToInt(width * 0.5f / _smSub));

                int x0 = Mathf.Max(0, x - r), x1 = Mathf.Min(_smW, x + r + 1);
                int z0 = Mathf.Max(0, z - r), z1 = Mathf.Min(_smH, z + r + 1);
                double o = sumO[x1 + z1 * iw] - sumO[x0 + z1 * iw] - sumO[x1 + z0 * iw] + sumO[x0 + z0 * iw];
                double hs = sumH[x1 + z1 * iw] - sumH[x0 + z1 * iw] - sumH[x1 + z0 * iw] + sumH[x0 + z0 * iw];
                h2[i] = o > 0.0 ? (float)(hs / o) : stepped[i];
            }

        // บังคับความชันไม่เกิน max (ไล่หน้า-หลังสลับกัน) cell ที่ fixed ไม่ขยับ เพื่อนบ้านปรับเข้าหามัน
        float lim4 = Mathf.Tan(maxA) * _smSub, lim8 = lim4 * 1.4142f;
        for (int iter = 0; iter < 8; iter++)
        {
            bool fwd = (iter & 1) == 0;
            for (int zz = 0; zz < _smH; zz++)
                for (int xx = 0; xx < _smW; xx++)
                {
                    int x = fwd ? xx : _smW - 1 - xx, z = fwd ? zz : _smH - 1 - zz;
                    int i = x + z * _smW;
                    if (!occ[i] || fix[i]) continue;
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dz == 0) continue;
                            int nx = x + dx, nz = z + dz;
                            if (nx < 0 || nz < 0 || nx >= _smW || nz >= _smH) continue;
                            int j = nx + nz * _smW;
                            if (!occ[j]) continue;
                            float lim = (dx == 0 || dz == 0) ? lim4 : lim8;
                            h2[i] = Mathf.Clamp(h2[i], h2[j] - lim, h2[j] + lim);
                        }
                }
        }

        // ขยายค่าออกไปนอก cell (ให้ขอบโค้งมน/ส่วนที่ยื่นออกมามีความสูงใช้ได้)
        var filled = (bool[])occ.Clone();
        var addIdx = new List<int>();
        var addVal = new List<float>();
        for (int iter = 0; iter < pad * 2 + 2; iter++)
        {
            addIdx.Clear(); addVal.Clear();
            for (int z = 0; z < _smH; z++)
                for (int x = 0; x < _smW; x++)
                {
                    int i = x + z * _smW;
                    if (filled[i]) continue;
                    float sum = 0f; int cnt = 0;
                    if (x > 0 && filled[i - 1]) { sum += h2[i - 1]; cnt++; }
                    if (x < _smW - 1 && filled[i + 1]) { sum += h2[i + 1]; cnt++; }
                    if (z > 0 && filled[i - _smW]) { sum += h2[i - _smW]; cnt++; }
                    if (z < _smH - 1 && filled[i + _smW]) { sum += h2[i + _smW]; cnt++; }
                    if (cnt > 0) { addIdx.Add(i); addVal.Add(sum / cnt); }
                }
            if (addIdx.Count == 0) break;
            for (int k = 0; k < addIdx.Count; k++) { h2[addIdx[k]] = addVal[k]; filled[addIdx[k]] = true; }
        }
        _sm = h2;
    }

    // world ของกลาง sub-cell ที่ index แบบ global g (แกนใดแกนหนึ่ง)
    private float SubCenter(int g) => -_stepSize * 0.5f + (g + 0.5f) * _smSub;

    // value noise 0..1 จาก hash ล้วน (ไม่พึ่ง UnityEngine.Random / Mathf.Perlin) -> ผลเหมือนกันทุกเครื่อง
    private static float ValueNoise(float x, float y)
    {
        int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
        float fx = x - ix, fy = y - iy;
        fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
        float a = HashTo01(ix, iy), b = HashTo01(ix + 1, iy), c = HashTo01(ix, iy + 1), d = HashTo01(ix + 1, iy + 1);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    public static float Noise01(float x, float y) => ValueNoise(x, y);

    private static float HashTo01(int x, int y)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }

    // ความสูงที่ตำแหน่ง world (bilinear ระหว่างกลาง sub-cell)
    public float SmoothHeightAt(float wx, float wz)
    {
        float u = (wx + _stepSize * 0.5f) / _smSub - 0.5f - _smX0;
        float v = (wz + _stepSize * 0.5f) / _smSub - 0.5f - _smZ0;
        u = Mathf.Clamp(u, 0f, _smW - 1.001f); v = Mathf.Clamp(v, 0f, _smH - 1.001f);
        int x = (int)u, z = (int)v;
        float fx = u - x, fz = v - z;
        int i = x + z * _smW;
        return Mathf.Lerp(Mathf.Lerp(_sm[i], _sm[i + 1], fx), Mathf.Lerp(_sm[i + _smW], _sm[i + _smW + 1], fx), fz);
    }

    // normal จากความชันของ height field (ให้ mesh ดูเนียน ไม่เป็นเหลี่ยม)
    public Vector3 SmoothNormalAt(float wx, float wz)
    {
        float e = _smSub * 0.5f;
        float dx = SmoothHeightAt(wx + e, wz) - SmoothHeightAt(wx - e, wz);
        float dz = SmoothHeightAt(wx, wz + e) - SmoothHeightAt(wx, wz - e);
        return new Vector3(-dx, 2f * e, -dz).normalized;
    }

    // ---------- Query ----------

    public int GetLevel(Vector2Int cell) => _levels.TryGetValue(cell, out int l) ? l : 0;
    public bool IsRamp(Vector2Int cell) => _sm == null && _ramps.ContainsKey(cell);
    public bool IsRampAccess(Vector2Int cell) => _sm == null && _rampAccess.Contains(cell);
    public float BaseHeight(Vector2Int cell) => GetLevel(cell) * _levelHeight;

    // local = ตำแหน่งเทียบกับกลาง cell (x, z) อยู่ในช่วง ±stepSize/2
    public float HeightAt(Vector2Int cell, Vector2 local)
    {
        if (_sm != null) return SmoothHeightAt(cell.x * _stepSize + local.x, cell.y * _stepSize + local.y);
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

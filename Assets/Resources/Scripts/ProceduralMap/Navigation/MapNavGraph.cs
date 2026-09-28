using System.Collections.Generic;
using UnityEngine;

// ประเภทการเดินทางระหว่าง node ข้างเคียง
public enum NavEdge : byte
{
    None = 0,
    Walk = 1,      // พื้นต่อเนื่อง (พื้นเรียบ/ทางลาด)
    JumpUp = 2,    // หน้าผาสูงขึ้น ต้องกระโดดขึ้น
    JumpDown = 3,  // หน้าผาต่ำลง กระโดด/ก้าวลง
}

// จุดบนเส้นทาง: edge = วิธีที่ต้องใช้ "เดินทางมาถึง" จุดนี้จากจุดก่อนหน้า
public struct NavWaypoint
{
    public Vector3 position;
    public NavEdge edge;
}

// กราฟนำทางของ AI บนแผนที่ procedural (grid graph: 1 cell ของแผนที่แบ่งเป็น nodesPerCell x nodesPerCell node)
// แผนที่เป็น heightfield (1 ตำแหน่ง xz มีพื้นชั้นเดียว) จึงเก็บ node เป็นตาราง 2D + ความสูง
// edge 8 ทิศ: เดิน (ทแยงได้เฉพาะเมื่อเดินอ้อมได้ทั้งสองข้าง) / กระโดดขึ้น-ลงหน้าผา (เฉพาะ 4 ทิศ)
// ความสามารถกระโดดของแต่ละ agent ส่งมาตอนหาเส้นทาง (กราฟเก็บ edge ทุกอันที่ไม่เกิน maxJumpUp/Down ของ Settings)
//
// logic ล้วน ไม่พึ่ง Renderer/Camera สร้างซ้ำได้จาก (config + seed) -> รันบน server headless ได้ (กฎข้อ 1, 2, 5)
// ไม่ thread-safe (buffer ของ A* ใช้ร่วมกัน) เรียกจาก main thread เท่านั้น
public class MapNavGraph
{
    public struct Settings
    {
        public float stepSize;       // ขนาด cell ของแผนที่
        public int nodesPerCell;     // node ต่อด้านต่อ cell (2 = ห่างกัน stepSize/2)
        public float maxJumpUp;      // หน้าผาสูงสุดที่ใส่ edge กระโดดขึ้น
        public float maxJumpDown;    // หน้าผาลึกสุดที่ใส่ edge กระโดดลง
        public float waterCost;      // ตัวคูณ cost ตอนลุยน้ำ (walkable water)
        public float jumpCost;       // cost เพิ่มต่อการกระโดด 1 ครั้ง (ให้เลือกทางลาดก่อนถ้าอ้อมไม่ไกล)
    }

    // ข้อมูลพื้นของแผนที่ที่กราฟต้องใช้ ส่งมาจาก generator (กราฟไม่รู้จัก ProceduralMapGenerator โดยตรง)
    public interface ISurface
    {
        float HeightAt(Vector3 worldPos);                 // ความสูงผิวที่ยืนได้
        bool IsWater(Vector3 worldPos);                   // อยู่ในบ่อ
        bool SameSurface(Vector2Int cellA, Vector2Int cellB, Vector3 boundaryPoint); // ขอบร่วมของสอง cell สูงเท่ากัน (เดินข้ามได้)
    }

    public struct Node
    {
        public Vector3 position;
        public Vector2Int grid;
        public bool water;
    }

    // 8 ทิศ: 4 ตัวแรกเป็นแนวตั้งฉาก (ใช้ index 0-3 ตอนเช็คทแยง)
    private static readonly Vector2Int[] Dirs =
    {
        new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1),
        new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1),
    };
    // ทิศทแยง i (4-7) = ทิศตั้งฉากคู่นี้รวมกัน
    private static readonly int[,] DiagonalParts = { { 0, 2 }, { 0, 3 }, { 1, 2 }, { 1, 3 } };

    private Node[] _nodes = new Node[0];
    private int[] _neighbors = new int[0];     // N * 8, -1 = ไม่มี
    private NavEdge[] _edges = new NavEdge[0]; // N * 8
    private readonly Dictionary<Vector2Int, int> _index = new Dictionary<Vector2Int, int>();
    private Settings _settings;
    private float _spacing;

    public int NodeCount => _nodes.Length;
    public float Spacing => _spacing;
    public Settings Config => _settings;
    // เพิ่มทุกครั้งที่สร้างใหม่ ให้ตัววาด gizmo รู้ว่าต้องสร้าง cache ใหม่
    public int Version { get; private set; }
    private static int _versionCounter;

    public Node GetNode(int index) => _nodes[index];

    public NavEdge GetEdge(int node, int dir, out int neighbor)
    {
        neighbor = _neighbors[node * 8 + dir];
        return neighbor < 0 ? NavEdge.None : _edges[node * 8 + dir];
    }

    // ---------- Build ----------

    // cells ต้องเรียงลำดับคงที่ (กราฟเหมือนกันทุกเครื่องจาก seed เดียวกัน)
    // isBlocked = ตำแหน่งที่มีสิ่งกีดขวาง (ต้นไม้/ก้อนหิน) / holesWalkable = false -> node ในบ่อถูกตัดทิ้ง
    public static MapNavGraph Build(IReadOnlyList<Vector2Int> sortedCells, ISurface surface, bool holesWalkable,
        System.Func<Vector3, bool> isBlocked, Settings s)
    {
        var graph = new MapNavGraph();
        graph._settings = s;
        int res = Mathf.Max(1, s.nodesPerCell);
        graph._spacing = s.stepSize / res;
        graph.Version = ++_versionCounter;

        var nodes = new List<Node>(sortedCells.Count * res * res);
        foreach (var cell in sortedCells)
        {
            for (int sz = 0; sz < res; sz++)
            {
                for (int sx = 0; sx < res; sx++)
                {
                    var g = new Vector2Int(cell.x * res + sx, cell.y * res + sz);
                    Vector3 pos = graph.GridToWorldFlat(g);
                    bool water = surface.IsWater(pos);
                    if (water && !holesWalkable) continue;
                    if (isBlocked != null && isBlocked(pos)) continue;

                    pos.y = surface.HeightAt(pos);
                    graph._index[g] = nodes.Count;
                    nodes.Add(new Node { position = pos, grid = g, water = water });
                }
            }
        }

        graph._nodes = nodes.ToArray();
        int n = graph._nodes.Length;
        graph._neighbors = new int[n * 8];
        graph._edges = new NavEdge[n * 8];
        for (int i = 0; i < graph._neighbors.Length; i++) graph._neighbors[i] = -1;

        // edge แนวตั้งฉากก่อน (ทแยงต้องอ้างถึง)
        for (int i = 0; i < n; i++)
        {
            for (int d = 0; d < 4; d++)
            {
                if (!graph._index.TryGetValue(graph._nodes[i].grid + Dirs[d], out int j)) continue;
                graph._neighbors[i * 8 + d] = j;
                graph._edges[i * 8 + d] = graph.ClassifyEdge(i, j, surface, res);
            }
        }

        // ทแยง: เดินได้เฉพาะเมื่อทั้งสองทางอ้อม (ผ่าน node ตั้งฉาก) เดินต่อกันได้ -> ไม่ตัดมุมหน้าผา/สิ่งกีดขวาง
        for (int i = 0; i < n; i++)
        {
            for (int d = 4; d < 8; d++)
            {
                if (!graph._index.TryGetValue(graph._nodes[i].grid + Dirs[d], out int j)) continue;
                int a = DiagonalParts[d - 4, 0], b = DiagonalParts[d - 4, 1];
                if (graph.WalkVia(i, a, b) && graph.WalkVia(i, b, a))
                {
                    graph._neighbors[i * 8 + d] = j;
                    graph._edges[i * 8 + d] = NavEdge.Walk;
                }
            }
        }
        return graph;
    }

    // i -> (ทิศ first) -> (ทิศ second) เดินได้ทั้งสองช่วง
    private bool WalkVia(int i, int first, int second)
    {
        int mid = _neighbors[i * 8 + first];
        if (mid < 0 || _edges[i * 8 + first] != NavEdge.Walk) return false;
        return _neighbors[mid * 8 + second] >= 0 && _edges[mid * 8 + second] == NavEdge.Walk;
    }

    private NavEdge ClassifyEdge(int i, int j, ISurface surface, int res)
    {
        Node a = _nodes[i], b = _nodes[j];
        Vector2Int cellA = FloorDiv(a.grid, res), cellB = FloorDiv(b.grid, res);

        // cell เดียวกัน = ผิวต่อเนื่องเสมอ (ทางลาดก็ลาดต่อเนื่องใน cell)
        // ต่าง cell: ความสูงตรงขอบร่วมต้องเท่ากันถึงเดินข้ามได้ ไม่งั้นเป็นหน้าผา
        if (cellA == cellB || surface.SameSurface(cellA, cellB, (a.position + b.position) * 0.5f)) return NavEdge.Walk;

        float dy = b.position.y - a.position.y;
        if (dy > 0f) return dy <= _settings.maxJumpUp ? NavEdge.JumpUp : NavEdge.None;
        return -dy <= _settings.maxJumpDown ? NavEdge.JumpDown : NavEdge.None;
    }

    private static Vector2Int FloorDiv(Vector2Int g, int res) =>
        new Vector2Int(Mathf.FloorToInt(g.x / (float)res), Mathf.FloorToInt(g.y / (float)res));

    // grid ของ node -> ตำแหน่งกลาง node (y = 0) / cell c กินพื้นที่ c*step ± step/2
    private Vector3 GridToWorldFlat(Vector2Int g)
    {
        float half = _settings.stepSize * 0.5f;
        return new Vector3(g.x * _spacing - half + _spacing * 0.5f, 0f, g.y * _spacing - half + _spacing * 0.5f);
    }

    private Vector2Int WorldToGrid(Vector3 pos)
    {
        float half = _settings.stepSize * 0.5f;
        return new Vector2Int(Mathf.FloorToInt((pos.x + half) / _spacing), Mathf.FloorToInt((pos.z + half) / _spacing));
    }

    // ---------- Query ----------

    // node ที่ใกล้ตำแหน่งนี้ที่สุด (ถ้าตรงนั้นไม่มี node เช่นยืนชิดต้นไม้ ไล่หาวงรอบออกไปไม่เกิน maxRing)
    public int FindNearestNode(Vector3 pos, int maxRing = 3)
    {
        if (_nodes.Length == 0) return -1;
        Vector2Int g = WorldToGrid(pos);
        if (_index.TryGetValue(g, out int exact)) return exact;

        int best = -1;
        float bestSqr = float.PositiveInfinity;
        for (int ring = 1; ring <= maxRing; ring++)
        {
            for (int dz = -ring; dz <= ring; dz++)
            {
                for (int dx = -ring; dx <= ring; dx++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != ring) continue;
                    if (!_index.TryGetValue(new Vector2Int(g.x + dx, g.y + dz), out int i)) continue;
                    float sqr = (_nodes[i].position - pos).sqrMagnitude;
                    if (sqr < bestSqr) { bestSqr = sqr; best = i; }
                }
            }
            if (best >= 0) return best; // เจอในวงนี้แล้ว วงถัดไปไกลกว่าแน่นอน
        }
        return -1;
    }

    // สุ่ม node ในรัศมี (ใช้ rng ของผู้เรียก -> ผลซ้ำได้จาก seed)
    public int RandomNodeNear(Vector3 center, float radius, System.Random rng, int attempts = 8)
    {
        for (int i = 0; i < attempts; i++)
        {
            double angle = rng.NextDouble() * Mathf.PI * 2f;
            float r = radius * Mathf.Sqrt((float)rng.NextDouble());
            var p = new Vector3(center.x + Mathf.Cos((float)angle) * r, center.y, center.z + Mathf.Sin((float)angle) * r);
            int node = FindNearestNode(p, 1);
            if (node >= 0) return node;
        }
        return -1;
    }

    // เดินเส้นตรงจาก a ไป b ได้ไหม (ทุกช่วงเป็น edge Walk) ใช้ตัดจุดเลี้ยวที่ไม่จำเป็นของเส้นทาง
    public bool HasWalkLine(Vector3 from, Vector3 to)
    {
        int current = FindNearestNode(from, 0);
        int target = FindNearestNode(to, 0);
        if (current < 0 || target < 0) return false;

        Vector3 delta = to - from;
        delta.y = 0f;
        int steps = Mathf.CeilToInt(delta.magnitude / (_spacing * 0.5f));
        for (int s = 1; s <= steps && current != target; s++)
        {
            Vector3 p = from + delta * (s / (float)steps);
            if (!_index.TryGetValue(WorldToGrid(p), out int next)) return false;
            if (next == current) continue;
            if (!IsWalkNeighbor(current, next)) return false;
            current = next;
        }
        return current == target;
    }

    private bool IsWalkNeighbor(int a, int b)
    {
        for (int d = 0; d < 8; d++)
            if (_neighbors[a * 8 + d] == b) return _edges[a * 8 + d] == NavEdge.Walk;
        return false;
    }

    // ---------- A* ----------

    private float[] _gScore = new float[0];
    private int[] _parent = new int[0];
    private NavEdge[] _parentEdge = new NavEdge[0];
    private int[] _visitStamp = new int[0];  // == _searchId -> ค่าใน gScore/parent ของรอบนี้
    private bool[] _closed = new bool[0];
    private int _searchId;
    private readonly List<(float f, int node)> _heap = new List<(float, int)>();
    private readonly List<NavWaypoint> _rawPath = new List<NavWaypoint>();

    // หาเส้นทางจาก from ไป to ตามความสามารถกระโดดของ agent
    // result = จุดที่ต้องไปตามลำดับ (ไม่รวมจุดเริ่ม) ถูกตัดจุดเลี้ยวที่เดินตรงได้ออกแล้ว
    // maxExpanded จำกัดงานต่อ query (กัน server ค้างเมื่อเป้าไปไม่ถึง)
    public bool FindPath(Vector3 from, Vector3 to, float maxJumpUp, float maxJumpDown, List<NavWaypoint> result, int maxExpanded = 4000)
    {
        result.Clear();
        int start = FindNearestNode(from);
        int goal = FindNearestNode(to);
        if (start < 0 || goal < 0) return false;
        if (start == goal)
        {
            result.Add(new NavWaypoint { position = _nodes[goal].position, edge = NavEdge.Walk });
            return true;
        }

        EnsureBuffers();
        if (++_searchId == int.MaxValue) { System.Array.Clear(_visitStamp, 0, _visitStamp.Length); _searchId = 1; }
        _heap.Clear();

        Visit(start, 0f, -1, NavEdge.Walk);
        HeapPush(Heuristic(start, goal), start);

        int expanded = 0;
        bool found = false;
        while (_heap.Count > 0)
        {
            int current = HeapPop();
            if (_closed[current]) continue;
            _closed[current] = true;
            if (current == goal) { found = true; break; }
            if (++expanded > maxExpanded) break;

            float g = _gScore[current];
            for (int d = 0; d < 8; d++)
            {
                int next = _neighbors[current * 8 + d];
                if (next < 0) continue;
                NavEdge edge = _edges[current * 8 + d];
                if (edge == NavEdge.None) continue;

                float dy = _nodes[next].position.y - _nodes[current].position.y;
                if (edge == NavEdge.JumpUp && dy > maxJumpUp) continue;
                if (edge == NavEdge.JumpDown && -dy > maxJumpDown) continue;

                float cost = (d < 4 ? _spacing : _spacing * 1.41421356f) * (_nodes[next].water ? _settings.waterCost : 1f);
                if (edge != NavEdge.Walk) cost += _settings.jumpCost;

                float ng = g + cost;
                bool seen = _visitStamp[next] == _searchId;
                if (seen && (_closed[next] || ng >= _gScore[next])) continue;

                Visit(next, ng, current, edge);
                HeapPush(ng + Heuristic(next, goal), next);
            }
        }
        if (!found) return false;

        // ย้อนจาก goal กลับไป start
        _rawPath.Clear();
        for (int node = goal; node != start; node = _parent[node])
            _rawPath.Add(new NavWaypoint { position = _nodes[node].position, edge = _parentEdge[node] });
        _rawPath.Reverse();

        SmoothPath(from, result);
        return true;
    }

    // string pulling: ข้ามจุดกลางทางถ้าเดินตรงจากจุดล่าสุดไปจุดถัดๆ ไปได้ (ไม่ข้ามจุดกระโดด)
    private void SmoothPath(Vector3 from, List<NavWaypoint> result)
    {
        Vector3 anchor = from;
        int i = 0;
        while (i < _rawPath.Count)
        {
            NavWaypoint wp = _rawPath[i];
            if (wp.edge != NavEdge.Walk)
            {
                result.Add(wp);
                anchor = wp.position;
                i++;
                continue;
            }

            // ไล่หาจุด Walk ที่ไกลที่สุดที่ยังเดินตรงถึง (หยุดก่อนจุดกระโดดถัดไป)
            int far = i;
            for (int k = i + 1; k < _rawPath.Count && _rawPath[k].edge == NavEdge.Walk; k++)
            {
                if (!HasWalkLine(anchor, _rawPath[k].position)) break;
                far = k;
            }
            // จุดก่อนกระโดดต้องคงไว้ (เป็นจุดยืนตั้งท่ากระโดด)
            result.Add(_rawPath[far]);
            anchor = _rawPath[far].position;
            i = far + 1;
        }
    }

    private void Visit(int node, float g, int parent, NavEdge edge)
    {
        _visitStamp[node] = _searchId;
        _closed[node] = false;
        _gScore[node] = g;
        _parent[node] = parent;
        _parentEdge[node] = edge;
    }

    private float Heuristic(int a, int b)
    {
        // octile distance บนระนาบ (admissible กับ cost เดิน 1 / ทแยง 1.414 ต่อช่อง)
        Vector2Int d = _nodes[a].grid - _nodes[b].grid;
        int dx = Mathf.Abs(d.x), dz = Mathf.Abs(d.y);
        return _spacing * (Mathf.Max(dx, dz) + 0.41421356f * Mathf.Min(dx, dz));
    }

    private void EnsureBuffers()
    {
        int n = _nodes.Length;
        if (_gScore.Length == n) return;
        _gScore = new float[n];
        _parent = new int[n];
        _parentEdge = new NavEdge[n];
        _visitStamp = new int[n];
        _closed = new bool[n];
        _searchId = 0;
    }

    private void HeapPush(float f, int node)
    {
        _heap.Add((f, node));
        int i = _heap.Count - 1;
        while (i > 0)
        {
            int p = (i - 1) / 2;
            if (_heap[p].f <= _heap[i].f) break;
            (_heap[p], _heap[i]) = (_heap[i], _heap[p]);
            i = p;
        }
    }

    private int HeapPop()
    {
        int top = _heap[0].node;
        int last = _heap.Count - 1;
        _heap[0] = _heap[last];
        _heap.RemoveAt(last);

        int i = 0;
        while (true)
        {
            int l = i * 2 + 1, r = l + 1, m = i;
            if (l < _heap.Count && _heap[l].f < _heap[m].f) m = l;
            if (r < _heap.Count && _heap[r].f < _heap[m].f) m = r;
            if (m == i) break;
            (_heap[m], _heap[i]) = (_heap[i], _heap[m]);
            i = m;
        }
        return top;
    }
}

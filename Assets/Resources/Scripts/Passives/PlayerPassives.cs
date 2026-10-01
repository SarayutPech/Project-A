using System.Collections.Generic;
using UnityEngine;

// passive tree ของ player (simulation ฝั่ง server): ลงแต้ม/คืนแต้ม แล้ว stat ของทุก node ที่ลงเข้า PlayerStats แหล่ง "passive"
// กติกา (server ตรวจเอง กฎข้อ 8): node มีจริง / แต้มพอ / ติดกับ node ที่ลงแล้ว / คืนแต้มแล้ว tree ที่เหลือยังต่อถึงจุด start
// แต้มทั้งหมด = (เลเวล - 1) + bonusPoints / จุด start ได้ฟรี (ไม่ใช้แต้ม) เลือกจุด start ใหม่ได้ตอนยังไม่ได้ลงแต้มอื่น
// UI เรียกแค่ Request* ด้วย node id (ภายหลัง = ServerRpc) / เซฟผ่าน ICharacterService
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerStats))]
public class PlayerPassives : MonoBehaviour
{
    [Tooltip("เว้นว่าง = โหลดจาก Resources/" + PassiveTree.DefaultResourcePath)]
    public PassiveTree tree;
    [Tooltip("แต้มเพิ่มนอกจากที่ได้ตามเลเวล")]
    [Min(0)] public int bonusPoints = 0;

    public event System.Action<PlayerPassives> Changed;

    public PassiveTree Tree => tree;
    public string StartNode { get; private set; }
    public IReadOnlyCollection<string> Allocated => _allocated;
    public int TotalPoints => (_exp != null ? _exp.Level - 1 : 0) + bonusPoints;
    public int SpentPoints => Mathf.Max(0, _allocated.Count - (StartNode != null ? 1 : 0));
    public int PointsLeft => TotalPoints - SpentPoints;

    private const string Source = "passive";
    private readonly HashSet<string> _allocated = new HashSet<string>();
    private PlayerStats _stats;
    private PlayerExperience _exp;
    private string OwnerId => GameServices.LocalPlayerId; // TODO Netcode: id ของเจ้าของตัวนี้

    private void Awake()
    {
        _stats = GetComponent<PlayerStats>();
        _exp = GetComponent<PlayerExperience>();
        if (tree == null) tree = Resources.Load<PassiveTree>(PassiveTree.DefaultResourcePath);
    }

    // โหลดตอน Start: PlayerExperience ต้องโหลดเลเวลเสร็จก่อน (ไม่งั้นแต้มดูเหมือนไม่พอแล้วโดนรีเซ็ต)
    private void Start()
    {
        Load();
    }

    private void OnEnable()
    {
        if (_exp != null) _exp.Changed += OnExpChanged;
    }

    private void OnDisable()
    {
        if (_exp != null) _exp.Changed -= OnExpChanged;
    }

    private void OnExpChanged(PlayerExperience _) => Changed?.Invoke(this); // เลเวลขึ้น = แต้มเพิ่ม

    public bool IsAllocated(string id) => id != null && _allocated.Contains(id);

    // ลงได้ไหม: มีแต้ม + ยังไม่ลง + ติดกับ node ที่ลงแล้ว
    public bool CanAllocate(string id)
    {
        if (tree == null || tree.Get(id) == null || IsAllocated(id) || PointsLeft <= 0) return false;
        foreach (var n in tree.Neighbors(id))
            if (_allocated.Contains(n)) return true;
        return false;
    }

    // คืนได้ไหม: ลงอยู่ + ไม่ใช่จุด start + ที่เหลือยังต่อถึง start ทั้งหมด
    public bool CanRefund(string id)
    {
        if (!IsAllocated(id) || id == StartNode) return false;
        return StillConnectedWithout(id);
    }

    // เลือกจุด start ได้ตอนยังไม่ได้ลงแต้มอื่น
    public bool CanChooseStart(string id)
    {
        var node = tree != null ? tree.Get(id) : null;
        return node != null && node.isStart && id != StartNode && SpentPoints == 0;
    }

    // ---------- คำขอจาก client ----------

    public bool RequestAllocate(string id)
    {
        if (!CanAllocate(id)) return false;
        _allocated.Add(id);
        Commit();
        return true;
    }

    public bool RequestRefund(string id)
    {
        if (!CanRefund(id)) return false;
        _allocated.Remove(id);
        Commit();
        return true;
    }

    public bool RequestChooseStart(string id)
    {
        if (!CanChooseStart(id)) return false;
        _allocated.Clear();
        StartNode = id;
        _allocated.Add(id);
        Commit();
        return true;
    }

    // คืนแต้มทั้งหมด (เหลือแค่จุด start)
    public void RequestResetAll()
    {
        _allocated.Clear();
        if (StartNode != null) _allocated.Add(StartNode);
        Commit();
    }

    // ---------- Internal ----------

    private void Load()
    {
        _allocated.Clear();
        StartNode = null;
        if (tree == null) return;

        // id ที่ไม่มีใน tree แล้ว (แก้ tree ทีหลัง) ถูกทิ้ง
        foreach (var id in GameServices.Character.LoadPassives(OwnerId))
        {
            var node = tree.Get(id);
            if (node == null) continue;
            _allocated.Add(id);
            if (node.isStart && StartNode == null) StartNode = id;
        }
        if (StartNode == null)
        {
            foreach (var s in tree.StartNodes)
            {
                StartNode = s.id;
                break;
            }
            if (StartNode != null) _allocated.Add(StartNode);
        }
        PruneDisconnected();
        // แต้มเกิน (เช่นลด bonus) -> รีเซ็ต
        if (PointsLeft < 0) RequestResetAll();
        else
        {
            Apply();
            Changed?.Invoke(this);
        }
    }

    private void Commit()
    {
        GameServices.Character.SavePassives(OwnerId, new List<string>(_allocated));
        Apply();
        Changed?.Invoke(this);
    }

    private void Apply()
    {
        _stats.RemoveModifiers(Source);
        foreach (var id in _allocated)
        {
            var node = tree.Get(id);
            if (node == null) continue;
            foreach (var m in node.modifiers) _stats.AddModifier(Source, m);
        }
    }

    // node ที่ลงไว้แต่ไม่ต่อถึง start (tree ถูกแก้เส้น) -> ทิ้ง
    private void PruneDisconnected()
    {
        if (StartNode == null) return;
        var reached = Reachable(null);
        _allocated.RemoveWhere(id => !reached.Contains(id));
    }

    private bool StillConnectedWithout(string removed)
    {
        var reached = Reachable(removed);
        foreach (var id in _allocated)
            if (id != removed && !reached.Contains(id)) return false;
        return true;
    }

    // BFS จาก start ผ่านเฉพาะ node ที่ลงแล้ว (ข้าม excluded)
    private HashSet<string> Reachable(string excluded)
    {
        var seen = new HashSet<string>();
        if (StartNode == null || StartNode == excluded) return seen;
        var queue = new Queue<string>();
        queue.Enqueue(StartNode);
        seen.Add(StartNode);
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            foreach (var n in tree.Neighbors(cur))
            {
                if (n == excluded || seen.Contains(n) || !_allocated.Contains(n)) continue;
                seen.Add(n);
                queue.Enqueue(n);
            }
        }
        return seen;
    }

    [ContextMenu("Add 5 Bonus Points")]
    private void DebugAddPoints()
    {
        bonusPoints += 5;
        Changed?.Invoke(this);
    }
}

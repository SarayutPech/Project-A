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
    [Tooltip("เว้นว่าง = tree ที่เลือกด้วย Use Tree (ไม่มี = Resources/" + PassiveTree.DefaultResourcePath + ")")]
    public PassiveTree tree;
    [Tooltip("แต้มเพิ่มนอกจากที่ได้ตามเลเวล")]
    [Min(0)] public int bonusPoints = 0;

    public event System.Action<PlayerPassives> Changed;

    public PassiveTree Tree => tree;
    public string StartNode { get; private set; }
    // จุด start ที่ race ของตัวละครกำหนด (null = ไม่มี race เช่นเล่นตรงจาก Hideout -> เลือก start เองได้แบบเดิม)
    public string RaceStartNode { get; private set; }
    public IReadOnlyCollection<string> Allocated => _allocated;
    public int TotalPoints => (_exp != null ? _exp.Level - 1 : 0) + bonusPoints;
    public int SpentPoints => Mathf.Max(0, _allocated.Count - (StartNode != null ? 1 : 0));
    public int PointsLeft => TotalPoints - SpentPoints;

    private const string Source = "passive";
    private const char ChoiceSeparator = '|'; // เซฟ node แบบ Choice เป็น "id|ลำดับตัวเลือก" (node อื่นเป็น id เฉยๆ)
    private readonly HashSet<string> _allocated = new HashSet<string>();
    private readonly Dictionary<string, int> _choices = new Dictionary<string, int>(); // node Choice ที่ลงแล้ว -> ลำดับตัวเลือก
    private PlayerStats _stats;
    private PlayerExperience _exp;
    private string OwnerId => GameServices.LocalPlayerId; // TODO Netcode: id ของเจ้าของตัวนี้

    private void Awake()
    {
        _stats = GetComponent<PlayerStats>();
        _exp = GetComponent<PlayerExperience>();
        if (tree == null) tree = PassiveTree.LoadActive();
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

    // เลือกจุด start ได้ตอนยังไม่ได้ลงแต้มอื่น และ race ไม่ได้ล็อกจุด start ไว้
    public bool CanChooseStart(string id)
    {
        var node = tree != null ? tree.Get(id) : null;
        return node != null && node.isStart && id != StartNode && SpentPoints == 0 && RaceStartNode == null;
    }

    // ---------- คำขอจาก client ----------

    // choice = ลำดับตัวเลือก (ใช้กับ node แบบ Choice เท่านั้น node อื่นไม่สน)
    public bool RequestAllocate(string id, int choice = 0)
    {
        if (!CanAllocate(id)) return false;
        var node = tree.Get(id);
        if (node.IsChoice)
        {
            if (choice < 0 || choice >= tree.ChoicesOf(node).Count) return false;
            _choices[id] = choice;
        }
        _allocated.Add(id);
        Commit();
        return true;
    }

    // คืนแต้ม node (gem ที่ใส่อยู่กลับกระเป๋าเองใน Apply)
    public bool RequestRefund(string id)
    {
        if (!CanRefund(id)) return false;
        _allocated.Remove(id);
        _choices.Remove(id);
        Commit();
        return true;
    }

    // ---------- Choice / Socket ----------

    // ตัวเลือกที่ลงไว้ของ node Choice (-1 = ไม่ใช่ node Choice / ยังไม่ลง)
    public int GetChoice(string id) => id != null && _choices.TryGetValue(id, out var c) ? c : -1;

    public PassiveChoice GetChosen(PassiveNode node)
    {
        if (node == null || !node.IsChoice || !_allocated.Contains(node.id)) return null;
        var choices = tree.ChoicesOf(node);
        if (choices.Count == 0) return null;
        return choices[Mathf.Clamp(GetChoice(node.id), 0, choices.Count - 1)];
    }

    // gem ที่ใส่ใน node Socket นี้ (null = ว่าง)
    public ItemInstance? GetSocketed(string nodeId)
    {
        foreach (var s in GameServices.Inventory.GetPassiveSockets(OwnerId))
            if (s.nodeId == nodeId) return s.item;
        return null;
    }

    public bool CanSocket(string nodeId)
    {
        var node = tree != null ? tree.Get(nodeId) : null;
        return node != null && node.type == PassiveNodeType.Socket && IsAllocated(nodeId);
    }

    // ใส่ PassiveGem จากกระเป๋าลง node Socket ที่ลงแต้มแล้ว (ของเดิมในช่องกลับกระเป๋า) server ตรวจ node / ชนิด gem / ของมีจริง
    public bool RequestSocketGem(string nodeId, string instanceId)
    {
        if (!CanSocket(nodeId)) return false;
        var inventory = GameServices.Inventory;
        if (!inventory.TryGetItem(OwnerId, instanceId, out var inst) || !(inst.Definition is PassiveGem)) return false;
        if (!inventory.SocketPassive(OwnerId, instanceId, nodeId, out _)) return false;
        Commit();
        return true;
    }

    public bool RequestUnsocketGem(string nodeId)
    {
        if (!GetSocketed(nodeId).HasValue) return false;
        if (!GameServices.Inventory.UnsocketPassive(OwnerId, nodeId, out _)) return false;
        Commit();
        return true;
    }

    public bool RequestChooseStart(string id)
    {
        if (!CanChooseStart(id)) return false;
        _allocated.Clear();
        _choices.Clear();
        StartNode = id;
        _allocated.Add(id);
        Commit();
        return true;
    }

    // คืนแต้มทั้งหมด (เหลือแค่จุด start)
    public void RequestResetAll()
    {
        _allocated.Clear();
        _choices.Clear();
        if (StartNode != null) _allocated.Add(StartNode);
        Commit();
    }

    // ---------- Internal ----------

    private void Load()
    {
        _allocated.Clear();
        _choices.Clear();
        StartNode = null;
        if (tree == null) return;

        // id ที่ไม่มีใน tree แล้ว (แก้ tree ทีหลัง) ถูกทิ้ง
        foreach (var raw in GameServices.Character.LoadPassives(OwnerId))
        {
            string id = raw;
            int choice = 0;
            int sep = raw.IndexOf(ChoiceSeparator);
            if (sep >= 0)
            {
                id = raw.Substring(0, sep);
                int.TryParse(raw.Substring(sep + 1), out choice);
            }
            var node = tree.Get(id);
            if (node == null) continue;
            _allocated.Add(id);
            if (node.IsChoice) _choices[id] = choice;
            if (node.isStart && StartNode == null) StartNode = id;
        }
        // race กำหนดจุด start: เซฟที่เริ่มคนละจุด (แก้ race/tree ทีหลัง) -> เริ่มใหม่จากจุดของ race
        RaceStartNode = ResolveRaceStart();
        if (RaceStartNode != null && StartNode != RaceStartNode)
        {
            _allocated.Clear();
            _choices.Clear();
            StartNode = RaceStartNode;
            _allocated.Add(StartNode);
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

    private string ResolveRaceStart()
    {
        if (!GameServices.Character.TryGetCharacter(OwnerId, out var info)) return null;
        var config = CharacterCreationConfig.LoadDefault();
        var race = config != null ? config.GetRace(info.raceId) : null;
        if (race == null || string.IsNullOrEmpty(race.passiveStartNode)) return null;

        var node = tree.Get(race.passiveStartNode);
        if (node != null && node.isStart) return node.id;
        Debug.LogWarning($"[{nameof(PlayerPassives)}] race '{race.Id}' ตั้งจุด start '{race.passiveStartNode}' ที่ไม่ใช่ node start ใน tree", race);
        return null;
    }

    private void Commit()
    {
        var saved = new List<string>(_allocated.Count);
        foreach (var id in _allocated)
            saved.Add(_choices.TryGetValue(id, out var choice) ? id + ChoiceSeparator + choice : id);
        GameServices.Character.SavePassives(OwnerId, saved);
        Apply();
        Changed?.Invoke(this);
    }

    private void Apply()
    {
        ReleaseOrphanSockets();
        _stats.RemoveModifiers(Source);
        var sockets = GameServices.Inventory.GetPassiveSockets(OwnerId);
        foreach (var id in _allocated)
        {
            var node = tree.Get(id);
            if (node == null) continue;
            foreach (var m in node.modifiers) _stats.AddModifier(Source, m);

            var chosen = GetChosen(node);
            if (chosen != null)
                foreach (var m in chosen.modifiers) _stats.AddModifier(Source, m);

            if (node.type != PassiveNodeType.Socket) continue;
            foreach (var s in sockets)
            {
                if (s.nodeId != id || !(s.item.Definition is PassiveGem gem)) continue;
                foreach (var m in gem.modifiers) _stats.AddModifier(Source, gem.Scaled(m, s.item.Level));
            }
        }
    }

    // gem ใน node ที่ไม่ได้ลงแต้มแล้ว / ไม่ใช่ Socket แล้ว (คืนแต้ม, รีเซ็ต, แก้ tree) -> กลับกระเป๋า
    private void ReleaseOrphanSockets()
    {
        var inventory = GameServices.Inventory;
        var orphans = new List<string>();
        foreach (var s in inventory.GetPassiveSockets(OwnerId))
        {
            var node = tree.Get(s.nodeId);
            if (node == null || node.type != PassiveNodeType.Socket || !_allocated.Contains(s.nodeId)) orphans.Add(s.nodeId);
        }
        foreach (var nodeId in orphans) inventory.UnsocketPassive(OwnerId, nodeId, out _);
    }

    // node ที่ลงไว้แต่ไม่ต่อถึง start (tree ถูกแก้เส้น) -> ทิ้ง
    private void PruneDisconnected()
    {
        if (StartNode == null) return;
        var reached = Reachable(null);
        _allocated.RemoveWhere(id => !reached.Contains(id));
        var stale = new List<string>();
        foreach (var id in _choices.Keys) if (!_allocated.Contains(id)) stale.Add(id);
        foreach (var id in stale) _choices.Remove(id);
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

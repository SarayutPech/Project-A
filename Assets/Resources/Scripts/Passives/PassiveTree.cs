using System.Collections.Generic;
using UnityEngine;

// ขนาด/ความสำคัญของ node (ค่าตัวเลขห้ามเปลี่ยน asset เก็บเป็นตัวเลข)
public enum PassiveNodeType
{
    Small = 0,    // node เล็ก: stat เล็กน้อย เช่น +10 Str
    Notable = 1,  // node ใหญ่: stat ชุดใหญ่กว่า
    Keystone = 2, // node พิเศษ: ผลแรงแต่มักมีข้อเสีย (ใส่ modifier ติดลบได้)
}

// node 1 จุดของ tree (ตำแหน่ง / เส้นเชื่อม / stat) แก้ใน Tools > Project-A > Passive Tree Editor
[System.Serializable]
public class PassiveNode
{
    [Tooltip("รหัสคงที่ ใช้เซฟ (ห้ามเปลี่ยนหลังมีผู้เล่นลงแต้มแล้ว)")]
    public string id;
    public string displayName;
    public PassiveNodeType type = PassiveNodeType.Small;
    [Tooltip("จุดเริ่ม: ได้ฟรี และลงแต้มต่อออกไปจากจุดนี้ (มีได้หลายจุด ผู้เล่นเลือกได้ 1 จุดก่อนลงแต้มแรก)")]
    public bool isStart;
    [Tooltip("ตำแหน่งบน tree (หน่วย = pixel ที่ zoom 1, +y = ขึ้น)")]
    public Vector2 position;
    [Tooltip("id ของ node ที่เชื่อม (ไม่มีทิศ: เก็บฝั่งไหนก็ได้)")]
    public List<string> links = new List<string>();
    public List<StatModifier> modifiers = new List<StatModifier>();
    [TextArea(1, 3)] public string description;
    [Tooltip("รูปในวงกลมของ node (เว้นว่าง = ไม่มีรูป)")]
    public Sprite icon;
    [Tooltip("กรอบ/พื้นของ node นี้ (เว้นว่าง = ใช้ของ template ตามชนิด node ใน GameUI prefab)")]
    public Sprite frame;
}

// passive skill tree ทั้งต้น (ข้อมูลกลางอ่านอย่างเดียว เหมือนกันทุกเครื่อง) ผู้เล่นเก็บแค่ id ของ node ที่ลงแต้ม
// asset หลักอยู่ที่ Resources/Gameobject/ScriptAbleObject/PassiveTree/PassiveTree
[CreateAssetMenu(menuName = "Passives/Passive Tree", fileName = "PassiveTree")]
public class PassiveTree : ScriptableObject
{
    public const string DefaultResourcePath = "Gameobject/ScriptAbleObject/PassiveTree/PassiveTree";

    public List<PassiveNode> nodes = new List<PassiveNode>();

    private Dictionary<string, PassiveNode> _byId;
    private Dictionary<string, List<string>> _adjacency;

    public PassiveNode Get(string id)
    {
        EnsureCache();
        return id != null && _byId.TryGetValue(id, out var n) ? n : null;
    }

    // เพื่อนบ้านทั้งสองทิศ (เส้นเก็บไว้ฝั่งเดียวก็นับ)
    public IReadOnlyList<string> Neighbors(string id)
    {
        EnsureCache();
        return id != null && _adjacency.TryGetValue(id, out var list) ? list : (IReadOnlyList<string>)System.Array.Empty<string>();
    }

    public IEnumerable<PassiveNode> StartNodes
    {
        get
        {
            foreach (var n in nodes) if (n.isStart) yield return n;
        }
    }

    // แก้ข้อมูลแล้วต้องเรียก (editor เรียกให้เอง)
    public void InvalidateCache()
    {
        _byId = null;
        _adjacency = null;
    }

    private void EnsureCache()
    {
        if (_byId != null) return;
        _byId = new Dictionary<string, PassiveNode>();
        _adjacency = new Dictionary<string, List<string>>();
        foreach (var n in nodes)
        {
            if (string.IsNullOrEmpty(n.id) || _byId.ContainsKey(n.id)) continue;
            _byId[n.id] = n;
            _adjacency[n.id] = new List<string>();
        }
        foreach (var n in nodes)
        {
            if (!_byId.ContainsKey(n.id)) continue;
            foreach (var other in n.links)
            {
                if (other == n.id || !_byId.ContainsKey(other)) continue;
                if (!_adjacency[n.id].Contains(other)) _adjacency[n.id].Add(other);
                if (!_adjacency[other].Contains(n.id)) _adjacency[other].Add(n.id);
            }
        }
    }

    private void OnValidate() => InvalidateCache();
    private void OnEnable() => InvalidateCache();
}

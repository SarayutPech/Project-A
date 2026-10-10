using System.Collections.Generic;
using UnityEngine;

// ขนาด/ความสำคัญของ node (ค่าตัวเลขห้ามเปลี่ยน asset เก็บเป็นตัวเลข)
public enum PassiveNodeType
{
    Small = 0,    // node เล็ก: stat เล็กน้อย เช่น +10 Str
    Notable = 1,  // node ใหญ่: stat ชุดใหญ่กว่า
    Keystone = 2, // node พิเศษ: ผลแรงแต่มักมีข้อเสีย (ใส่ modifier ติดลบได้)
    Choice = 3,   // node ที่ผู้เล่นเลือก stat เองตอนลงแต้ม (เลือกจาก choices ของ node) เปลี่ยนต้องคืนแต้มแล้วลงใหม่
    Socket = 4,   // node ช่องใส่ PassiveGem: ลงแต้มแล้วใส่ gem จากกระเป๋า -> ได้ stat ของ gem เพิ่มจาก modifiers ของ node
    FixedChoice = 5, // เหมือน Choice แต่ตัวเลือกมาจากชุดกลางของ tree (choiceSets) แก้ชุดทีเดียวมีผลทุก node ที่ใช้ชุดนั้น
}

// ตัวเลือก 1 อันของ node แบบ Choice (เช่น "+10 Str" / "+10 Dex")
[System.Serializable]
public class PassiveChoice
{
    public string name;
    public List<StatModifier> modifiers = new List<StatModifier>();
}

// ชุดตัวเลือกกลาง (Fixed Choice): ตั้งครั้งเดียว node ไหนอ้าง id นี้ก็ได้ตัวเลือกชุดเดียวกัน
[System.Serializable]
public class PassiveChoiceSet
{
    public string id;
    public string name;
    public List<PassiveChoice> choices = new List<PassiveChoice>();
}

// ภาพพื้นหลังของ tree (art ใหญ่ที่วางหลัง node/เส้น) ทึบเมื่อยังไม่ได้ลงแต้ม สว่างเมื่อลง node ที่กำหนด
[System.Serializable]
public class PassiveBackground
{
    public string name;
    public Sprite art;
    [Tooltip("ตำแหน่งกลางภาพบน tree (หน่วยเดียวกับ node, +y = ขึ้น)")]
    public Vector2 position;
    [Tooltip("ขนาดบน tree (0,0 = ขนาดตามภาพ)")]
    public Vector2 size;
    [Tooltip("หมุนทวนเข็ม (องศา)")]
    public float rotation;
    [Tooltip("node ที่ทำให้ภาพสว่างเมื่อลงแต้ม (ว่าง = สว่างตลอด)")]
    public List<string> nodeIds = new List<string>();
    [Tooltip("เปิด = ต้องลงครบทุก node ในรายการ / ปิด = ลง node ใดก็ได้ 1 ตัวก็สว่าง")]
    public bool requireAll = false;

    public bool IsLit(System.Func<string, bool> isAllocated)
    {
        if (nodeIds == null || nodeIds.Count == 0) return true;
        foreach (var id in nodeIds)
        {
            bool on = isAllocated(id);
            if (requireAll && !on) return false;
            if (!requireAll && on) return true;
        }
        return requireAll;
    }

    public Vector2 SizeOrNative => size.sqrMagnitude > 0.01f ? size : art != null ? art.rect.size : Vector2.zero;
}

// รูปทรงของ node (ค่าตัวเลข = จำนวนด้าน 0 = วงกลม)
public enum PassiveNodeShape
{
    Circle = 0,
    Triangle = 3,
    Square = 4,
    Pentagon = 5,
    Hexagon = 6,
}

// ค่าเริ่มต้นของ node แต่ละชนิด (รูป / ขนาด / สี) ใช้เมื่อ node นั้นไม่ได้ตั้งเอง แก้ที่เดียวมีผลทุก node ของชนิดนั้น
[System.Serializable]
public class PassiveNodeStyle
{
    public PassiveNodeType type;
    [Tooltip("กรอบ/พื้นเริ่มต้น (ว่าง = ใช้ template ใน GameUI prefab)")]
    public Sprite frame;
    [Tooltip("รูปในวงกลมเริ่มต้น (ว่าง = ไม่มีรูป)")]
    public Sprite icon;
    [Tooltip("ขนาด node เทียบขนาดมาตรฐานของ tree (nodeSize) 1 = เท่ากันทุกชนิด")]
    [Min(0.1f)] public float scale = 1f;
    [Tooltip("รูปทรงของ node (ใช้เมื่อ node ไม่มี frame sprite)")]
    public PassiveNodeShape shape = PassiveNodeShape.Circle;
    [Tooltip("สีประจำชนิด node (ผังใน editor + สีรูปทรงในเกม)")]
    public Color color = new Color(0.55f, 0.55f, 0.55f, 1f);
    [HideInInspector] public bool configured; // ตั้งสี/รูปทรงเริ่มต้นของชนิดแล้ว (editor ใช้ย้ายค่าเก่า)
    [Tooltip("เปิด = ใช้สี 3 สถานะข้างล่างแทนสีใน template")]
    public bool overrideColors;
    public Color lockedColor = new Color(0.3f, 0.3f, 0.3f, 1f);
    public Color availableColor = new Color(0.6f, 0.6f, 0.6f, 1f);
    public Color allocatedColor = new Color(0.95f, 0.8f, 0.4f, 1f);
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
    [Tooltip("เฉพาะ node แบบ Choice: ตัวเลือกที่ผู้เล่นเลือก 1 อันตอนลงแต้ม (ได้ร่วมกับ modifiers ของ node เสมอ)")]
    public List<PassiveChoice> choices = new List<PassiveChoice>();
    [Tooltip("เฉพาะ node แบบ Fixed Choice: id ของชุดตัวเลือกใน PassiveTree.choiceSets")]
    public string choiceSetId;

    public bool IsChoice => type == PassiveNodeType.Choice || type == PassiveNodeType.FixedChoice;
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
    // tree ที่เกมใช้: ที่เลือกไว้ใน PassiveTreeSelection ไม่มี = ตัวเริ่มต้น
    public static PassiveTree LoadActive()
    {
        var selection = Resources.Load<PassiveTreeSelection>(PassiveTreeSelection.ResourcePath);
        if (selection != null && selection.tree != null) return selection.tree;
        return Resources.Load<PassiveTree>(DefaultResourcePath);
    }


    public List<PassiveNode> nodes = new List<PassiveNode>();
    [Tooltip("ชุดตัวเลือกกลางของ node แบบ Fixed Choice")]
    public List<PassiveChoiceSet> choiceSets = new List<PassiveChoiceSet>();
    [Tooltip("ภาพพื้นหลัง (วาดตามลำดับ ตัวบนสุดของ list อยู่หลังสุด)")]
    public List<PassiveBackground> backgrounds = new List<PassiveBackground>();

    [Header("Defaults per node type")]
    [Tooltip("รูป / ขนาด / สีเริ่มต้นของ node แต่ละชนิด (แก้ใน tab Config ของ Tree Editor)")]
    public List<PassiveNodeStyle> styles = new List<PassiveNodeStyle>();
    [Tooltip("สีภาพพื้นหลังตอนยังไม่ได้ลง node ที่กำหนด (ทึบ) กับตอนลงแล้ว (สว่าง)")]
    public Color backgroundDim = new Color(0.22f, 0.22f, 0.22f, 1f);
    public Color backgroundLit = Color.white;

    [Tooltip("ขนาด node มาตรฐาน (pixel ที่ zoom 1) ทุกชนิดเท่ากัน ปรับเฉพาะชนิดด้วย scale ของ style / 0 = ใช้ขนาด template ใน prefab")]
    [Min(0f)] public float nodeSize = 48f;

    // สี/รูปทรงเริ่มต้นของแต่ละชนิด (ใช้ตอนสร้าง style ใหม่)
    public static void ApplyDefaultLook(PassiveNodeStyle s)
    {
        s.shape = s.type == PassiveNodeType.Socket ? PassiveNodeShape.Square : PassiveNodeShape.Circle;
        s.color = s.type switch
        {
            PassiveNodeType.Notable => new Color(0.85f, 0.7f, 0.3f),
            PassiveNodeType.Keystone => new Color(0.6f, 0.35f, 0.85f),
            PassiveNodeType.Choice => new Color(0.3f, 0.7f, 0.85f),
            PassiveNodeType.FixedChoice => new Color(0.9f, 0.55f, 0.3f),
            PassiveNodeType.Socket => new Color(0.35f, 0.8f, 0.45f),
            _ => new Color(0.55f, 0.55f, 0.55f),
        };
        s.configured = true;
    }

    public PassiveNodeStyle StyleOf(PassiveNodeType type)
    {
        foreach (var s in styles) if (s.type == type) return s;
        return null;
    }

    // node ตั้งเองก่อน ไม่ตั้ง = ค่าเริ่มต้นของชนิด (null = ใช้ template)
    public Sprite FrameOf(PassiveNode n) => n.frame != null ? n.frame : StyleOf(n.type)?.frame;
    public Sprite IconOf(PassiveNode n) => n.icon != null ? n.icon : StyleOf(n.type)?.icon;
    public float ScaleOf(PassiveNodeType type) => StyleOf(type)?.scale ?? 1f;

    private static readonly List<PassiveChoice> NoChoices = new List<PassiveChoice>();

    // ตัวเลือกของ node: Choice = ของ node เอง / Fixed Choice = ของชุดที่ node อ้างอยู่ / อื่นๆ = ว่าง
    public IReadOnlyList<PassiveChoice> ChoicesOf(PassiveNode node)
    {
        if (node == null) return NoChoices;
        if (node.type == PassiveNodeType.Choice) return node.choices;
        if (node.type != PassiveNodeType.FixedChoice) return NoChoices;
        foreach (var set in choiceSets)
            if (set.id == node.choiceSetId) return set.choices;
        return NoChoices;
    }

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

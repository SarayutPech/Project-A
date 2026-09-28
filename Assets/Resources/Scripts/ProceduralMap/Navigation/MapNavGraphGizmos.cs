using System.Collections.Generic;
using UnityEngine;

// วาดกราฟเดินของ AI (MapNavGraph) ใน Scene view ไว้ตรวจว่าเดิน/กระโดดไปไหนได้บ้าง (debug อย่างเดียว ไม่มีผลกับเกม)
// เขียว = เดิน / เหลือง = กระโดดขึ้น / ฟ้า = กระโดดลง / จุดน้ำเงิน = node ในน้ำ
// วางบน object เดียวกับ ProceduralMapGenerator (หรือลาก generator มาใส่) เห็นได้ทั้งตอน Edit และ Play
public class MapNavGraphGizmos : MonoBehaviour
{
    [Tooltip("ถ้าเว้นว่างจะหาบน object เดียวกัน")]
    public ProceduralMapGenerator generator;
    public bool drawWalkEdges = true;
    public bool drawJumpEdges = true;
    public bool drawNodes = false;
    [Tooltip("วาดเฉพาะเมื่อเลือก object นี้อยู่ (กราฟใหญ่วาดทุกเฟรมแล้ว Scene view หน่วง)")]
    public bool onlyWhenSelected = true;
    [Tooltip("ยกเส้นขึ้นจากพื้นเท่านี้ไม่ให้จมผิวพื้น")]
    public float heightOffset = 0.15f;

    public Color walkColor = new Color(0.2f, 0.9f, 0.3f, 0.6f);
    public Color jumpUpColor = new Color(1f, 0.85f, 0.1f, 1f);
    public Color jumpDownColor = new Color(0.2f, 0.7f, 1f, 1f);
    public Color waterNodeColor = new Color(0.1f, 0.3f, 1f, 0.8f);

    // cache เส้นไว้ สร้างใหม่เฉพาะตอนกราฟเปลี่ยน (Version)
    private int _cachedVersion = -1;
    private Vector3[] _walkLines = new Vector3[0];
    private Vector3[] _upLines = new Vector3[0];
    private Vector3[] _downLines = new Vector3[0];

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!onlyWhenSelected) Draw();
    }

    private void OnDrawGizmosSelected()
    {
        if (onlyWhenSelected) Draw();
    }

    private void Draw()
    {
        if (generator == null) generator = GetComponent<ProceduralMapGenerator>();
        MapNavGraph graph = generator != null ? generator.NavGraph : null;
        if (graph == null) return;
        if (graph.Version != _cachedVersion) Rebuild(graph);

        if (drawWalkEdges && _walkLines.Length > 0) { Gizmos.color = walkColor; Gizmos.DrawLineList(_walkLines); }
        if (drawJumpEdges)
        {
            if (_upLines.Length > 0) { Gizmos.color = jumpUpColor; Gizmos.DrawLineList(_upLines); }
            if (_downLines.Length > 0) { Gizmos.color = jumpDownColor; Gizmos.DrawLineList(_downLines); }
        }

        if (!drawNodes) return;
        Vector3 size = Vector3.one * graph.Spacing * 0.15f;
        for (int i = 0; i < graph.NodeCount; i++)
        {
            var node = graph.GetNode(i);
            Gizmos.color = node.water ? waterNodeColor : walkColor;
            Gizmos.DrawCube(node.position + Vector3.up * heightOffset, size);
        }
    }

    private void Rebuild(MapNavGraph graph)
    {
        _cachedVersion = graph.Version;
        var walk = new List<Vector3>();
        var up = new List<Vector3>();
        var down = new List<Vector3>();
        Vector3 lift = Vector3.up * heightOffset;

        for (int i = 0; i < graph.NodeCount; i++)
        {
            Vector3 a = graph.GetNode(i).position + lift;
            for (int d = 0; d < 8; d++)
            {
                NavEdge edge = graph.GetEdge(i, d, out int j);
                if (edge == NavEdge.None) continue;
                Vector3 b = graph.GetNode(j).position + lift;

                switch (edge)
                {
                    // edge เดินมีทั้งไป-กลับ วาดครั้งเดียวพอ
                    case NavEdge.Walk:
                        if (j > i) { walk.Add(a); walk.Add(b); }
                        break;
                    // กระโดด: วาดเป็นเส้นโค้งหักมุม (ขึ้นแนวตั้งก่อนแล้วไปข้าง) ให้เห็นทิศ
                    case NavEdge.JumpUp:
                        AddJump(up, a, b, 0.35f);
                        break;
                    case NavEdge.JumpDown:
                        AddJump(down, a, b, 0.65f);
                        break;
                }
            }
        }
        _walkLines = walk.ToArray();
        _upLines = up.ToArray();
        _downLines = down.ToArray();
    }

    // เส้นหักจาก a ขึ้นไปยอดโค้ง แล้วลงที่ b / bias = ตำแหน่งยอดตามแนวนอน (ขึ้น/ลงไม่ทับกัน)
    private static void AddJump(List<Vector3> lines, Vector3 a, Vector3 b, float bias)
    {
        Vector3 peak = Vector3.Lerp(a, b, bias);
        peak.y = Mathf.Max(a.y, b.y) + 0.4f;
        lines.Add(a); lines.Add(peak);
        lines.Add(peak); lines.Add(b);
    }
#endif
}

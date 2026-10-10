using UnityEngine;
using UnityEngine.UI;

// รูปหลายเหลี่ยมปกติ (3-6+ ด้าน) เต็มกรอบ RectTransform ใช้เป็นรูปทรง node ของ passive tree เมื่อไม่มี sprite
// สีตาม Graphic.color / ไม่ต้องมี asset (mesh มาจาก VertexHelper ของ canvas ไม่มีอะไรต้อง Destroy เอง)
[RequireComponent(typeof(CanvasRenderer))]
public class PolygonGraphic : MaskableGraphic
{
    [SerializeField, Range(3, 12)] private int _sides = 4;

    public int Sides
    {
        get => _sides;
        set
        {
            value = Mathf.Clamp(value, 3, 12);
            if (_sides == value) return;
            _sides = value;
            SetVerticesDirty();
        }
    }

    // 4 ด้าน = สี่เหลี่ยมวางตรง (ขอบขนานแกน) / อื่นๆ = มียอดชี้ขึ้น (ตรงกับ Tree Editor)
    public static float StartAngle(int sides) => sides == 4 ? 45f : 90f;

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = GetPixelAdjustedRect();
        Vector2 center = r.center;
        Vector2 radius = r.size * 0.5f;
        vh.AddVert(center, color, Vector2.zero);
        float start = StartAngle(_sides) * Mathf.Deg2Rad;
        for (int i = 0; i < _sides; i++)
        {
            float a = start + i * Mathf.PI * 2f / _sides;
            vh.AddVert(center + new Vector2(Mathf.Cos(a) * radius.x, Mathf.Sin(a) * radius.y), color, Vector2.zero);
        }
        for (int i = 0; i < _sides; i++) vh.AddTriangle(0, 1 + i, 1 + (i + 1) % _sides);
    }
}

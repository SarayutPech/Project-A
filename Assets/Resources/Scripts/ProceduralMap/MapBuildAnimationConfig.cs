using System.Collections.Generic;
using UnityEngine;

// layer ของแผนที่ที่ ProceduralMapGenerator สร้าง ให้ MapBuildAnimator เลือก animate ทีละ layer
public enum MapLayer
{
    Ground,    // ผิวพื้นด้านบน
    Underside, // ใต้เกาะ
    Pits,      // ก้นหลุม + ผนังหลุม
    Water,     // ผิวน้ำในหลุม
    Markers,   // จุด Start / End
    Objects,   // object ที่ scatter (ต้นไม้ ก้อนหิน ฯลฯ) ขึ้นทีละตัว
}

// ลำดับที่ object ในแต่ละ layer โผล่ขึ้นมา (มีผลเฉพาะ layer ที่มีหลายชิ้น คือ Markers / Objects)
public enum MapBuildItemOrder
{
    CenterOutward, // จากกลางแผนที่กระจายออกขอบ
    FromStart,     // ไล่จากจุด Start ไปไกลสุด
    Random,
    CreationOrder, // ตามลำดับที่ generator สร้าง
}

[System.Serializable]
public class MapBuildStep
{
    public MapLayer layer = MapLayer.Ground;

    [Tooltip("เปิด = เริ่มหลัง step ก่อนหน้าเล่นจบ / ปิด = เริ่มพร้อม step ก่อนหน้า (ซ้อนกัน) แล้วค่อยนับ Delay")]
    public bool waitForPrevious = true;
    [Tooltip("รอเพิ่มก่อนเริ่ม step นี้ (วินาที)")]
    [Min(0f)] public float delay = 0f;

    [Tooltip("เวลาที่ใช้ยกขึ้นมาจนเข้าที่ ต่อ 1 ชิ้น (วินาที)")]
    [Min(0.01f)] public float duration = 1f;
    [Tooltip("เริ่มต่ำกว่าตำแหน่งจริงเท่าไหร่ (world unit)")]
    [Min(0f)] public float riseDistance = 5f;
    [Tooltip("ขยายจาก 0 ขึ้นมาเป็นขนาดจริงพร้อมกับยกขึ้น (เหมาะกับ object ไม่เหมาะกับพื้นแบนๆ)")]
    public bool scaleIn = false;
    [Tooltip("รูปแบบการเคลื่อน 0..1 ยอมให้เกิน 1 ได้ (เด้งเลยแล้วกลับเข้าที่)")]
    public AnimationCurve curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("หลายชิ้น (Markers / Objects)")]
    [Tooltip("ระยะห่างระหว่างการเริ่มของแต่ละชิ้น (วินาที) 0 = ขึ้นพร้อมกันหมด")]
    [Min(0f)] public float itemInterval = 0.05f;
    public MapBuildItemOrder itemOrder = MapBuildItemOrder.CenterOutward;
}

[CreateAssetMenu(fileName = "MapBuildAnimationConfig", menuName = "Procedural Map/Build Animation Config")]
public class MapBuildAnimationConfig : ScriptableObject
{
    [Tooltip("ตัวคูณความเร็วรวมทุก step (2 = เร็วขึ้น 2 เท่า, 0.5 = ช้าลงครึ่งหนึ่ง)")]
    [Min(0.01f)] public float speed = 1f;
    [Tooltip("รอก่อนเริ่ม step แรก (วินาที)")]
    [Min(0f)] public float startDelay = 0.3f;

    [Tooltip("เล่นตามลำดับบนลงล่าง layer ที่ไม่อยู่ในรายการจะโผล่ทันทีตั้งแต่แรก")]
    public List<MapBuildStep> steps = new List<MapBuildStep>
    {
        new MapBuildStep { layer = MapLayer.Ground, duration = 1.2f, riseDistance = 6f },
        new MapBuildStep { layer = MapLayer.Underside, duration = 1.2f, riseDistance = 10f },
        new MapBuildStep { layer = MapLayer.Pits, duration = 0.6f, riseDistance = 2f },
        new MapBuildStep { layer = MapLayer.Water, duration = 0.8f, riseDistance = 1f },
        new MapBuildStep
        {
            layer = MapLayer.Markers, duration = 0.5f, riseDistance = 3f, scaleIn = true,
            curve = BackOutCurve(), itemInterval = 0.2f, itemOrder = MapBuildItemOrder.CreationOrder,
        },
        new MapBuildStep
        {
            layer = MapLayer.Objects, duration = 0.4f, riseDistance = 2f, scaleIn = true,
            curve = BackOutCurve(), itemInterval = 0.03f, itemOrder = MapBuildItemOrder.CenterOutward,
        },
    };

    // ขึ้นเลยเป้า ~10% แล้วกลับเข้าที่ ให้ object ดูเด้งตอนโผล่
    public static AnimationCurve BackOutCurve() =>
        new AnimationCurve(new Keyframe(0f, 0f, 0f, 3f), new Keyframe(0.7f, 1.1f, 0f, 0f), new Keyframe(1f, 1f));
}

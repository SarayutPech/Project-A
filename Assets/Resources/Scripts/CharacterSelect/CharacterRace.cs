using UnityEngine;

// race 1 ชนิด (asset ละ 1 race) ใส่ใน CharacterCreationConfig.races ก็โผล่ใน carousel หน้าสร้างตัวละครเอง ไม่ต้องแก้ UI
// race กำหนดจุด start ของ passive tree (PlayerPassives ล็อกไว้ เลือกจุด start เองไม่ได้)
[CreateAssetMenu(menuName = "Project-A/Character Race", fileName = "Race")]
public class CharacterRace : ScriptableObject
{
    [Tooltip("id คงที่ เซฟลงตัวละคร (ห้ามเปลี่ยนหลังมีตัวละครใช้ race นี้แล้ว) ว่าง = ใช้ชื่อไฟล์")]
    public string id;
    public string displayName;
    [TextArea(2, 5)] public string description;

    [Header("Art")]
    [Tooltip("รูปการ์ดใน carousel (ขนาดดูใน ArtExport/UI/UI-Art-List.md หัวข้อ Races)")]
    public Sprite portrait;
    [Tooltip("พื้นหลังทั้งจอตอนเลือก race นี้ (ว่าง = พื้นหลังเดิมของ prefab)")]
    public Sprite background;
    [Tooltip("สีชื่อ race / กรอบการ์ดที่เลือก")]
    public Color accentColor = new Color(0.93f, 0.85f, 0.66f);

    [Header("Gameplay")]
    [Tooltip("จุด start ของ passive tree (node ที่ติ๊ก Start ใน Passive Tree Editor)")]
    [PassiveStartNode] public string passiveStartNode;

    public string Id => string.IsNullOrEmpty(id) ? name : id;
    public string DisplayName => string.IsNullOrEmpty(displayName) ? Id : displayName;
}

// ช่อง string ที่เลือกได้เฉพาะ node start ของ passive tree (drawer อยู่ที่ Editor/PassiveStartNodeDrawer)
public class PassiveStartNodeAttribute : PropertyAttribute { }

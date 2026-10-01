using UnityEngine;

// ของหมวด "อื่นๆ": ไม่มีผลตอนถือ ใช้เป็นวัตถุดิบ/ของเควส/ของขาย (ข้อมูลอย่างเดียว)
[CreateAssetMenu(menuName = "Items/Misc", fileName = "Misc_")]
public class MiscItem : ItemDefinition
{
    // ของหมวดอื่นๆ stack ได้ 20 ชิ้นเป็นค่าเริ่มต้น
    public MiscItem() => maxStack = 20;
}

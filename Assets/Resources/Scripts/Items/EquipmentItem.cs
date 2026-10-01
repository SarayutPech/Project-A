using System.Collections.Generic;
using UnityEngine;

// ช่องสวมใส่ของตัวละคร (ค่าตัวเลขห้ามเปลี่ยน เซฟเป็นตัวเลข)
public enum EquipSlot
{
    Weapon = 0,
    OffHand = 1,
    Helmet = 2,
    BodyArmour = 3,
    Gloves = 4,
    Boots = 5,
    Belt = 6,
    Amulet = 7,
    Ring = 8,   // แหวนใส่ได้ทั้งช่อง Ring / Ring2
    Ring2 = 9,
}

// ของสวมใส่: ใส่แล้ว modifier ทั้งหมดเข้า PlayerStats (ถอดแล้วถอดออกทั้งแหล่ง) ผ่าน PlayerEquipment
// stat ใช้ชุดเดียวกับ support gem (StatType) -> ใส่ Damage / Attack Speed / Area ได้ด้วย มีผลกับทุกสกิล
[CreateAssetMenu(menuName = "Items/Equipment", fileName = "Equip_")]
public class EquipmentItem : ItemDefinition
{
    [Tooltip("ช่องที่ใส่ได้ (แหวนตั้ง Ring = ใส่ได้ทั้ง 2 วง)")]
    public EquipSlot slot = EquipSlot.Helmet;
    [Tooltip("ค่าที่ได้ตอนสวม (Flat / Increased% / More% สูตรเดียวกับ gem)")]
    public List<StatModifier> modifiers = new List<StatModifier>();

    public bool CanEquipIn(EquipSlot target)
    {
        if (slot == EquipSlot.Ring || slot == EquipSlot.Ring2) return target == EquipSlot.Ring || target == EquipSlot.Ring2;
        return target == slot;
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        maxStack = 1;
    }
#endif
}

using System.Collections.Generic;
using UnityEngine;

// ของกดใช้ (ยา / ม้วนบัฟ): ใช้แล้วหมดไป 1 ชิ้น ผลตัดสินฝั่ง server ใน PlayerConsumables (ที่นี่แค่ข้อมูล)
[CreateAssetMenu(menuName = "Items/Consumable", fileName = "Consumable_")]
public class ConsumableItem : ItemDefinition
{
    [Header("Heal")]
    [Tooltip("เติม HP ตรงๆ")]
    [Min(0f)] public float healFlat = 0f;
    [Tooltip("เติม HP เป็น % ของ Max HP (20 = 20%)")]
    [Min(0f)] public float healPercent = 0f;

    [Header("Buff")]
    [Tooltip("modifier ชั่วคราว เข้า PlayerStats ตอนใช้ หมดเวลาแล้วถอดเอง (ใช้ซ้ำ = รีเวลาใหม่ ไม่ซ้อน)")]
    public List<StatModifier> buffs = new List<StatModifier>();
    [Min(0f)] public float buffDuration = 5f;

    [Header("Use")]
    [Tooltip("ใช้ไอเทมชนิดนี้ซ้ำได้อีกหลังกี่วินาที (นับแยกต่อชนิด)")]
    [Min(0f)] public float cooldown = 1f;

    public bool HasHeal => healFlat > 0f || healPercent > 0f;
    public bool HasBuff => buffs != null && buffs.Count > 0 && buffDuration > 0f;

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        if (maxStack < 1) maxStack = 1;
    }
#endif
}

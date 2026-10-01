using System.Collections.Generic;
using UnityEngine;

// ของที่ player สวมอยู่ (simulation ฝั่ง server) ใส่/ถอดแล้ว modifier เข้า/ออก PlayerStats ทั้งชุดต่อช่อง
// แหล่งของ modifier = "equip:<ช่อง>" -> ถอดช่องไหนก็ถอดเฉพาะของช่องนั้น
// ภายหลังทำ Netcode: client ส่งคำขอ "ใส่ไอเทม id X ช่อง Y" -> server เช็คว่ามีของนั้นใน inventory จริงก่อนเรียก Equip (กฎข้อ 8)
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerStats))]
public class PlayerEquipment : MonoBehaviour
{
    [System.Serializable]
    public struct SlotEntry
    {
        public EquipSlot slot;
        public EquipmentItem item;
    }

    [Tooltip("ของที่ใส่ตอนเริ่ม (ทดสอบ) ช่องว่าง = ใช้ช่องของไอเทม")]
    public List<SlotEntry> startingEquipment = new List<SlotEntry>();

    public event System.Action<PlayerEquipment, EquipSlot> Changed;

    private readonly Dictionary<EquipSlot, EquipmentItem> _equipped = new Dictionary<EquipSlot, EquipmentItem>();
    private PlayerStats _stats;

    public IReadOnlyDictionary<EquipSlot, EquipmentItem> Equipped => _equipped;

    private void Awake()
    {
        _stats = GetComponent<PlayerStats>();
        foreach (var e in startingEquipment)
            if (e.item != null) Equip(e.item, e.item.CanEquipIn(e.slot) ? e.slot : e.item.slot, out _);
    }

    public EquipmentItem Get(EquipSlot slot) => _equipped.TryGetValue(slot, out var item) ? item : null;

    // ใส่ไอเทมลงช่อง (ถอดของเดิมออกให้ คืนผ่าน previous ให้ผู้เรียกเอากลับเข้า inventory)
    public bool Equip(EquipmentItem item, EquipSlot slot, out EquipmentItem previous)
    {
        previous = null;
        if (item == null || !item.CanEquipIn(slot)) return false;

        previous = Unequip(slot);
        _equipped[slot] = item;
        string source = Source(slot);
        foreach (var mod in item.modifiers) _stats.AddModifier(source, mod);
        Changed?.Invoke(this, slot);
        return true;
    }

    // ใส่ช่องของไอเทมเอง (แหวน: ช่อง Ring ก่อน เต็มแล้วใส่ Ring2)
    public bool Equip(EquipmentItem item, out EquipmentItem previous)
    {
        previous = null;
        if (item == null) return false;
        EquipSlot slot = item.slot;
        if (item.CanEquipIn(EquipSlot.Ring)) slot = Get(EquipSlot.Ring) == null ? EquipSlot.Ring : EquipSlot.Ring2;
        return Equip(item, slot, out previous);
    }

    // ถอดของในช่อง คืนไอเทมที่ถอด (ว่าง = null)
    public EquipmentItem Unequip(EquipSlot slot)
    {
        if (!_equipped.TryGetValue(slot, out var item)) return null;
        _equipped.Remove(slot);
        _stats.RemoveModifiers(Source(slot));
        Changed?.Invoke(this, slot);
        return item;
    }

    private static string Source(EquipSlot slot) => "equip:" + slot;
}

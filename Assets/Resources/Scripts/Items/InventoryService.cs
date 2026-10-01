using System.Collections.Generic;
using UnityEngine;

// ของที่สวมอยู่ 1 ช่อง (เซฟเป็นตัวเลขช่อง + ไอเทมชิ้นนั้น)
[System.Serializable]
public struct EquippedItem
{
    public EquipSlot slot;
    public ItemInstance item;
}

// gem ที่ใส่อยู่ในช่องสกิล: skillSlot = ช่องของ PlayerSkills / socket = ActiveSocket (gem หลัก) หรือ 0-4 (support)
[System.Serializable]
public struct SocketedGem
{
    public const int ActiveSocket = -1;

    public int skillSlot;
    public int socket;
    public ItemInstance item;
}

// กระเป๋า + ของที่สวมของผู้เล่นแต่ละคน (ที่เก็บข้อมูลอย่างเดียว ไม่ตัดสินกติกาเกม)
// กติกา (น้ำหนัก, ช่องที่ใส่ได้, cooldown ยา) อยู่ที่ PlayerInventory ฝั่ง server แล้วค่อยเรียก service นี้
// ตอนนี้ implement แบบ local JSON (LocalInventoryService) ภายหลังเป็น backend โดยโค้ดที่ใช้ไม่ต้องแก้
// การย้ายของหลายจุด (ใส่/ถอด = กระเป๋า <-> ช่องสวม) เป็นเมธอดเดียว = transaction เดียว (กฎข้อ 7)
public interface IInventoryService
{
    // ownerId ที่ของเปลี่ยน
    event System.Action<string> Changed;

    // เคยมีข้อมูลของผู้เล่นนี้แล้วไหม (false = ตัวละครใหม่ ใช้ให้ของเริ่มต้นครั้งแรก)
    bool HasData(string ownerId);
    IReadOnlyList<ItemInstance> GetItems(string ownerId);
    IReadOnlyList<EquippedItem> GetEquipped(string ownerId);
    bool TryGetItem(string ownerId, string instanceId, out ItemInstance item);

    // ---- ฝั่ง server เท่านั้น ----
    // เพิ่มไอเทมเข้ากระเป๋า: ของที่ stack ได้เติมช่องเดิมที่ยังไม่เต็มก่อน ที่เหลือออก instanceId ใหม่
    // คืนช่องสุดท้ายที่ได้ของ (ของไม่ stack = ชิ้นใหม่ที่สร้าง)
    ItemInstance Add(string ownerId, string itemId, int count = 1);
    // เอาทั้งช่องออก (ทั้ง stack)
    bool Remove(string ownerId, string instanceId, out ItemInstance removed);
    // ลดจำนวนในช่อง (หมดแล้วช่องหาย) ไม่พอ = false
    bool RemoveCount(string ownerId, string instanceId, int count);
    void Sort(string ownerId);
    // รวม stack ที่ไม่เต็ม แล้วเรียงกระเป๋า (หมวด -> rarity สูงก่อน -> ชื่อ -> level สูงก่อน)
    // ย้ายชิ้นในกระเป๋าไปช่องสวม ของเดิมในช่อง (ถ้ามี) กลับเข้ากระเป๋า
    bool Equip(string ownerId, string instanceId, EquipSlot slot, out ItemInstance previous);
    // ถอดของในช่องกลับเข้ากระเป๋า
    bool Unequip(string ownerId, EquipSlot slot, out ItemInstance removed);

    // gem ในช่องสกิล (ย้าย กระเป๋า <-> ช่อง เป็น transaction เดียวเหมือนของสวมใส่)
    IReadOnlyList<SocketedGem> GetSocketed(string ownerId);
    bool Socket(string ownerId, string instanceId, int skillSlot, int socket, out ItemInstance previous);
    bool Unsocket(string ownerId, int skillSlot, int socket, out ItemInstance removed);
    // สร้างข้อมูลผู้เล่นใหม่ (ว่าง) ให้ HasData เป็น true
    void EnsureCreated(string ownerId);
    // ลบข้อมูลทั้งหมดของผู้เล่น (ตอนลบตัวละคร) ฝั่ง server เท่านั้น
    void DeleteOwner(string ownerId);
}

// เก็บในเครื่องเป็น JSON (persistentDataPath/inventory.json) เซฟทุกครั้งที่เปลี่ยน
public class LocalInventoryService : IInventoryService
{
    [System.Serializable]
    private class OwnerData
    {
        public string ownerId;
        public List<ItemInstance> items = new List<ItemInstance>();
        public List<EquippedItem> equipped = new List<EquippedItem>();
        public List<SocketedGem> socketed = new List<SocketedGem>();
    }

    [System.Serializable]
    private class SaveData
    {
        public List<OwnerData> owners = new List<OwnerData>();
    }

    public event System.Action<string> Changed;

    private readonly string _fileName;
    private readonly SaveData _data;

    public LocalInventoryService(string fileName = "inventory.json")
    {
        _fileName = fileName;
        _data = LocalJson.Load<SaveData>(fileName);
        // เซฟเก่า (ก่อนมี stack) / maxStack เปลี่ยน -> รวม stack ให้ตรงกติกาปัจจุบัน (ไม่เปลี่ยนลำดับ)
        foreach (var owner in _data.owners) MergeStacks(owner.items);
    }

    public bool HasData(string ownerId) => GetOwner(ownerId, false) != null;

    public IReadOnlyList<ItemInstance> GetItems(string ownerId)
    {
        var owner = GetOwner(ownerId, false);
        return owner != null ? owner.items : (IReadOnlyList<ItemInstance>)System.Array.Empty<ItemInstance>();
    }

    public IReadOnlyList<EquippedItem> GetEquipped(string ownerId)
    {
        var owner = GetOwner(ownerId, false);
        return owner != null ? owner.equipped : (IReadOnlyList<EquippedItem>)System.Array.Empty<EquippedItem>();
    }

    public bool TryGetItem(string ownerId, string instanceId, out ItemInstance item)
    {
        item = default;
        var owner = GetOwner(ownerId, false);
        if (owner == null) return false;
        int i = owner.items.FindIndex(x => x.instanceId == instanceId);
        if (i < 0) return false;
        item = owner.items[i];
        return true;
    }

    public ItemInstance Add(string ownerId, string itemId, int count = 1)
    {
        var owner = GetOwner(ownerId, true);
        var def = ItemDatabase.Get(itemId);
        int limit = def != null ? def.StackLimit : 1;
        int left = Mathf.Max(1, count);
        ItemInstance last = default;

        // เติม stack เดิมที่ยังไม่เต็มก่อน
        if (limit > 1)
        {
            for (int i = 0; i < owner.items.Count && left > 0; i++)
            {
                var it = owner.items[i];
                if (it.itemId != itemId || it.Count >= limit) continue;
                int add = Mathf.Min(left, limit - it.Count);
                it.count = it.Count + add;
                owner.items[i] = it;
                left -= add;
                last = it;
            }
        }
        // ที่เหลือเป็นช่องใหม่ (ไม่ stack = ทีละ 1 ชิ้น)
        while (left > 0)
        {
            int n = Mathf.Min(left, limit);
            last = new ItemInstance(System.Guid.NewGuid().ToString("N"), itemId, 1, n);
            owner.items.Add(last);
            left -= n;
        }
        Commit(ownerId);
        return last;
    }

    public void Sort(string ownerId)
    {
        var owner = GetOwner(ownerId, false);
        if (owner == null) return;
        MergeStacks(owner.items);
        owner.items.Sort(CompareForSort);
        Commit(ownerId);
    }

    private static int CompareForSort(ItemInstance a, ItemInstance b)
    {
        var da = a.Definition;
        var db = b.Definition;
        if (da == null || db == null) return (da == null).CompareTo(db == null); // ไอเทมที่หา definition ไม่เจอไปท้าย
        int c = da.Category.CompareTo(db.Category);
        if (c != 0) return c;
        c = db.rarity.CompareTo(da.rarity);
        if (c != 0) return c;
        c = string.Compare(da.displayName, db.displayName, System.StringComparison.OrdinalIgnoreCase);
        if (c != 0) return c;
        c = string.CompareOrdinal(a.itemId, b.itemId);
        if (c != 0) return c;
        c = b.Level.CompareTo(a.Level);
        return c != 0 ? c : b.Count.CompareTo(a.Count);
    }

    // รวมช่องของชนิดเดียวกันที่ stack ได้ให้เต็มทีละช่อง (ช่องแรกที่เจอเป็นตัวรับ ช่องที่ว่างแล้วถูกลบ)
    private static void MergeStacks(List<ItemInstance> items)
    {
        for (int i = 0; i < items.Count; i++)
        {
            var def = items[i].Definition;
            int limit = def != null ? def.StackLimit : 1;
            var target = items[i];
            // เกินที่อนุญาต (เช่น maxStack ลดลง / ของไม่ stack แต่ count > 1) -> แตกส่วนเกินเป็นช่องใหม่ (วนมาตรวจต่อ)
            if (target.Count > limit)
            {
                int extra = target.Count - limit;
                target.count = limit;
                items[i] = target;
                items.Insert(i + 1, new ItemInstance(System.Guid.NewGuid().ToString("N"), target.itemId, 1, extra));
                continue;
            }
            for (int j = i + 1; j < items.Count && target.Count < limit; j++)
            {
                if (items[j].itemId != target.itemId) continue;
                var other = items[j];
                int move = Mathf.Min(limit - target.Count, other.Count);
                target.count = target.Count + move;
                other.count = other.Count - move;
                if (other.count <= 0) items.RemoveAt(j--);
                else items[j] = other;
            }
            items[i] = target;
        }
    }

    public bool RemoveCount(string ownerId, string instanceId, int count)
    {
        var owner = GetOwner(ownerId, false);
        if (owner == null || count <= 0) return false;
        int i = owner.items.FindIndex(x => x.instanceId == instanceId);
        if (i < 0 || owner.items[i].Count < count) return false;
        var it = owner.items[i];
        if (it.Count == count) owner.items.RemoveAt(i);
        else
        {
            it.count = it.Count - count;
            owner.items[i] = it;
        }
        Commit(ownerId);
        return true;
    }

    public bool Remove(string ownerId, string instanceId, out ItemInstance removed)
    {
        removed = default;
        var owner = GetOwner(ownerId, false);
        if (owner == null) return false;
        int i = owner.items.FindIndex(x => x.instanceId == instanceId);
        if (i < 0) return false;
        removed = owner.items[i];
        owner.items.RemoveAt(i);
        Commit(ownerId);
        return true;
    }

    public bool Equip(string ownerId, string instanceId, EquipSlot slot, out ItemInstance previous)
    {
        previous = default;
        var owner = GetOwner(ownerId, false);
        if (owner == null) return false;
        int i = owner.items.FindIndex(x => x.instanceId == instanceId);
        if (i < 0) return false;

        ItemInstance item = owner.items[i];
        owner.items.RemoveAt(i);
        int e = owner.equipped.FindIndex(x => x.slot == slot);
        if (e >= 0)
        {
            previous = owner.equipped[e].item;
            owner.items.Insert(i, previous); // ของเดิมกลับไปที่ตำแหน่งของชิ้นที่ใส่ (ไม่กระโดดไปท้ายรายการ)
            owner.equipped[e] = new EquippedItem { slot = slot, item = item };
        }
        else owner.equipped.Add(new EquippedItem { slot = slot, item = item });
        Commit(ownerId);
        return true;
    }

    public bool Unequip(string ownerId, EquipSlot slot, out ItemInstance removed)
    {
        removed = default;
        var owner = GetOwner(ownerId, false);
        if (owner == null) return false;
        int e = owner.equipped.FindIndex(x => x.slot == slot);
        if (e < 0) return false;
        removed = owner.equipped[e].item;
        owner.equipped.RemoveAt(e);
        owner.items.Add(removed);
        Commit(ownerId);
        return true;
    }

    public IReadOnlyList<SocketedGem> GetSocketed(string ownerId)
    {
        var owner = GetOwner(ownerId, false);
        return owner != null ? owner.socketed : (IReadOnlyList<SocketedGem>)System.Array.Empty<SocketedGem>();
    }

    public bool Socket(string ownerId, string instanceId, int skillSlot, int socket, out ItemInstance previous)
    {
        previous = default;
        var owner = GetOwner(ownerId, false);
        if (owner == null) return false;
        int i = owner.items.FindIndex(x => x.instanceId == instanceId);
        if (i < 0) return false;

        ItemInstance item = owner.items[i];
        owner.items.RemoveAt(i);
        var entry = new SocketedGem { skillSlot = skillSlot, socket = socket, item = item };
        int s = owner.socketed.FindIndex(x => x.skillSlot == skillSlot && x.socket == socket);
        if (s >= 0)
        {
            previous = owner.socketed[s].item;
            owner.items.Insert(i, previous);
            owner.socketed[s] = entry;
        }
        else owner.socketed.Add(entry);
        Commit(ownerId);
        return true;
    }

    public bool Unsocket(string ownerId, int skillSlot, int socket, out ItemInstance removed)
    {
        removed = default;
        var owner = GetOwner(ownerId, false);
        if (owner == null) return false;
        int s = owner.socketed.FindIndex(x => x.skillSlot == skillSlot && x.socket == socket);
        if (s < 0) return false;
        removed = owner.socketed[s].item;
        owner.socketed.RemoveAt(s);
        owner.items.Add(removed);
        Commit(ownerId);
        return true;
    }

    public void EnsureCreated(string ownerId)
    {
        if (HasData(ownerId)) return;
        GetOwner(ownerId, true);
        Commit(ownerId);
    }

    public void DeleteOwner(string ownerId)
    {
        if (_data.owners.RemoveAll(o => o.ownerId == ownerId) > 0) Commit(ownerId);
    }

    private void Commit(string ownerId)
    {
        LocalJson.Save(_fileName, _data);
        Changed?.Invoke(ownerId);
    }

    private OwnerData GetOwner(string ownerId, bool create)
    {
        var owner = _data.owners.Find(o => o.ownerId == ownerId);
        if (owner == null && create)
        {
            owner = new OwnerData { ownerId = ownerId };
            _data.owners.Add(owner);
        }
        return owner;
    }
}

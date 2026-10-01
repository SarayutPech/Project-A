using System.Collections.Generic;
using System.IO;
using UnityEngine;

// ของที่ดรอป 1 ชิ้น: server สร้างพร้อม dropId -> ผู้เล่นขอเก็บด้วย id ได้ครั้งเดียว (กฎข้อ 7 drop -> claim)
[System.Serializable]
public struct LootDrop
{
    public string dropId;
    public ItemStack item;
}

// กล่องรับของดรอปของผู้เล่นแต่ละคน (ของที่ดรอปใน map มารอที่ hideout)
// ตอนนี้ implement แบบ local JSON (LocalLootStash) ภายหลังเปลี่ยนเป็น backend โดยโค้ดที่ใช้ไม่ต้องแก้
public interface ILootStashService
{
    // ownerId ที่ของเปลี่ยน
    event System.Action<string> Changed;

    // ฝั่ง server เท่านั้น: สร้าง drop ใหม่ให้ผู้เล่น
    LootDrop Add(string ownerId, ItemStack item);
    IReadOnlyList<LootDrop> GetPending(string ownerId);
    // เอาของออกจากกล่อง (เก็บเข้า inventory) สำเร็จได้ครั้งเดียวต่อ dropId
    bool TryClaim(string ownerId, string dropId, out LootDrop drop);
    // ฝั่ง server เท่านั้น: ทิ้งของที่ยังไม่เก็บทั้งหมด (เช่นเข้า map ใหม่) คืนจำนวน drop ที่ลบ
    int Clear(string ownerId);
}

// เก็บในเครื่องเป็น JSON (persistentDataPath/loot_stash.json) เซฟทุกครั้งที่เปลี่ยน (ของน้อย ไฟล์เล็ก)
public class LocalLootStash : ILootStashService
{
    [System.Serializable]
    private class OwnerData
    {
        public string ownerId;
        public List<LootDrop> drops = new List<LootDrop>();
    }

    [System.Serializable]
    private class SaveData
    {
        public List<OwnerData> owners = new List<OwnerData>();
    }

    public event System.Action<string> Changed;

    private readonly string _path;
    private SaveData _data;

    public LocalLootStash(string fileName = "loot_stash.json")
    {
        _path = Path.Combine(Application.persistentDataPath, fileName);
        Load();
    }

    public LootDrop Add(string ownerId, ItemStack item)
    {
        var drop = new LootDrop { dropId = System.Guid.NewGuid().ToString("N"), item = item };
        GetOwner(ownerId, true).drops.Add(drop);
        Save();
        Changed?.Invoke(ownerId);
        return drop;
    }

    public IReadOnlyList<LootDrop> GetPending(string ownerId)
    {
        var owner = GetOwner(ownerId, false);
        return owner != null ? owner.drops : (IReadOnlyList<LootDrop>)System.Array.Empty<LootDrop>();
    }

    public bool TryClaim(string ownerId, string dropId, out LootDrop drop)
    {
        drop = default;
        var owner = GetOwner(ownerId, false);
        if (owner == null) return false;
        int index = owner.drops.FindIndex(d => d.dropId == dropId);
        if (index < 0) return false;

        drop = owner.drops[index];
        owner.drops.RemoveAt(index);
        Save();
        Changed?.Invoke(ownerId);
        return true;
    }

    public int Clear(string ownerId)
    {
        var owner = GetOwner(ownerId, false);
        if (owner == null || owner.drops.Count == 0) return 0;

        int count = owner.drops.Count;
        owner.drops.Clear();
        Save();
        Changed?.Invoke(ownerId);
        return count;
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

    private void Load()
    {
        _data = new SaveData();
        if (!File.Exists(_path)) return;
        try
        {
            _data = JsonUtility.FromJson<SaveData>(File.ReadAllText(_path)) ?? new SaveData();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[{nameof(LocalLootStash)}] อ่าน {_path} ไม่ได้: {e.Message}");
        }
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(_path, JsonUtility.ToJson(_data));
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[{nameof(LocalLootStash)}] เซฟ {_path} ไม่ได้: {e.Message}");
        }
        LocalJson.Flush(); // WebGL: ลง IndexedDB
    }
}

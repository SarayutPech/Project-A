using System.Collections.Generic;
using UnityEngine;

// แปลง item id -> ItemDefinition (ข้อมูลกลางอ่านอย่างเดียว เหมือนกันทุก instance/ทุกเครื่อง จึงเป็น static ได้)
// โหลดครั้งแรกที่ใช้ จาก Resources/Gameobject/ScriptAbleObject/Items (รวมโฟลเดอร์ย่อย)
public static class ItemDatabase
{
    public const string ResourcesPath = "Gameobject/ScriptAbleObject/Items";
    public const string GemResourcesPath = "Gameobject/ScriptAbleObject/Skills"; // skill/support gem ก็เป็นไอเทม

    private static Dictionary<string, ItemDefinition> _byId;

    public static IEnumerable<ItemDefinition> All
    {
        get
        {
            EnsureLoaded();
            return _byId.Values;
        }
    }

    public static ItemDefinition Get(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        EnsureLoaded();
        return _byId.TryGetValue(id, out var def) ? def : null;
    }

    public static T Get<T>(string id) where T : ItemDefinition => Get(id) as T;

    private static void EnsureLoaded()
    {
        if (_byId != null) return;
        _byId = new Dictionary<string, ItemDefinition>();
        var all = new List<ItemDefinition>(Resources.LoadAll<ItemDefinition>(ResourcesPath));
        all.AddRange(Resources.LoadAll<ItemDefinition>(GemResourcesPath));
        foreach (var def in all)
        {
            if (string.IsNullOrEmpty(def.id))
            {
                Debug.LogWarning($"[{nameof(ItemDatabase)}] '{def.name}' ไม่มี id", def);
                continue;
            }
            if (_byId.TryGetValue(def.id, out var other))
            {
                Debug.LogError($"[{nameof(ItemDatabase)}] id '{def.id}' ซ้ำกัน: '{other.name}' กับ '{def.name}'", def);
                continue;
            }
            _byId.Add(def.id, def);
        }
    }

    // Enter Play Mode แบบไม่ reload domain -> ล้าง cache ให้โหลดใหม่ (เผื่อเพิ่ม/แก้ asset ระหว่างนั้น)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCache() => _byId = null;
}

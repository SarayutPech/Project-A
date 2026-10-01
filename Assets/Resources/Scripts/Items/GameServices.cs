using System.IO;
using UnityEngine;

// ตัวกลางหา service ข้อมูลผู้เล่น (ตอนนี้ตัวเดียวทั้ง process) เปลี่ยน implementation ได้ก่อนเริ่มเกม
// เช่น GameServices.Inventory = new BackendInventoryService(...) โค้ดที่ใช้ไม่ต้องแก้ (กฎข้อ 7)
public static class GameServices
{
    // id ของผู้เล่นที่เครื่องนี้ควบคุม ภายหลังทำ Netcode/บัญชี ใช้ account id / clientId แทน
    public const string LocalPlayerId = "local";

    private static ILootStashService _lootStash;
    public static ILootStashService LootStash
    {
        get => _lootStash ??= new LocalLootStash();
        set => _lootStash = value;
    }

    private static IInventoryService _inventory;
    public static IInventoryService Inventory
    {
        get => _inventory ??= new LocalInventoryService();
        set => _inventory = value;
    }

    private static ICharacterService _character;
    public static ICharacterService Character
    {
        get => _character ??= new LocalCharacterService();
        set => _character = value;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetServices()
    {
        _lootStash = null;
        _inventory = null;
        _character = null;
    }
}

// อ่าน/เขียน JSON ใน persistentDataPath ให้ service แบบ local ใช้ร่วมกัน
public static class LocalJson
{
    public static T Load<T>(string fileName) where T : new()
    {
        string path = Path.Combine(Application.persistentDataPath, fileName);
        if (!File.Exists(path)) return new T();
        try
        {
            var data = JsonUtility.FromJson<T>(File.ReadAllText(path));
            return data != null ? data : new T();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[{nameof(LocalJson)}] อ่าน {path} ไม่ได้: {e.Message}");
            return new T();
        }
    }

    public static void Save<T>(string fileName, T data)
    {
        string path = Path.Combine(Application.persistentDataPath, fileName);
        try
        {
            File.WriteAllText(path, JsonUtility.ToJson(data));
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[{nameof(LocalJson)}] เซฟ {path} ไม่ได้: {e.Message}");
        }
    }
}

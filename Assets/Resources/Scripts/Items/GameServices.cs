using System.IO;
using UnityEngine;

// ตัวกลางหา service ข้อมูลผู้เล่น (ตอนนี้ตัวเดียวทั้ง process) เปลี่ยน implementation ได้ก่อนเริ่มเกม
// เช่น GameServices.Inventory = new BackendInventoryService(...) โค้ดที่ใช้ไม่ต้องแก้ (กฎข้อ 7)
public static class GameServices
{
    // บัญชีของเครื่องนี้ (ภายหลังได้จาก login/backend) ตัวละครทุกตัวในหน้าเลือกตัวผูกกับ id นี้
    public const string LocalAccountId = "local";
    // ตัวละครเมื่อไม่ได้ผ่านหน้าเลือกตัว (กด Play ใน Hideout ตรงๆ / เซฟก่อนมีหน้าเลือกตัว)
    public const string DefaultPlayerId = "local";

    // id ของตัวละครที่เครื่องนี้ควบคุม (= ownerId ของ inventory / progress / passive / loot)
    // หน้าเลือกตัวละครตั้งค่าก่อนโหลด Hideout / ภายหลังทำ Netcode ใช้ id ตัวละครของ client แต่ละคนแทน
    public static string LocalPlayerId { get; set; } = DefaultPlayerId;

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

    // checkpoint (เข้า Hideout / กลับหน้าเลือกตัว): service แบบ local เขียนไฟล์ทุกครั้งที่ข้อมูลเปลี่ยนอยู่แล้ว
    // ที่นี่ = ยืนยันลงที่เก็บถาวร (WebGL: IndexedDB) + setting ใน PlayerPrefs / ภายหลังมี backend = จุด commit
    public static void SaveAll()
    {
        LocalJson.Flush();
        PlayerPrefs.Save();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetServices()
    {
        _lootStash = null;
        _inventory = null;
        _character = null;
        LocalPlayerId = DefaultPlayerId;
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
        Flush();
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern void ProjectA_SyncFS(); // Assets/Plugins/WebGL/LocalSaveSync.jslib
#endif

    // WebGL: ไฟล์ใน persistentDataPath อยู่ใน memory จนกว่าจะ sync ลง IndexedDB (ไม่ sync = F5 แล้วเซฟหาย)
    // sync แบบ async และรวบรอบที่ซ้อนกัน (เรียกถี่ได้) / platform อื่นเขียนลงดิสก์ทันทีอยู่แล้ว ไม่ต้องทำอะไร
    public static void Flush()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        ProjectA_SyncFS();
#endif
    }
}

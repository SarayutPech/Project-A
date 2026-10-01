using UnityEngine;

public enum ItemRarity
{
    Normal,
    Magic,
    Rare,
    Unique,
}

// ไอเทม 1 ชนิด (ScriptableObject = ข้อมูลกลาง ไม่ใช่ของที่ผู้เล่นถือ) ของที่ผู้เล่นถือจริงคือ ItemStack (id + จำนวน)
// id ใช้เซฟ/ส่งผ่านเน็ต/เก็บใน DB แทนการอ้าง asset ตรงๆ (กฎข้อ 7) แปลง id กลับเป็น asset ผ่าน ItemDatabase
// asset ต้องอยู่ใต้ Gameobject/ScriptAbleObject/Items (ItemDatabase โหลดจากโฟลเดอร์นี้)
public abstract class ItemDefinition : ScriptableObject
{
    [Tooltip("รหัสคงที่ ใช้เซฟ/sync (ห้ามเปลี่ยนหลังมีผู้เล่นใช้แล้ว)")]
    public string id;
    public string displayName;
    [TextArea(2, 4)] public string description;
    public Sprite icon;
    public ItemRarity rarity = ItemRarity.Normal;
    [Tooltip("ซ้อนกันได้สูงสุดกี่ชิ้นต่อช่อง (ของสวมใส่ = 1)")]
    [Min(1)] public int maxStack = 1;

    // สีชื่อตาม rarity แบบ PoE (ฝั่ง UI ใช้)
    public static Color RarityColor(ItemRarity rarity) => rarity switch
    {
        ItemRarity.Magic => new Color(0.53f, 0.53f, 1f),
        ItemRarity.Rare => new Color(1f, 1f, 0.47f),
        ItemRarity.Unique => new Color(0.69f, 0.38f, 0.15f) * 1.4f,
        _ => new Color(0.86f, 0.86f, 0.86f),
    };

#if UNITY_EDITOR
    protected virtual void OnValidate()
    {
        if (string.IsNullOrEmpty(id)) id = name;
        if (string.IsNullOrEmpty(displayName)) displayName = name;
    }
#endif
}

// ไอเทมที่ผู้เล่นถือ/ได้รับ: id ชนิด + จำนวน (เก็บ id ไม่อ้าง asset -> เซฟเป็น JSON / ส่งผ่านเน็ต / เก็บ DB ได้ตรงๆ)
// ภายหลังไอเทมสุ่ม mod (แบบ PoE) ให้เพิ่ม field ค่าที่สุ่มได้ตรงนี้
[System.Serializable]
public struct ItemStack
{
    public string itemId;
    [Min(1)] public int count;

    public ItemStack(string itemId, int count = 1)
    {
        this.itemId = itemId;
        this.count = count;
    }

    public ItemDefinition Definition => ItemDatabase.Get(itemId);
    public bool IsEmpty => string.IsNullOrEmpty(itemId) || count <= 0;
}

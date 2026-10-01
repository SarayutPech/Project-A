using UnityEngine;

public enum ItemRarity
{
    Normal,
    Magic,
    Rare,
    Unique,
}

// หมวดของกระเป๋า (ค่าตัวเลขห้ามเปลี่ยน)
public enum ItemCategory
{
    Equipment = 0, // ของสวมใส่
    Usable = 1,    // กดใช้
    Other = 2,     // อื่นๆ (วัตถุดิบ, ของเควส ฯลฯ)
    SkillGem = 3,  // active skill gem
    SupportGem = 4,
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
    [Tooltip("ซ้อนกันได้สูงสุดกี่ชิ้นต่อช่อง ใช้กับของหมวด กดใช้ / อื่นๆ เท่านั้น (ของสวมใส่ / gem ไม่ stack แยกเป็นชิ้นเสมอ)")]
    [Min(1)] public int maxStack = 1;
    [Tooltip("น้ำหนักต่อชิ้น (Byte) กระเป๋ารับได้ตาม Carry Capacity ของ PlayerStats")]
    [Min(0)] public int weight = 16;

    // หมวดในกระเป๋า (ของสวมใส่ / กดใช้ / อื่นๆ) แต่ละคลาสลูกกำหนดเอง
    public virtual ItemCategory Category => ItemCategory.Other;

    // stack ในกระเป๋าได้ไหม / ได้สูงสุดกี่ชิ้นต่อช่อง
    public bool IsStackable => (Category == ItemCategory.Usable || Category == ItemCategory.Other) && maxStack > 1;
    public int StackLimit => IsStackable ? maxStack : 1;

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

// ไอเทม 1 ช่องในกระเป๋า/ที่สวมอยู่: ของสวมใส่ / gem = 1 ชิ้นต่อช่อง / ของกดใช้ / อื่นๆ = stack ได้ถึง StackLimit (count)
// instanceId ออกโดย server/backend ตอนของเข้ากระเป๋า client ส่งคำขอด้วย id นี้ ("ใส่ชิ้น X", "ใช้ชิ้น Y")
// ภายหลังไอเทมสุ่ม mod (แบบ PoE) เก็บค่าที่สุ่มได้ใน struct นี้
[System.Serializable]
public struct ItemInstance
{
    public string instanceId;
    public string itemId;
    [Tooltip("level ของ gem (ไอเทมอื่นไม่ใช้) 0 = 1")]
    public int level;
    [Tooltip("จำนวนในช่องนี้ (ของที่ stack ได้) 0 = 1")]
    public int count;

    public ItemInstance(string instanceId, string itemId, int level = 1, int count = 1)
    {
        this.instanceId = instanceId;
        this.itemId = itemId;
        this.level = level;
        this.count = count;
    }

    public ItemDefinition Definition => ItemDatabase.Get(itemId);
    public int Level => Mathf.Max(1, level);
    public int Count => Mathf.Max(1, count);
    public bool IsEmpty => string.IsNullOrEmpty(instanceId);
}

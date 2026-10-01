using UnityEngine;

// ค่าทุกชนิดที่ modifier ปรับได้ ใช้ชุดเดียวกันทั้ง stat sheet ของตัวละคร / support gem / elite
// (ค่าตัวเลขของแต่ละตัวห้ามเปลี่ยน asset เก็บเป็นตัวเลข)
public enum StatType
{
    // ---- ใช้กับสกิล (support gem ปรับได้ / stat sheet ปรับได้ทุกสกิล) ----
    Damage = 0,               // ดาเมจต่อ hitbox (ก่อนคูณตัวคูณของ hitbox)
    AttackSpeed = 1,          // ความเร็วตี (ฐาน = Attack Speed ของ stat sheet, ไม่มี sheet = 1)
    MoveSpeedWhileUsing = 2,  // ตัวคูณความเร็วเดินระหว่างใช้ (ฐาน = 1 - Move Slow % ของสกิล)
    AreaOfEffect = 3,         // ตัวคูณพื้นที่ hitbox (ฐาน 1 / รัศมีโต √ ของค่านี้ แบบ PoE)

    // ---- ของตัวละคร (stat sheet เท่านั้น support gem ใส่ไปก็ไม่มีผล) ----
    MaxHealth = 10,
    MovementSpeed = 11,
    MaxMana = 12,
    ManaRegen = 13,           // มานาฟื้นต่อวินาที
    CarryCapacity = 14,       // น้ำหนักที่กระเป๋ารับได้ (Byte)
    Strength = 15,            // Str: +Life / +Damage (อัตราใน PlayerStats)
    Dexterity = 16,           // Dex: +Attack Speed
    Intelligence = 17,        // Int: +Mana
}

public enum ModifierType
{
    Flat,       // + ค่าตรงๆ (บวกกับฐาน)
    Increased,  // + % แบบบวกกัน (รวมทุกอันก่อนแล้วคูณครั้งเดียว)
    More,       // x % แบบคูณแยกทีละอัน
}

// ตัวปรับค่า 1 อัน: ค่าตาม level = value + valuePerLevel * (level - 1)
[System.Serializable]
public struct StatModifier
{
    public StatType stat;
    public ModifierType type;
    [Tooltip("Flat = หน่วยตรงๆ (เช่น Attack Speed +0.2) / Increased, More = เปอร์เซ็นต์ (20 = 20%)")]
    public float value;
    [Tooltip("เพิ่มต่อ level ของ gem (level 1 = value)")]
    public float valuePerLevel;
    [Tooltip("ใช้กับ Damage แบบ Flat เท่านั้น: ดาเมจสูงสุด (value = ต่ำสุด) เช่น value 3 / valueMax 7 = Adds 3-7 Damage\n0 หรือน้อยกว่า value = ค่าเดียว")]
    public float valueMax;

    public StatModifier(StatType stat, ModifierType type, float value, float valuePerLevel = 0f)
    {
        this.stat = stat;
        this.type = type;
        this.value = value;
        this.valuePerLevel = valuePerLevel;
        valueMax = 0f;
    }

    public float Evaluate(int level) => value + valuePerLevel * (Mathf.Max(1, level) - 1);

    // ค่าสูงสุดของช่วง (Flat Damage แบบ min-max) ค่าอื่น = เท่ากับ Evaluate
    public float EvaluateMax(int level) => Mathf.Max(value, valueMax) + valuePerLevel * (Mathf.Max(1, level) - 1);
    public bool IsRange => valueMax > value;

    public static StatModifier FlatRange(StatType stat, float min, float max, float perLevel = 0f) =>
        new StatModifier(stat, ModifierType.Flat, min, perLevel) { valueMax = max };
}

// gem 1 ชนิด = ไอเทมชนิดหนึ่ง (id / ชื่อ / คำอธิบาย / ไอคอน / น้ำหนัก มาจาก ItemDefinition) -> อยู่ในกระเป๋าได้ ดรอปได้
// ของที่ผู้เล่นถือจริงคือ ItemInstance ในกระเป๋า (มี level) ใส่ช่องสกิลแล้ว PlayerSkills ใช้เป็น GemInstance (ชนิด + level)
// asset อยู่ใต้ Gameobject/ScriptAbleObject/Skills (ItemDatabase โหลดโฟลเดอร์นี้ด้วย) id ต้องไม่ซ้ำกับไอเทมอื่น
public abstract class SkillGem : ItemDefinition
{
    public Color gemColor = Color.red;
    [Min(1)] public int maxLevel = 20;

    // gem หนัก 1 Byte (ค่าเริ่มต้นของ asset ใหม่และ asset เก่าที่ยังไม่มี field น้ำหนัก)
    protected SkillGem() => weight = 1;
}

// gem ที่ผู้เล่นถือ/ใส่ช่อง: ชนิด + level
[System.Serializable]
public struct GemInstance
{
    public SkillGem gem;
    [Min(1)] public int level;

    public GemInstance(SkillGem gem, int level = 1)
    {
        this.gem = gem;
        this.level = level;
    }

    public bool IsEmpty => gem == null;
    public int ClampedLevel => gem != null ? Mathf.Clamp(level, 1, gem.maxLevel) : 1;
}

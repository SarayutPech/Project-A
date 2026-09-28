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

    public StatModifier(StatType stat, ModifierType type, float value, float valuePerLevel = 0f)
    {
        this.stat = stat;
        this.type = type;
        this.value = value;
        this.valuePerLevel = valuePerLevel;
    }

    public float Evaluate(int level) => value + valuePerLevel * (Mathf.Max(1, level) - 1);
}

// gem 1 ชนิด (ScriptableObject = ข้อมูลกลาง ไม่ใช่ของที่ผู้เล่นถือ) ของที่ผู้เล่นถือจริงคือ GemInstance (ชนิด + level)
// id ใช้เซฟ/ส่งผ่านเน็ต/เก็บใน DB แทนการอ้าง asset ตรงๆ (กฎข้อ 7)
public abstract class SkillGem : ScriptableObject
{
    [Tooltip("รหัสคงที่ ใช้เซฟ/sync (ห้ามเปลี่ยนหลังมีผู้เล่นใช้แล้ว)")]
    public string id;
    public string displayName;
    [TextArea(2, 4)] public string description;
    public Color gemColor = Color.red;
    public Sprite icon;
    [Min(1)] public int maxLevel = 20;

#if UNITY_EDITOR
    protected virtual void OnValidate()
    {
        if (string.IsNullOrEmpty(id)) id = name;
        if (string.IsNullOrEmpty(displayName)) displayName = name;
    }
#endif
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

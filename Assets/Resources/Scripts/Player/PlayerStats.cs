using System.Collections.Generic;
using UnityEngine;

// modifier 1 อันพร้อมแหล่งที่มา (ถอดทีเดียวทั้งแหล่งได้ เช่นถอดไอเทม / buff หมดเวลา)
[System.Serializable]
public struct SourcedModifier
{
    [Tooltip("แหล่งที่มา เช่น \"item:helmet_01\" / \"buff:war_cry\" ใช้ถอดทีหลัง")]
    public string source;
    public StatModifier modifier;

    public SourcedModifier(string source, StatModifier modifier)
    {
        this.source = source;
        this.modifier = modifier;
    }
}

// stat sheet ของ player (simulation ฝั่ง server) ที่เดียวที่ถือค่าตัวละคร: ค่าฐาน + modifier ทุกแหล่ง
// ระบบอื่นแค่ AddModifier / RemoveModifiers แล้ว sheet คำนวณใหม่แล้วส่งค่าไปให้:
//   Max Health -> Health / Movement Speed -> PlayerMovement / Attack Speed + ค่าสกิล (Damage, Area ฯลฯ) -> PlayerSkills ทุก slot
// สูตรเดียวกับ support gem (StatMath): (ฐาน + Σflat) × (1 + Σincreased%) × Π(1 + more%)
// Attack Speed ของ sheet = ฐานของทุกสกิล แล้ว support gem ของแต่ละ slot บวก/คูณต่อ -> ท่า/hitbox/cooldown/animation เร็วตาม
//
// ภายหลังทำ Netcode: ค่าสุดท้ายที่ client ต้องเห็น (HP, move speed) sync เป็น NetworkVariable / modifier คิดฝั่ง server เท่านั้น
[DisallowMultipleComponent]
public class PlayerStats : MonoBehaviour
{
    [Header("Base")]
    [Min(1f)] public float baseMaxHealth = 100f;
    [Tooltip("ความเร็วเดิน (หน่วย/วินาที) วิ่ง/dash คิดจากค่านี้ตาม multiplier ใน PlayerMovement")]
    [Min(0f)] public float baseMovementSpeed = 5f;
    [Tooltip("ความเร็วตีฐานของตัวละคร (1 = ท่าเล่นตามความยาวใน gem) ทุกสกิลใช้เป็นฐาน")]
    [Min(0.1f)] public float baseAttackSpeed = 1f;

    [Header("Modifiers")]
    [Tooltip("modifier ถาวร/ทดสอบ ใส่ใน Inspector ได้ (ตอนเล่นแก้แล้วมีผลทันที) ของจากระบบอื่นใช้ AddModifier")]
    public List<SourcedModifier> modifiers = new List<SourcedModifier>();

    // ค่าสุดท้าย
    public float MaxHealth => Get(StatType.MaxHealth);
    public float MovementSpeed => Get(StatType.MovementSpeed);
    public float AttackSpeed => Get(StatType.AttackSpeed);

    // เปลี่ยนเมื่อไหร่ก็ได้ (ใส่/ถอด modifier, แก้ค่าฐาน) ระบบที่ใช้ค่า sheet ฟังตรงนี้
    public event System.Action<PlayerStats> Changed;

    private readonly List<SourcedModifier> _runtime = new List<SourcedModifier>();
    private readonly List<(StatModifier mod, int level)> _all = new List<(StatModifier, int)>();
    private readonly List<StatModifier> _skillMods = new List<StatModifier>();
    private bool _dirty = true;
    private bool _applied;

    private Health _health;
    private PlayerMovement _movement;

    private void Awake()
    {
        _health = GetComponent<Health>();
        _movement = GetComponent<PlayerMovement>();
        ApplyToComponents(true);
    }

    // ---------- API ----------

    public void AddModifier(string source, StatModifier modifier)
    {
        _runtime.Add(new SourcedModifier(source, modifier));
        MarkChanged();
    }

    // ถอดทุก modifier ของแหล่งนี้ คืนจำนวนที่ถอด
    public int RemoveModifiers(string source)
    {
        int removed = _runtime.RemoveAll(m => m.source == source);
        if (removed > 0) MarkChanged();
        return removed;
    }

    public float Get(StatType stat)
    {
        Rebuild();
        float baseValue = stat switch
        {
            StatType.MaxHealth => baseMaxHealth,
            StatType.MovementSpeed => baseMovementSpeed,
            StatType.AttackSpeed => baseAttackSpeed,
            StatType.AreaOfEffect => 1f,
            _ => 0f,
        };
        float value = StatMath.Apply(baseValue, stat, _all);
        return stat switch
        {
            StatType.MaxHealth => Mathf.Max(1f, value),
            StatType.MovementSpeed => Mathf.Max(0f, value),
            StatType.AttackSpeed => Mathf.Max(0.1f, value),
            _ => value,
        };
    }

    // modifier ที่มีผลกับสกิลทุก slot (Damage, Area, Move Speed ตอนใช้สกิล) ไม่รวม Attack Speed (ส่งเป็นฐานแยก)
    public IReadOnlyList<StatModifier> SkillModifiers
    {
        get
        {
            Rebuild();
            return _skillMods;
        }
    }

    // ---------- Internal ----------

    private void MarkChanged()
    {
        _dirty = true;
        if (!_applied) return; // ยังไม่ Awake (ยังไม่มี component ปลายทาง)
        ApplyToComponents(false);
        Changed?.Invoke(this);
    }

    private void Rebuild()
    {
        if (!_dirty) return;
        _dirty = false;
        _all.Clear();
        _skillMods.Clear();
        foreach (var m in modifiers) Add(m.modifier);
        foreach (var m in _runtime) Add(m.modifier);

        void Add(StatModifier mod)
        {
            _all.Add((mod, 1));
            if (!StatMath.IsCharacterStat(mod.stat) && mod.stat != StatType.AttackSpeed) _skillMods.Add(mod);
        }
    }

    // ส่งค่าไปให้ component ที่ใช้ (ครั้งแรกเติม HP เต็ม / ครั้งต่อไปคงสัดส่วน HP เดิม)
    private void ApplyToComponents(bool first)
    {
        _applied = true;
        if (_health != null) _health.SetMaxHealth(MaxHealth, first);
        if (_movement != null) _movement.moveSpeed = MovementSpeed;
    }

    [ContextMenu("Log Final Stats")]
    private void LogStats()
    {
        Debug.Log($"[{nameof(PlayerStats)}] MaxHealth={MaxHealth:0.#} MovementSpeed={MovementSpeed:0.##} AttackSpeed={AttackSpeed:0.##} " +
                  $"Damage(flat/inc)={StatMath.Apply(0f, StatType.Damage, _all):0.#} Area={Get(StatType.AreaOfEffect):0.##} modifiers={_all.Count}", this);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // แก้ค่าใน Inspector ระหว่างเล่น -> คำนวณใหม่ทันที
        if (Application.isPlaying && _applied) MarkChanged();
        else _dirty = true;
    }
#endif
}

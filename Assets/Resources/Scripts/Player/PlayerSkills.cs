using System.Collections.Generic;
using UnityEngine;

// skill slot 1 ช่อง: active gem 1 อัน + support gem ของตัวเอง 5 ช่อง (link ของ PoE)
// ปุ่มของแต่ละ slot อยู่ที่ PlayerAttackInput (ฝั่ง client) ช่องที่ index ตรงกัน
[System.Serializable]
public class SkillSlot
{
    [Tooltip("ชื่อไว้ดูใน Inspector/UI")]
    public string name = "Skill";
    public GemInstance activeGem = new GemInstance(null, 1);
    [Tooltip("support gem ของ slot นี้ (สูงสุด 5) gem ชนิดเดียวกันใส่ซ้ำได้แต่มีผลครั้งเดียว")]
    public GemInstance[] supportGems = new GemInstance[PlayerSkills.MaxSupportSlots];

    // ค่าสุดท้ายของ slot นี้ (null = ช่องว่าง)
    public ResolvedSkill Resolved { get; internal set; }
    public bool IsEmpty => activeGem.gem == null;
}

// ชุดสกิลของ player (simulation ฝั่ง server): หลาย slot แต่ละ slot มี active gem + support 5 ช่องของตัวเอง
// ค่าสกิลทุก slot = gem + support ของ slot นั้น + PlayerStats (Attack Speed เป็นฐาน / Damage, Area ฯลฯ ทุกสกิล)
// คำนวณใหม่ทุกครั้งที่ใส่/ถอด gem หรือ stat sheet เปลี่ยน
//
// ตอนนี้ตั้งค่าใน Inspector (ตอนเล่นแก้ช่องได้ มีผลทันที) ภายหลัง gem มาจาก inventory ของผู้เล่นผ่าน service (กฎข้อ 7):
// client ส่งคำขอ "ใส่ gem id X ที่ slot S ช่อง N" -> server เช็คว่ามี gem นั้นจริง แล้วค่อยเรียก SetActiveGem / SocketSupport
[RequireComponent(typeof(MeleeAttack))]
public class PlayerSkills : MonoBehaviour
{
    public const int MaxSupportSlots = 5;
    public const int MaxSkillSlots = 10; // 2 ชุด x 5 ช่อง (กด Ctrl ค้าง = ชุดที่ 2 ดู PlayerAttackInput)

    public List<SkillSlot> slots = new List<SkillSlot>
    {
        new SkillSlot { name = "Skill 1" },
        new SkillSlot { name = "Skill 2" },
        new SkillSlot { name = "Skill 3" },
        new SkillSlot { name = "Skill 4" },
    };

    public event System.Action<PlayerSkills> Changed;

    private MeleeAttack _attack;
    private PlayerStats _stats;

    public MeleeAttack Attack => _attack != null ? _attack : _attack = GetComponent<MeleeAttack>();
    public int SlotCount => slots.Count;

    private void Awake()
    {
        _stats = GetComponent<PlayerStats>();
        ResolveAll();
    }

    private void OnEnable()
    {
        if (_stats != null) _stats.Changed += OnStatsChanged;
    }

    private void OnDisable()
    {
        if (_stats != null) _stats.Changed -= OnStatsChanged;
    }

    private void OnStatsChanged(PlayerStats stats) => ResolveAll();

    public SkillSlot GetSlot(int index) => index >= 0 && index < slots.Count ? slots[index] : null;

    // ---------- ใส่/ถอด gem (server) ----------

    public bool SetActiveGem(int slot, ActiveSkillGem gem, int level = 1)
    {
        var s = GetSlot(slot);
        if (s == null) return false;
        s.activeGem = new GemInstance(gem, level);
        ResolveAll();
        return true;
    }

    // ใส่ support ที่ slot ช่อง socket (0-4)
    public bool SocketSupport(int slot, int socket, SupportGem gem, int level = 1)
    {
        var s = GetSlot(slot);
        if (s == null || socket < 0 || socket >= MaxSupportSlots) return false;
        EnsureSockets(s);
        s.supportGems[socket] = new GemInstance(gem, level);
        ResolveAll();
        return true;
    }

    public bool RemoveSupport(int slot, int socket) => SocketSupport(slot, socket, null);

    // ---------- คำนวณ ----------

    [ContextMenu("Resolve All")]
    public void ResolveAll()
    {
        float baseAttackSpeed = _stats != null ? _stats.AttackSpeed : 1f;
        IReadOnlyList<StatModifier> statMods = _stats != null ? _stats.SkillModifiers : null;

        foreach (var s in slots)
        {
            EnsureSockets(s);
            var gem = s.activeGem.gem as ActiveSkillGem;
            if (s.activeGem.gem != null && gem == null)
                Debug.LogWarning($"[{nameof(PlayerSkills)}] '{s.activeGem.gem.name}' ไม่ใช่ Active Skill Gem ใส่ช่องสกิลหลักของ '{s.name}' ไม่ได้", this);
            s.Resolved = ResolvedSkill.Resolve(gem, s.activeGem.ClampedLevel, s.supportGems, statMods, baseAttackSpeed);
        }
        Changed?.Invoke(this);
    }

    // ช่อง support ครบ 5 เสมอ (ข้อมูลเก่า/แก้ใน Inspector แล้วขนาดเปลี่ยน)
    private static void EnsureSockets(SkillSlot s)
    {
        if (s.supportGems != null && s.supportGems.Length == MaxSupportSlots) return;
        var sockets = new GemInstance[MaxSupportSlots];
        if (s.supportGems != null) System.Array.Copy(s.supportGems, sockets, Mathf.Min(s.supportGems.Length, MaxSupportSlots));
        s.supportGems = sockets;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (slots.Count > MaxSkillSlots) slots.RemoveRange(MaxSkillSlots, slots.Count - MaxSkillSlots);
        foreach (var s in slots)
        {
            EnsureSockets(s);
            if (s.activeGem.level < 1) s.activeGem.level = 1;
            for (int i = 0; i < s.supportGems.Length; i++)
            {
                if (s.supportGems[i].level < 1) s.supportGems[i].level = 1;
                if (s.supportGems[i].gem != null && !(s.supportGems[i].gem is SupportGem))
                {
                    Debug.LogWarning($"[{nameof(PlayerSkills)}] '{s.supportGems[i].gem.name}' ไม่ใช่ Support Gem ถอดออกจาก '{s.name}' ช่อง {i + 1}", this);
                    s.supportGems[i].gem = null;
                }
            }
        }
        // แก้ช่องระหว่างเล่น -> มีผลทันที
        if (Application.isPlaying) ResolveAll();
    }
#endif
}

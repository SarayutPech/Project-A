using System.Text;
using TMPro;
using UnityEngine;

// หน้าต่าง Character Sheet (กด C): ช่องสวมใส่ 11 ช่อง (หมวก ชุด ถุงมือ รองเท้า อาวุธ อาวุธรอง แหวน x2 สร้อย เข็มขัด ผ้าคลุม)
// + ค่าสถานะจาก PlayerStats (อ่านอย่างเดียว) คลิกขวาที่ช่อง = ขอถอด (PlayerInventory.RequestUnequip)
public class CharacterWindow : UIWindow<CharacterWindow>, IPlayerUI
{
    public EquipSlotView[] slots;
    public Sprite fallbackIcon;

    [Header("Info")]
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI levelText;
    [Tooltip("คอลัมน์ชื่อ stat (ชิดซ้าย)")]
    public TextMeshProUGUI statNames;
    [Tooltip("คอลัมน์ค่า stat (ชิดขวา) บรรทัดตรงกับคอลัมน์ชื่อ")]
    public TextMeshProUGUI statValues;
    public string characterName = "Exile";

    private PlayerInventory _inventory;
    private PlayerStats _stats;
    private PlayerExperience _exp;
    private Health _health;
    private Mana _mana;
    private readonly StringBuilder _names = new StringBuilder();
    private readonly StringBuilder _values = new StringBuilder();

    protected override void Awake()
    {
        base.Awake();
        if (!IsInstance) return;
        foreach (var s in slots)
            if (s != null) s.Activated = OnSlotActivated;
    }

    public void Bind(GameObject player)
    {
        if (_inventory != null) _inventory.Changed -= OnInventoryChanged;
        if (_stats != null) _stats.Changed -= OnStatsChanged;
        if (_exp != null) _exp.Changed -= OnExpChanged;

        _inventory = player != null ? player.GetComponent<PlayerInventory>() : null;
        _stats = player != null ? player.GetComponent<PlayerStats>() : null;
        _exp = player != null ? player.GetComponent<PlayerExperience>() : null;
        _health = player != null ? player.GetComponent<Health>() : null;
        _mana = player != null ? player.GetComponent<Mana>() : null;

        if (_inventory != null) _inventory.Changed += OnInventoryChanged;
        if (_stats != null) _stats.Changed += OnStatsChanged;
        if (_exp != null) _exp.Changed += OnExpChanged;
        Refresh();
    }

    protected override void OnOpenChanged(bool open)
    {
        if (open) Refresh();
    }

    private void OnInventoryChanged(PlayerInventory _) { if (IsOpen) RefreshSlots(); }
    private void OnStatsChanged(PlayerStats _) { if (IsOpen) RefreshStats(); }
    private void OnExpChanged(PlayerExperience _) { if (IsOpen) RefreshStats(); }

    public void Refresh()
    {
        RefreshSlots();
        RefreshStats();
    }

    private void RefreshSlots()
    {
        foreach (var s in slots)
            if (s != null) s.Set(_inventory != null ? _inventory.GetEquipped(s.slot) : null, fallbackIcon);
    }

    private void RefreshStats()
    {
        if (nameText != null) nameText.text = characterName;
        if (levelText != null) levelText.text = _exp != null ? $"Level {_exp.Level}" : "";
        if (_stats == null || statNames == null || statValues == null) return;

        _names.Clear();
        _values.Clear();
        if (_exp != null)
            Row("Experience", _exp.IsMaxLevel ? "MAX" : $"{_exp.Experience} / {_exp.ExpToNextLevel}");
        Gap();
        Row("Life", $"{(_health != null ? Mathf.CeilToInt(_health.Current) : 0)} / {_stats.MaxHealth:0}");
        Row("Mana", $"{(_mana != null ? Mathf.FloorToInt(_mana.Current) : 0)} / {_stats.MaxMana:0}");
        Row("Mana Regeneration", $"{_stats.ManaRegen:0.#} /s");
        Gap();
        Row("Strength", $"{_stats.Get(StatType.Strength):0}");
        Row("Dexterity", $"{_stats.Get(StatType.Dexterity):0}");
        Row("Intelligence", $"{_stats.Get(StatType.Intelligence):0}");
        Gap();
        Row("Attack Speed", $"{_stats.AttackSpeed:0.##}x");
        Vector2 added = _stats.SumFlatRange(StatType.Damage);
        Row("Added Damage", added.y > 0f ? StatMath.FormatRange(added.x, added.y) : "0");
        Row("Increased Damage", $"{_stats.Sum(StatType.Damage, ModifierType.Increased):+0.#;-0.#;0}%");
        Row("More Damage", $"{_stats.Sum(StatType.Damage, ModifierType.More):+0.#;-0.#;0}%");
        Row("Area of Effect", $"{_stats.Get(StatType.AreaOfEffect) * 100f:0}%");
        Gap();
        Row("Movement Speed", $"{_stats.MovementSpeed:0.##}");
        Row("Carry Capacity", $"{_stats.CarryCapacity:0} B");

        statNames.text = _names.ToString();
        statValues.text = _values.ToString();
    }

    private void Row(string name, string value)
    {
        _names.Append(name).Append('\n');
        _values.Append(value).Append('\n');
    }

    private void Gap()
    {
        _names.Append("<size=40%>\n</size>");
        _values.Append("<size=40%>\n</size>");
    }

    private void Update()
    {
        // HP/มานาเปลี่ยนตลอด -> อัปเดตตัวเลขช้าๆ ตอนเปิดอยู่
        if (IsOpen && Time.unscaledTime >= _nextTick)
        {
            _nextTick = Time.unscaledTime + 0.25f;
            RefreshStats();
        }
    }

    private float _nextTick;

    private void OnSlotActivated(EquipSlotView view)
    {
        if (_inventory == null) return;
        _inventory.RequestUnequip(view.slot);
    }
}

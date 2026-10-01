using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// หน้าต่าง Skill Gems (กด K หรือคลิกช่องสกิลบน HUD): จัดการ gem หลัก + support (link) ของแต่ละช่องสกิล
//   ซ้าย = รายการช่องสกิลทั้งหมด (ชุด I / II) / กลาง = ช่อง gem หลัก + 5 support พร้อมเส้น link + ค่าสกิลสุดท้าย
//   ขวา = gem ในกระเป๋าที่ใส่ช่องที่เลือกได้ (ค้นหาชื่อได้) คลิก = ใส่ / คลิกขวาที่ช่อง = ถอด
// UI ส่งคำขอ PlayerInventory.RequestSocketGem / RequestUnsocketGem เท่านั้น (server ตรวจชนิด gem / ช่อง / น้ำหนัก)
public class SkillGemWindow : UIWindow<SkillGemWindow>, IPlayerUI
{
    [Header("Skill Slots (left)")]
    public RectTransform slotListContent;
    [Tooltip("แถวต้นแบบ (ปิดไว้) clone ตามจำนวนช่องสกิล")]
    public SkillSlotListItem slotTemplate;
    public string[] setNames = { "I", "II" };

    [Header("Sockets (center)")]
    public TextMeshProUGUI slotTitle;
    public GemSocketView activeSocket;
    public GemSocketView[] supportSockets = new GemSocketView[PlayerSkills.MaxSupportSlots];
    [Tooltip("เส้น link จาก gem หลักไป support แต่ละช่อง (ลำดับเดียวกับ Support Sockets) ติดสีเมื่อทั้งสองฝั่งมี gem")]
    public Image[] links = new Image[PlayerSkills.MaxSupportSlots];
    public Color linkOn = new Color(0.93f, 0.85f, 0.66f, 1f);
    public Color linkOff = new Color(1f, 1f, 1f, 0.12f);
    [Tooltip("ค่าสุดท้ายของสกิลช่องนี้ (ดาเมจ ความเร็ว ฯลฯ)")]
    public TextMeshProUGUI resolvedText;

    [Header("Gem Picker (right)")]
    public TextMeshProUGUI pickerTitle;
    public TMP_InputField searchField;
    public RectTransform pickerContent;
    public InventoryRowView pickerRowTemplate;
    public TextMeshProUGUI pickerEmptyText;
    public Sprite fallbackIcon;

    public int SelectedSlot { get; private set; }
    public int SelectedSocket { get; private set; } = SocketedGem.ActiveSocket;

    private PlayerInventory _inventory;
    private PlayerSkills _skills;
    private PlayerAttackInput _input;
    private readonly List<SkillSlotListItem> _slotItems = new List<SkillSlotListItem>();
    private readonly List<InventoryRowView> _pickerRows = new List<InventoryRowView>();
    private readonly List<ItemInstance> _candidates = new List<ItemInstance>();
    private readonly StringBuilder _sb = new StringBuilder();

    protected override void Awake()
    {
        base.Awake();
        if (!IsInstance) return;
        if (slotTemplate != null) slotTemplate.gameObject.SetActive(false);
        if (pickerRowTemplate != null) pickerRowTemplate.gameObject.SetActive(false);
        if (activeSocket != null)
        {
            activeSocket.socket = SocketedGem.ActiveSocket;
            activeSocket.Clicked = OnSocketClicked;
            activeSocket.RightClicked = OnSocketRightClicked;
        }
        for (int i = 0; i < supportSockets.Length; i++)
        {
            if (supportSockets[i] == null) continue;
            supportSockets[i].socket = i;
            supportSockets[i].Clicked = OnSocketClicked;
            supportSockets[i].RightClicked = OnSocketRightClicked;
        }
        if (searchField != null) searchField.onValueChanged.AddListener(OnSearchChanged);
    }

    protected override void OnDestroy()
    {
        if (searchField != null) searchField.onValueChanged.RemoveListener(OnSearchChanged);
        base.OnDestroy();
    }

    public void Bind(GameObject player)
    {
        if (_inventory != null) _inventory.Changed -= OnDataChanged;
        if (_skills != null) _skills.Changed -= OnSkillsChanged;
        _inventory = player != null ? player.GetComponent<PlayerInventory>() : null;
        _skills = player != null ? player.GetComponent<PlayerSkills>() : null;
        _input = player != null ? player.GetComponent<PlayerAttackInput>() : null;
        if (_inventory != null) _inventory.Changed += OnDataChanged;
        if (_skills != null) _skills.Changed += OnSkillsChanged;
        Refresh();
    }

    // เปิดที่ช่องสกิลนี้ (จากคลิกช่องบน HUD)
    public void OpenFor(int slotIndex)
    {
        SelectSlot(slotIndex);
        Open();
    }

    public void SelectSlot(int slotIndex)
    {
        SelectedSlot = Mathf.Max(0, slotIndex);
        SelectedSocket = SocketedGem.ActiveSocket;
        Refresh();
    }

    protected override void OnOpenChanged(bool open)
    {
        if (open) Refresh();
    }

    private void OnDataChanged(PlayerInventory _) { if (IsOpen) Refresh(); }
    private void OnSkillsChanged(PlayerSkills _) { if (IsOpen) RefreshResolved(); }
    private void OnSearchChanged(string _) => RefreshPicker();

    // ---------- คลิก ----------

    private void OnSocketClicked(GemSocketView view)
    {
        SelectedSocket = view.socket;
        Refresh();
    }

    private void OnSocketRightClicked(GemSocketView view)
    {
        if (_inventory != null && view.Item.HasValue && !_inventory.RequestUnsocketGem(SelectedSlot, view.socket)) UIToast.Show("Cannot remove gem");
    }

    private void OnPickGem(InventoryRowView row)
    {
        if (_inventory == null) return;
        if (!_inventory.RequestSocketGem(row.Item.instanceId, SelectedSlot, SelectedSocket))
        {
            UIToast.Show("Cannot socket this gem here");
            return;
        }
        if (ItemTooltip.Instance != null) ItemTooltip.Instance.Hide();
        // ใส่แล้วเลื่อนไปช่อง support ว่างช่องถัดไป (ใส่ link ต่อได้เลย)
        int next = FirstEmptySupport();
        if (next >= 0) SelectedSocket = next;
        Refresh();
    }

    private int FirstEmptySupport()
    {
        for (int i = 0; i < PlayerSkills.MaxSupportSlots; i++)
            if (!_inventory.GetSocketed(SelectedSlot, i).HasValue) return i;
        return -1;
    }

    // ---------- แสดงผล ----------

    public void Refresh()
    {
        if (_inventory == null || _skills == null) return;
        if (SelectedSlot >= _skills.SlotCount) SelectedSlot = 0;
        RefreshSlotList();
        RefreshSockets();
        RefreshResolved();
        RefreshPicker();
    }

    private string KeyOf(int slot)
    {
        int perSet = _input != null && _input.SlotsPerSet > 0 ? _input.SlotsPerSet : PlayerAttackInput.DefaultSlotsPerSet;
        int set = slot / perSet, button = slot % perSet;
        string setName = set < setNames.Length ? setNames[set] : (set + 1).ToString();
        string key = _input != null ? _input.KeyLabel(button) : (button + 1).ToString();
        return $"{setName} · {key}";
    }

    private void RefreshSlotList()
    {
        if (slotListContent == null || slotTemplate == null) return;
        int count = _skills.SlotCount;
        while (_slotItems.Count < count)
        {
            var item = Instantiate(slotTemplate, slotListContent);
            item.gameObject.SetActive(true);
            int index = _slotItems.Count;
            if (item.button != null) item.button.onClick.AddListener(() => SelectSlot(index));
            _slotItems.Add(item);
        }
        for (int i = 0; i < _slotItems.Count; i++)
        {
            bool used = i < count;
            _slotItems[i].gameObject.SetActive(used);
            if (!used) continue;
            var active = _inventory.GetSocketed(i, SocketedGem.ActiveSocket);
            int supports = 0;
            for (int s = 0; s < PlayerSkills.MaxSupportSlots; s++) if (_inventory.GetSocketed(i, s).HasValue) supports++;
            _slotItems[i].Set(i, KeyOf(i), active.HasValue ? active.Value.Definition as ActiveSkillGem : null, supports, i == SelectedSlot);
        }
    }

    private void RefreshSockets()
    {
        if (slotTitle != null) slotTitle.text = $"Skill Slot  {KeyOf(SelectedSlot)}";
        var active = _inventory.GetSocketed(SelectedSlot, SocketedGem.ActiveSocket);
        if (activeSocket != null) activeSocket.Set(active, SelectedSocket == SocketedGem.ActiveSocket);
        for (int i = 0; i < supportSockets.Length; i++)
        {
            var support = _inventory.GetSocketed(SelectedSlot, i);
            if (supportSockets[i] != null) supportSockets[i].Set(support, SelectedSocket == i);
            if (i < links.Length && links[i] != null) links[i].color = active.HasValue && support.HasValue ? linkOn : linkOff;
        }
    }

    private void RefreshResolved()
    {
        if (resolvedText == null || _skills == null) return;
        var slot = _skills.GetSlot(SelectedSlot);
        var r = slot != null ? slot.Resolved : null;
        if (r == null || r.Gem == null)
        {
            resolvedText.text = "<color=#777>No skill gem</color>";
            return;
        }
        _sb.Clear();
        _sb.Append($"Damage  <color=#FFF>{StatMath.FormatRange(r.DamageMin, r.DamageMax)}</color>\n");
        _sb.Append($"Attack Speed  <color=#FFF>{r.AttackSpeed:0.##}x</color>  ({r.ScaledDuration:0.##}s)\n");
        _sb.Append($"Area  <color=#FFF>{r.AreaMultiplier * 100f:0}%</color>\n");
        if (r.ScaledCooldown > 0.01f) _sb.Append($"Cooldown  <color=#FFF>{r.ScaledCooldown:0.##}s</color>\n");
        _sb.Append($"Move Speed while using  <color=#FFF>{r.MoveSpeedMultiplier * 100f:0}%</color>");
        resolvedText.text = _sb.ToString();
    }

    private void RefreshPicker()
    {
        if (_inventory == null || pickerContent == null || pickerRowTemplate == null) return;
        bool wantActive = SelectedSocket == SocketedGem.ActiveSocket;
        if (pickerTitle != null) pickerTitle.text = wantActive ? "Skill Gems" : $"Support Gems  <color=#888>(socket {SelectedSocket + 1})</color>";

        string query = searchField != null ? searchField.text : "";
        _candidates.Clear();
        foreach (var item in _inventory.Items)
        {
            var def = item.Definition;
            bool fits = wantActive ? def is ActiveSkillGem : def is SupportGem;
            if (fits && SearchQuery.Matches(def.displayName, query)) _candidates.Add(item);
        }

        while (_pickerRows.Count < _candidates.Count)
        {
            var row = Instantiate(pickerRowTemplate, pickerContent);
            row.Clicked = OnPickGem;
            row.hintOverride = "Click to socket";
            _pickerRows.Add(row);
        }
        for (int i = 0; i < _pickerRows.Count; i++)
        {
            bool used = i < _candidates.Count;
            if (_pickerRows[i].gameObject.activeSelf != used) _pickerRows[i].gameObject.SetActive(used);
            if (used) _pickerRows[i].Set(_candidates[i], fallbackIcon);
        }
        if (pickerEmptyText != null)
        {
            pickerEmptyText.gameObject.SetActive(_candidates.Count == 0);
            pickerEmptyText.text = string.IsNullOrWhiteSpace(query) ? "No gems in bag" : "No match";
        }
    }
}

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// หน้าต่างกระเป๋า (กด I): แท็บ ของสวมใส่ / กดใช้ / อื่นๆ / Skill Gem / Support Gem
// รายการเป็นแถว [ไอคอน] ชื่อ เรียงลงมา ไม่ stack (1 แถว = 1 ชิ้น) / ช่อง search ต่อแท็บ (จำคำค้นแยกแต่ละแท็บ)
// ท้ายหน้าต่าง = น้ำหนักรวม / ที่รับได้ (Byte)
// คลิกซ้ายค้างลากแถวออกไปปล่อยนอก UI (ลงพื้น) = ทิ้งทั้งช่อง (RequestDiscard) / ปล่อยบน UI = ยกเลิก
// UI แค่อ่าน PlayerInventory และส่งคำขอ RequestEquip / RequestUse ไม่แก้ข้อมูลเอง
public class InventoryWindow : UIWindow<InventoryWindow>, IPlayerUI
{
    [System.Serializable]
    public struct Tab
    {
        public ItemCategory category;
        public Button button;
        public TextMeshProUGUI label;
        [Tooltip("แสดงตอนแท็บนี้ถูกเลือก (เช่นเส้นใต้/พื้นสว่าง)")]
        public GameObject selected;
        public string title;
    }

    public Tab[] tabs =
    {
        new Tab { category = ItemCategory.Equipment, title = "Equip" },
        new Tab { category = ItemCategory.Usable, title = "Usable" },
        new Tab { category = ItemCategory.Other, title = "Other" },
        new Tab { category = ItemCategory.SkillGem, title = "Skill" },
        new Tab { category = ItemCategory.SupportGem, title = "Support" },
    };

    [Header("Search")]
    [Tooltip("ช่องค้นหาชื่อ (เว้นวรรค = อะไรก็ได้คั่น เช่น \"A ppl\" เจอ Apple)")]
    public TMP_InputField searchField;
    [Tooltip("ปุ่มล้างคำค้น (เว้นว่างได้)")]
    public Button clearSearchButton;
    [Tooltip("ปุ่มเรียง + รวม stack (เว้นว่างได้)")]
    public Button sortButton;

    [Header("List")]
    public RectTransform listContent;
    [Tooltip("แถวต้นแบบ (ปิดไว้) ถูก clone ตามจำนวนไอเทม")]
    public InventoryRowView rowTemplate;
    public TextMeshProUGUI emptyText;
    public Sprite fallbackIcon;

    [Header("Drag")]
    [Tooltip("ไอคอนที่ตามเมาส์ตอนลาก (ไม่รับ raycast) เว้นว่าง = สร้างให้ตอนเล่น")]
    public Image dragGhost;
    [Tooltip("สีไอคอนตอนลากอยู่เหนือ UI / ตอนลากออกนอก UI (ปล่อย = ทิ้ง)")]
    public Color dragOverUiColor = new Color(1f, 1f, 1f, 0.85f);
    public Color dragDiscardColor = new Color(1f, 0.35f, 0.3f, 0.9f);

    [Header("Weight")]
    public UIBar weightBar;
    [Tooltip("สีหลอดน้ำหนัก ปกติ / เกือบเต็ม (>= 90%)")]
    public Color weightNormal = new Color(0.75f, 0.65f, 0.4f);
    public Color weightFull = new Color(0.85f, 0.3f, 0.25f);

    public ItemCategory CurrentTab { get; private set; } = ItemCategory.Equipment;

    private PlayerInventory _inventory;
    private readonly List<InventoryRowView> _rows = new List<InventoryRowView>();
    private readonly List<ItemInstance> _filtered = new List<ItemInstance>();
    private readonly Dictionary<ItemCategory, string> _queries = new Dictionary<ItemCategory, string>();

    protected override void Awake()
    {
        base.Awake();
        if (!IsInstance) return;
        if (rowTemplate != null) rowTemplate.gameObject.SetActive(false);
        for (int i = 0; i < tabs.Length; i++)
        {
            var category = tabs[i].category;
            if (tabs[i].button != null) tabs[i].button.onClick.AddListener(() => SelectTab(category));
        }
        if (searchField != null) searchField.onValueChanged.AddListener(OnSearchChanged);
        if (clearSearchButton != null) clearSearchButton.onClick.AddListener(ClearSearch);
        if (sortButton != null) sortButton.onClick.AddListener(Sort);
        SelectTab(CurrentTab);
    }

    protected override void OnDestroy()
    {
        foreach (var t in tabs)
            if (t.button != null) t.button.onClick.RemoveAllListeners();
        if (searchField != null) searchField.onValueChanged.RemoveListener(OnSearchChanged);
        if (clearSearchButton != null) clearSearchButton.onClick.RemoveListener(ClearSearch);
        if (sortButton != null) sortButton.onClick.RemoveListener(Sort);
        base.OnDestroy();
    }

    public void Bind(GameObject player)
    {
        if (_inventory != null) _inventory.Changed -= OnInventoryChanged;
        _inventory = player != null ? player.GetComponent<PlayerInventory>() : null;
        if (_inventory != null) _inventory.Changed += OnInventoryChanged;
        Refresh();
    }

    public void SelectTab(ItemCategory category)
    {
        CurrentTab = category;
        foreach (var t in tabs)
            if (t.selected != null) t.selected.SetActive(t.category == category);
        // คำค้นของแท็บนี้ (ไม่ยิง onValueChanged)
        if (searchField != null) searchField.SetTextWithoutNotify(Query);
        Refresh();
    }

    private string Query => _queries.TryGetValue(CurrentTab, out var q) ? q : "";

    private void OnSearchChanged(string text)
    {
        _queries[CurrentTab] = text;
        Refresh();
    }

    public void Sort()
    {
        if (_inventory != null) _inventory.RequestSort();
    }

    private void ClearSearch()
    {
        if (searchField != null) searchField.text = ""; // ยิง OnSearchChanged
        else OnSearchChanged("");
    }

    private void OnInventoryChanged(PlayerInventory inv)
    {
        if (IsOpen) Refresh();
    }

    public void Refresh()
    {
        if (_inventory == null || listContent == null || rowTemplate == null) return;

        var items = _inventory.Items;
        // จำนวนต่อแท็บบนป้ายแท็บ (ไม่สนคำค้น)
        for (int t = 0; t < tabs.Length; t++)
        {
            if (tabs[t].label == null) continue;
            int count = 0;
            foreach (var item in items) if (CategoryOf(item) == tabs[t].category) count++;
            tabs[t].label.text = count > 0 ? $"{tabs[t].title} <color=#888>({count})</color>" : tabs[t].title;
        }

        string query = Query;
        _filtered.Clear();
        foreach (var item in items)
            if (CategoryOf(item) == CurrentTab && SearchQuery.Matches(NameOf(item), query)) _filtered.Add(item);

        while (_rows.Count < _filtered.Count)
        {
            var row = Instantiate(rowTemplate, listContent);
            row.Activated = OnRowActivated;
            row.DragStarted = OnRowDragStarted;
            row.Dragging = OnRowDragging;
            row.DragReleased = OnRowDragReleased;
            _rows.Add(row);
        }
        for (int i = 0; i < _rows.Count; i++)
        {
            bool used = i < _filtered.Count;
            if (_rows[i].gameObject.activeSelf != used) _rows[i].gameObject.SetActive(used);
            if (used) _rows[i].Set(_filtered[i], fallbackIcon);
        }
        if (emptyText != null)
        {
            emptyText.gameObject.SetActive(_filtered.Count == 0);
            emptyText.text = string.IsNullOrWhiteSpace(query) ? "Empty" : "No match";
        }

        if (weightBar != null)
        {
            float usedWeight = _inventory.UsedWeight, cap = _inventory.Capacity;
            weightBar.Set(usedWeight, cap, $"{usedWeight:0} / {cap:0} B");
            if (weightBar.fill != null) weightBar.fill.color = cap > 0f && usedWeight / cap >= 0.9f ? weightFull : weightNormal;
        }
    }

    private static ItemCategory CategoryOf(ItemInstance item)
    {
        var def = item.Definition;
        return def != null ? def.Category : ItemCategory.Other;
    }

    private static string NameOf(ItemInstance item)
    {
        var def = item.Definition;
        return def != null ? def.displayName : item.itemId;
    }

    // คลิกขวา/ดับเบิลคลิก: ของสวมใส่ = ใส่ / กดใช้ = ใช้ / gem = เปิดหน้าต่าง Skill Gems (ส่งคำขอ server ตัดสิน)
    private void OnRowActivated(InventoryRowView row)
    {
        if (_inventory == null) return;
        string id = row.Item.instanceId;
        switch (row.Item.Definition)
        {
            case EquipmentItem _: if (!_inventory.RequestEquip(id)) UIToast.Show("Cannot equip"); break;
            case ConsumableItem _: if (!_inventory.RequestUse(id)) UIToast.Show("Not ready yet"); break;
            case SkillGem _: if (SkillGemWindow.Instance != null) SkillGemWindow.Instance.Open(); break;
        }
        if (ItemTooltip.Instance != null) ItemTooltip.Instance.Hide();
    }

    // ---------- ลากทิ้ง ----------

    private static bool OverUI(PointerEventData e) => e.pointerCurrentRaycast.gameObject != null;

    private void OnRowDragStarted(InventoryRowView row, PointerEventData e)
    {
        LocalInputGate.PointerCaptured = true; // ปล่อยปุ่มลงพื้นไม่ใช่การโจมตี
        EnsureDragGhost();
        var def = row.Item.Definition;
        dragGhost.sprite = def != null && def.icon != null ? def.icon : fallbackIcon;
        dragGhost.gameObject.SetActive(true);
        dragGhost.transform.SetAsLastSibling();
        OnRowDragging(row, e);
    }

    private void OnRowDragging(InventoryRowView row, PointerEventData e)
    {
        if (dragGhost == null) return;
        var canvas = (RectTransform)dragGhost.canvas.rootCanvas.transform;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, e.position, null, out Vector2 local))
            dragGhost.rectTransform.anchoredPosition = local;
        dragGhost.color = OverUI(e) ? dragOverUiColor : dragDiscardColor;
    }

    private void OnRowDragReleased(InventoryRowView row, PointerEventData e)
    {
        LocalInputGate.PointerCaptured = false;
        if (dragGhost != null) dragGhost.gameObject.SetActive(false);
        if (_inventory == null || OverUI(e)) return;

        // ถามก่อนทิ้ง (ลบถาวร) ของที่ stack อยู่ = ถามจำนวนด้วย
        var item = row.Item;
        var def = item.Definition;
        string name = def != null ? def.displayName : item.itemId;
        Color c = def is SkillGem gem ? gem.gemColor : ItemDefinition.RarityColor(def != null ? def.rarity : ItemRarity.Normal);
        string colored = $"<color=#{ColorUtility.ToHtmlStringRGB(c)}>{name}</color>";
        const string warning = "\n<size=80%><color=#999>This cannot be undone.</color></size>";
        var inventory = _inventory;
        if (item.Count > 1)
            ConfirmDialog.AskAmount("Discard Item", $"How many {colored} to destroy?{warning}", item.Count,
                "Discard", amount => inventory.RequestDiscard(item.instanceId, amount));
        else
            ConfirmDialog.Ask("Discard Item", $"Destroy {colored}?{warning}", "Discard", () => inventory.RequestDiscard(item.instanceId));
    }

    protected override void OnOpenChanged(bool open)
    {
        if (!open)
        {
            LocalInputGate.PointerCaptured = false;
            if (dragGhost != null) dragGhost.gameObject.SetActive(false);
        }
        if (open) Refresh();
    }

    // ไม่ได้ตั้งไว้ใน prefab -> สร้างใต้ root canvas (canvas ซ้อนให้อยู่บนสุด ไม่รับ raycast)
    private void EnsureDragGhost()
    {
        if (dragGhost != null) return;
        var root = GetComponentInParent<Canvas>().rootCanvas.transform;
        var obj = new GameObject("DragGhost", typeof(RectTransform));
        obj.transform.SetParent(root, false);
        var c = obj.AddComponent<Canvas>();
        c.overrideSorting = true;
        c.sortingOrder = 190;
        dragGhost = obj.AddComponent<Image>();
        dragGhost.raycastTarget = false;
        dragGhost.preserveAspect = true;
        var rt = dragGhost.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(56f, 56f);
        obj.SetActive(false);
    }
}

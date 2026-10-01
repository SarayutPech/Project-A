using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// หน้าต่างกล่องของดรอปใน Hideout (presentation อ่านจาก ILootStashService ไม่แก้ข้อมูลเอง) อยู่ใน GameUI prefab (ใส่ art ได้)
// แถว = [ไอคอน] ชื่อไอเทม (สีตาม rarity) xจำนวน / ของชนิดเดียวกันรวมเป็นแถวเดียว
// คลิกแถว = เก็บ 1 drop ของชนิดนั้นเข้ากระเป๋า / Take All = เก็บทั้งหมดที่น้ำหนักพอ (ส่งคำขอให้ PlayerInventory ตัดสิน)
// LootChest เป็นคนเปิด/ปิด (เดินออกนอกระยะ = ปิด)
public class LootStashWindow : UIWindow<LootStashWindow>
{
    public TextMeshProUGUI titleText;
    public string title = "Loot";
    public Button takeAllButton;
    public RectTransform listContent;
    [Tooltip("แถวต้นแบบ (ปิดไว้)")]
    public InventoryRowView rowTemplate;
    public TextMeshProUGUI emptyText;
    public Sprite fallbackIcon;

    public Object Opener { get; private set; } // ใครเปิดอยู่ (LootChest ใช้เช็คปิดตอนเดินออกจากระยะ)

    private readonly List<InventoryRowView> _rows = new List<InventoryRowView>();
    private readonly List<(string id, int count)> _grouped = new List<(string, int)>();
    private ILootStashService _stash;
    private string _ownerId;

    // เดิมสร้างต่อ scene ตอนนี้อยู่ใน GameUI (ตัวเดียว) -> คืนตัวนั้น
    public static LootStashWindow FindOrCreate(Component context)
    {
        if (Instance == null) Debug.LogWarning($"[{nameof(LootStashWindow)}] ไม่มีใน GameUI prefab (Tools > Project-A > Build UI Template)", context);
        return Instance;
    }

    protected override void Awake()
    {
        base.Awake();
        if (!IsInstance) return;
        if (rowTemplate != null) rowTemplate.gameObject.SetActive(false);
        if (takeAllButton != null) takeAllButton.onClick.AddListener(TakeAll);
    }

    protected override void OnDestroy()
    {
        if (takeAllButton != null) takeAllButton.onClick.RemoveListener(TakeAll);
        Unsubscribe();
        base.OnDestroy();
    }

    public void Open(Object opener, ILootStashService stash, string ownerId)
    {
        Unsubscribe();
        Opener = opener;
        _stash = stash;
        _ownerId = ownerId;
        _stash.Changed += OnStashChanged;
        Open();
    }

    protected override void OnOpenChanged(bool open)
    {
        if (open)
        {
            Refresh();
            return;
        }
        Unsubscribe();
        Opener = null;
    }

    private void Unsubscribe()
    {
        if (_stash != null) _stash.Changed -= OnStashChanged;
        _stash = null;
    }

    private void OnStashChanged(string ownerId)
    {
        if (ownerId == _ownerId) Refresh();
    }

    public void Refresh()
    {
        if (_stash == null || listContent == null || rowTemplate == null) return;

        // รวมของชนิดเดียวกัน คงลำดับที่ดรอปครั้งแรก
        _grouped.Clear();
        foreach (var drop in _stash.GetPending(_ownerId))
        {
            int i = _grouped.FindIndex(g => g.id == drop.item.itemId);
            if (i >= 0) _grouped[i] = (drop.item.itemId, _grouped[i].count + drop.item.count);
            else _grouped.Add((drop.item.itemId, drop.item.count));
        }

        while (_rows.Count < _grouped.Count)
        {
            var row = Instantiate(rowTemplate, listContent);
            row.Clicked = r => TakeOne(r.Item.itemId);
            row.Activated = r => TakeOne(r.Item.itemId);
            row.hintOverride = "Click to take";
            _rows.Add(row);
        }
        for (int i = 0; i < _rows.Count; i++)
        {
            bool used = i < _grouped.Count;
            if (_rows[i].gameObject.activeSelf != used) _rows[i].gameObject.SetActive(used);
            if (used) _rows[i].Set(new ItemInstance(null, _grouped[i].id, 1, _grouped[i].count), fallbackIcon);
        }

        if (emptyText != null) emptyText.gameObject.SetActive(_grouped.Count == 0);
        if (titleText != null) titleText.text = _grouped.Count > 0 ? $"{title}  <size=70%><color=#999>({_grouped.Count})</color></size>" : title;
    }

    // ---------- เก็บเข้ากระเป๋า (คำขอให้ PlayerInventory ของ local player ตัดสิน น้ำหนักไม่พอ = ไม่เก็บ) ----------

    private PlayerInventory LocalInventory()
    {
        var player = GameUI.Instance != null ? GameUI.Instance.BoundPlayer : null;
        return player != null ? player.GetComponent<PlayerInventory>() : null;
    }

    public void TakeAll()
    {
        var inv = LocalInventory();
        if (inv == null || _stash == null) return;
        int pending = _stash.GetPending(_ownerId).Count;
        if (inv.RequestClaimAll(_stash) < pending) UIToast.Show("Bag is full (weight)");
    }

    private void TakeOne(string itemId)
    {
        var inv = LocalInventory();
        if (inv == null || _stash == null || string.IsNullOrEmpty(itemId)) return;
        // เก็บตัวที่ดรอปหลังสุดของชนิดนั้น: ตัวแรกยังอยู่ -> ลำดับแถว (เรียงตามที่ดรอปครั้งแรก) ไม่สลับ
        var pending = _stash.GetPending(_ownerId);
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            if (pending[i].item.itemId != itemId) continue;
            if (!inv.RequestClaim(_stash, pending[i].dropId)) UIToast.Show("Bag is full (weight)");
            return;
        }
    }
}

using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// แถวไอเทม 1 ช่องในกระเป๋า: [กรอบ rarity + ไอคอน] ชื่อ xจำนวน / ประเภท ... น้ำหนัก
// ชี้ = tooltip / คลิกขวาหรือดับเบิลคลิก = ใส่/ใช้ (ส่งต่อให้ InventoryWindow)
// คลิกซ้ายค้างแล้วลาก = ลากไอเทม (ถ้าเจ้าของตั้ง DragReleased ไว้) ไม่งั้นส่งการลากต่อให้ ScrollRect ของรายการ
public class InventoryRowView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler,
    IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public Image background;
    public Image iconFrame;
    public Image icon;
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI typeText;
    public TextMeshProUGUI weightText;
    [Tooltip("ป้ายจำนวนบนไอคอน (ของที่ stack) เว้นว่าง = ต่อท้ายชื่อแทน")]
    public TextMeshProUGUI countText;
    [Tooltip("สีพื้นแถวปกติ / ตอนชี้")]
    public Color normalColor = new Color(1f, 1f, 1f, 0.04f);
    public Color hoverColor = new Color(1f, 1f, 1f, 0.12f);

    public ItemInstance Item { get; private set; }
    public System.Action<InventoryRowView> Activated; // คลิกขวา / ดับเบิลคลิก
    public System.Action<InventoryRowView> Clicked;   // คลิกซ้าย 1 ครั้ง (เช่นหน้าต่างเลือก gem)
    [System.NonSerialized] public string hintOverride; // ข้อความวิธีใช้ใน tooltip แทนค่าเริ่มต้น

    // ลากไอเทม (DragReleased = null -> แถวนี้ลากไม่ได้ การลากเลื่อนรายการแทน)
    public System.Action<InventoryRowView, PointerEventData> DragStarted;
    public System.Action<InventoryRowView, PointerEventData> Dragging;
    public System.Action<InventoryRowView, PointerEventData> DragReleased;

    private GameObject _forwardDrag; // ScrollRect ที่รับการลากแทน

    public void Set(ItemInstance item, Sprite fallbackIcon)
    {
        Item = item;
        ItemDefinition def = item.Definition;
        Color rarity = ItemDefinition.RarityColor(def != null ? def.rarity : ItemRarity.Normal);

        Sprite sprite = def != null && def.icon != null ? def.icon : fallbackIcon;
        if (icon != null)
        {
            icon.sprite = sprite;
            icon.enabled = sprite != null;
        }
        if (iconFrame != null) iconFrame.color = new Color(rarity.r * 0.35f, rarity.g * 0.35f, rarity.b * 0.35f, 1f);

        int count = item.Count;
        if (nameText != null)
        {
            string name = def != null ? def.displayName : $"<i>{item.itemId}</i>";
            nameText.text = count > 1 && countText == null ? $"{name}  <color=#CCC>x{count}</color>" : name;
            nameText.color = def is SkillGem gem ? gem.gemColor : def != null ? rarity : Color.gray;
        }
        if (countText != null) countText.text = count > 1 ? count.ToString() : "";
        if (typeText != null)
            typeText.text = def is EquipmentItem eq ? EquipmentItem.SlotName(eq.slot)
                          : def is SkillGem ? $"{ItemTooltip.CategoryName(def.Category)}  ·  Level {item.Level}"
                          : def != null && def.IsStackable && count <= def.StackLimit ? $"{ItemTooltip.CategoryName(def.Category)}  ·  {count} / {def.StackLimit}"
                          : def != null ? ItemTooltip.CategoryName(def.Category) : "";
        if (weightText != null) weightText.text = def != null ? $"{def.weight * count} B" : "";
        if (background != null) background.color = normalColor;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (background != null) background.color = hoverColor;
        if (ItemTooltip.Instance != null && !eventData.dragging)
            ItemTooltip.Instance.ShowItem(Item, hintOverride ?? HintFor(Item.Definition));
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (background != null) background.color = normalColor;
        if (ItemTooltip.Instance != null) ItemTooltip.Instance.Hide();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.dragging) return;
        bool activate = eventData.button == PointerEventData.InputButton.Right
                     || (eventData.button == PointerEventData.InputButton.Left && eventData.clickCount >= 2);
        if (eventData.button == PointerEventData.InputButton.Left && eventData.clickCount == 1) Clicked?.Invoke(this);
        if (activate) Activated?.Invoke(this);
    }

    // ---------- ลาก ----------

    private bool CanDragItem(PointerEventData e) => DragReleased != null && e.button == PointerEventData.InputButton.Left;

    // ส่งการลากต่อให้ ScrollRect (แถวที่ลากของไม่ได้ / ลากด้วยปุ่มอื่น)
    private bool Forward<T>(PointerEventData e, ExecuteEvents.EventFunction<T> handler) where T : IEventSystemHandler
    {
        if (_forwardDrag == null) return false;
        ExecuteEvents.Execute(_forwardDrag, e, handler);
        return true;
    }

    public void OnInitializePotentialDrag(PointerEventData e)
    {
        _forwardDrag = null;
        if (CanDragItem(e) || transform.parent == null) return;
        _forwardDrag = ExecuteEvents.GetEventHandler<IBeginDragHandler>(transform.parent.gameObject);
        if (_forwardDrag != null) ExecuteEvents.Execute(_forwardDrag, e, ExecuteEvents.initializePotentialDrag);
    }

    public void OnBeginDrag(PointerEventData e)
    {
        if (Forward(e, ExecuteEvents.beginDragHandler)) return;
        if (!CanDragItem(e)) return;
        if (ItemTooltip.Instance != null) ItemTooltip.Instance.Hide();
        DragStarted?.Invoke(this, e);
    }

    public void OnDrag(PointerEventData e)
    {
        if (Forward(e, ExecuteEvents.dragHandler)) return;
        if (CanDragItem(e)) Dragging?.Invoke(this, e);
    }

    public void OnEndDrag(PointerEventData e)
    {
        if (Forward(e, ExecuteEvents.endDragHandler))
        {
            _forwardDrag = null;
            return;
        }
        if (CanDragItem(e)) DragReleased?.Invoke(this, e);
    }

    private void OnDisable()
    {
        if (background != null) background.color = normalColor;
    }

    public static string HintFor(ItemDefinition def) => def switch
    {
        EquipmentItem => "Right-click to equip\nDrag outside the window to discard",
        ConsumableItem => "Right-click to use\nDrag outside the window to discard",
        SkillGem => "Socket it in the Skill Gems window (K)\nDrag outside the window to discard",
        _ => "Drag outside the window to discard",
    };
}

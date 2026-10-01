using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// ช่องสวมใส่ 1 ช่องบน Character Sheet: ชี้ = tooltip / คลิกขวาหรือดับเบิลคลิก = ถอด (ส่งต่อให้ CharacterWindow)
public class EquipSlotView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    public EquipSlot slot;
    public Image frame;
    public Image icon;
    [Tooltip("รูป/ชื่อช่องตอนว่าง (เช่นเงาหมวก) ซ่อนเมื่อมีของ")]
    public GameObject emptyState;
    public TextMeshProUGUI slotLabel;
    public Color emptyFrameColor = new Color(1f, 1f, 1f, 0.15f);

    public ItemInstance? Item { get; private set; }
    public System.Action<EquipSlotView> Activated;

    private bool _hover;

    public void Set(ItemInstance? item, Sprite fallbackIcon)
    {
        Item = item;
        ItemDefinition def = item.HasValue ? item.Value.Definition : null;
        Sprite sprite = def != null ? (def.icon != null ? def.icon : fallbackIcon) : null;
        if (icon != null)
        {
            icon.sprite = sprite;
            icon.enabled = sprite != null;
        }
        if (emptyState != null) emptyState.SetActive(def == null);
        if (frame != null)
        {
            Color c = def != null ? ItemDefinition.RarityColor(def.rarity) : emptyFrameColor;
            frame.color = new Color(c.r, c.g, c.b, def != null ? 0.9f : emptyFrameColor.a);
        }
        if (_hover) ShowTooltip();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        _hover = true;
        ShowTooltip();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _hover = false;
        if (ItemTooltip.Instance != null) ItemTooltip.Instance.Hide();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        bool activate = eventData.button == PointerEventData.InputButton.Right
                     || (eventData.button == PointerEventData.InputButton.Left && eventData.clickCount >= 2);
        if (activate && Item.HasValue) Activated?.Invoke(this);
    }

    private void OnDisable() => _hover = false;

    private void ShowTooltip()
    {
        if (ItemTooltip.Instance == null) return;
        if (Item.HasValue) ItemTooltip.Instance.ShowItem(Item.Value.Definition, "Right-click to unequip", "Equipped");
        else ItemTooltip.Instance.ShowText(EquipmentItem.SlotName(slot), "Empty", null, Color.gray);
    }
}

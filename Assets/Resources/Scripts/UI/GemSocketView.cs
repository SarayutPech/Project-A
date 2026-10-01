using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// ช่องใส่ gem 1 ช่องในหน้าต่าง Skill Gems (gem หลัก หรือ support 1 ใน 5)
// คลิกซ้าย = เลือกช่องนี้ (รายการ gem ด้านข้างกรองตามชนิดช่อง) / คลิกขวา = ถอด gem กลับกระเป๋า / ชี้ = tooltip
public class GemSocketView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [Tooltip("SocketedGem.ActiveSocket (-1) = gem หลัก / 0-4 = support")]
    public int socket = SocketedGem.ActiveSocket;
    public Image frame;
    public Image icon;
    [Tooltip("แสดงตอนว่าง")]
    public GameObject emptyState;
    [Tooltip("แสดงตอนช่องนี้ถูกเลือก")]
    public GameObject selectedState;
    public TextMeshProUGUI nameText;
    public Color emptyColor = new Color(1f, 1f, 1f, 0.15f);

    public ItemInstance? Item { get; private set; }
    public System.Action<GemSocketView> Clicked;
    public System.Action<GemSocketView> RightClicked;

    private bool _hover;

    public bool IsActiveSocket => socket == SocketedGem.ActiveSocket;

    public void Set(ItemInstance? item, bool selected)
    {
        Item = item;
        var gem = item.HasValue ? item.Value.Definition as SkillGem : null;
        if (icon != null)
        {
            icon.sprite = gem != null ? gem.icon : null;
            icon.enabled = gem != null && gem.icon != null;
        }
        if (emptyState != null) emptyState.SetActive(gem == null);
        if (selectedState != null) selectedState.SetActive(selected);
        if (frame != null)
        {
            Color c = gem != null ? gem.gemColor : emptyColor;
            frame.color = new Color(c.r, c.g, c.b, gem != null ? 0.9f : emptyColor.a);
        }
        if (nameText != null)
        {
            nameText.text = gem != null ? $"{gem.displayName} <color=#888>Lv {item.Value.Level}</color>" : (IsActiveSocket ? "Skill Gem" : "Support");
            nameText.color = gem != null ? Color.white : new Color(1f, 1f, 1f, 0.35f);
        }
        if (_hover) ShowTooltip();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left) Clicked?.Invoke(this);
        else if (eventData.button == PointerEventData.InputButton.Right) RightClicked?.Invoke(this);
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

    private void OnDisable() => _hover = false;

    private void ShowTooltip()
    {
        if (ItemTooltip.Instance == null) return;
        if (Item.HasValue) ItemTooltip.Instance.ShowItem(Item.Value, "Right-click to remove", "Socketed");
        else ItemTooltip.Instance.ShowText(IsActiveSocket ? "Skill Gem Socket" : $"Support Socket {socket + 1}", "Empty",
            "Click, then pick a gem from the list", Color.gray);
    }
}

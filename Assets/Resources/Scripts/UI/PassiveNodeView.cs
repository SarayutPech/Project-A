using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// node 1 จุดบนหน้าต่าง passive tree (presentation) คลิกซ้าย = ลงแต้ม / เลือกจุด start, คลิกขวา = คืนแต้ม, ชี้ = tooltip
public class PassiveNodeView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    public Image fill;
    public Image icon;
    [Tooltip("แสดงตอนลงแต้มแล้ว (เช่นกรอบทอง)")]
    public GameObject allocatedState;
    [Tooltip("แสดงตอนลงได้ (ติดกับที่ลงแล้ว + มีแต้ม)")]
    public GameObject availableState;
    [Tooltip("แสดงเมื่อเป็นจุด start")]
    public GameObject startState;
    public Color lockedColor = new Color(0.3f, 0.3f, 0.3f, 1f);
    public Color availableColor = new Color(0.6f, 0.6f, 0.6f, 1f);
    public Color allocatedColor = new Color(0.95f, 0.8f, 0.4f, 1f);

    public string NodeId { get; private set; }
    public System.Action<PassiveNodeView> Clicked;
    public System.Action<PassiveNodeView> RightClicked;
    public System.Action<PassiveNodeView> Hovered;

    private bool _hover;

    private Sprite _templateFill;

    public void Init(PassiveNode node)
    {
        NodeId = node.id;
        if (fill != null)
        {
            if (_templateFill == null) _templateFill = fill.sprite;
            fill.sprite = node.frame != null ? node.frame : _templateFill;
        }
        if (icon != null)
        {
            icon.sprite = node.icon;
            icon.enabled = node.icon != null;
        }
    }

    public void SetState(bool allocated, bool available, bool isStart)
    {
        if (fill != null) fill.color = allocated ? allocatedColor : available ? availableColor : lockedColor;
        if (allocatedState != null) allocatedState.SetActive(allocated);
        if (availableState != null) availableState.SetActive(available && !allocated);
        if (startState != null) startState.SetActive(isStart);
        if (_hover) Hovered?.Invoke(this);
    }

    public void OnPointerClick(PointerEventData e)
    {
        if (e.dragging) return;
        if (e.button == PointerEventData.InputButton.Left) Clicked?.Invoke(this);
        else if (e.button == PointerEventData.InputButton.Right) RightClicked?.Invoke(this);
    }

    public void OnPointerEnter(PointerEventData e)
    {
        _hover = true;
        Hovered?.Invoke(this);
    }

    public void OnPointerExit(PointerEventData e)
    {
        _hover = false;
        if (ItemTooltip.Instance != null) ItemTooltip.Instance.Hide();
    }

    private void OnDisable() => _hover = false;
}

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
    private Sprite _nodeIcon;

    // รูปทับ icon ของ node (เช่น gem ที่ใส่ใน Socket) null = กลับไปใช้ icon ของ node
    public void SetOverlayIcon(Sprite overlay)
    {
        if (icon == null) return;
        Sprite s = overlay != null ? overlay : _nodeIcon;
        icon.sprite = s;
        icon.enabled = s != null;
    }

    private PassiveNodeStyle _style;
    private PolygonGraphic _poly; // รูปทรงหลายเหลี่ยมแทน sprite วงกลมของ template (เมื่อ style ไม่ใช่วงกลมและ node ไม่มี frame)
    private Color _typeColor = Color.white;

    public void Init(PassiveNode node, Sprite frameSprite, Sprite iconSprite, PassiveNodeStyle style)
    {
        NodeId = node.id;
        _nodeIcon = iconSprite;
        _style = style != null && style.overrideColors ? style : null;
        _typeColor = style != null ? style.color : Color.white;
        SetupShape(frameSprite == null && style != null ? style.shape : PassiveNodeShape.Circle);
        if (fill != null)
        {
            if (_templateFill == null) _templateFill = fill.sprite;
            fill.sprite = frameSprite != null ? frameSprite : _templateFill;
        }
        if (icon != null)
        {
            icon.sprite = iconSprite;
            icon.enabled = iconSprite != null;
        }
    }

    // วงกลม = ใช้ Image/sprite ของ template / อื่นๆ = วาดหลายเหลี่ยมทับตำแหน่งเดียวกัน
    private void SetupShape(PassiveNodeShape shape)
    {
        if (fill == null) return;
        if (shape == PassiveNodeShape.Circle)
        {
            if (_poly != null) _poly.gameObject.SetActive(false);
            return;
        }
        if (_poly == null)
        {
            var go = new GameObject("Shape", typeof(RectTransform), typeof(CanvasRenderer), typeof(PolygonGraphic));
            var rt = (RectTransform)go.transform;
            var src = fill.rectTransform;
            rt.SetParent(src.parent, false);
            rt.anchorMin = src.anchorMin;
            rt.anchorMax = src.anchorMax;
            rt.pivot = src.pivot;
            rt.offsetMin = src.offsetMin;
            rt.offsetMax = src.offsetMax;
            rt.SetSiblingIndex(src.GetSiblingIndex() + 1); // เหนือพื้น ใต้ icon
            _poly = go.GetComponent<PolygonGraphic>();
            _poly.raycastTarget = false;
        }
        _poly.gameObject.SetActive(true);
        _poly.Sides = (int)shape;
    }

    public void SetState(bool allocated, bool available, bool isStart)
    {
        if (fill != null)
        {
            Color c = allocated ? (_style != null ? _style.allocatedColor : allocatedColor) : available ? (_style != null ? _style.availableColor : availableColor) : (_style != null ? _style.lockedColor : lockedColor);
            if (_poly != null && _poly.gameObject.activeSelf)
            {
                fill.color = Color.clear; // Image ยังรับคลิก แต่ไม่วาด
                float f = allocated ? 1f : available ? 0.65f : 0.35f;
                _poly.color = _style != null ? c : new Color(_typeColor.r * f, _typeColor.g * f, _typeColor.b * f, 1f);
            }
            else fill.color = c;
        }
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

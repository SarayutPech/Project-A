using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// popup เลือก 1 จากรายการ (ตัวเลือกของ node Choice / gem ที่จะใส่ node Socket) สร้าง UI เองด้วยโค้ด ไม่ต้องมีใน prefab
// คลิกพื้นมืดหรือ Cancel = ปิดโดยไม่เลือก / เป็น presentation อย่างเดียว ผลที่เลือกส่งกลับผ่าน callback
public class PassivePickerPopup : MonoBehaviour
{
    public struct Entry
    {
        public string title;
        public string detail;
        public Sprite icon;
        public Color color;
    }

    private const float Width = 400f;
    private const float RowHeight = 58f;
    private const float MaxListHeight = 340f;

    private static readonly Color PanelColor = new Color(0.07f, 0.06f, 0.05f, 0.97f);
    private static readonly Color RowColor = new Color(1f, 1f, 1f, 0.07f);
    private static readonly Color Gold = new Color(0.93f, 0.85f, 0.66f);

    private TMP_FontAsset _font;
    private TextMeshProUGUI _title;
    private RectTransform _content;
    private LayoutElement _scrollLayout;
    private System.Action<int> _onPick;

    public bool IsOpen => gameObject.activeSelf;

    public static PassivePickerPopup Create(Transform parent, TMP_FontAsset font)
    {
        var go = new GameObject("PassivePicker", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var popup = go.AddComponent<PassivePickerPopup>();
        popup._font = font;
        popup.Build();
        go.SetActive(false);
        return popup;
    }

    // onPick(ลำดับใน entries) / ปิดโดยไม่เลือก = ไม่เรียก
    public void Show(string title, IReadOnlyList<Entry> entries, System.Action<int> onPick)
    {
        _onPick = onPick;
        _title.text = title;
        for (int i = _content.childCount - 1; i >= 0; i--) Destroy(_content.GetChild(i).gameObject);
        for (int i = 0; i < entries.Count; i++) AddRow(entries[i], i);
        _scrollLayout.preferredHeight = Mathf.Min(MaxListHeight, entries.Count * (RowHeight + 6f));
        transform.SetAsLastSibling();
        gameObject.SetActive(true);
    }

    public void Hide()
    {
        _onPick = null;
        gameObject.SetActive(false);
    }

    private void Pick(int index)
    {
        var action = _onPick;
        Hide();
        action?.Invoke(index);
    }

    // ---------- สร้าง UI ----------

    private void Build()
    {
        var root = (RectTransform)transform;
        Stretch(root);

        // พื้นมืดเต็มจอ: กันคลิกทะลุ + คลิก = ปิด
        var dim = NewImage("Dimmer", root, new Color(0f, 0f, 0f, 0.55f));
        Stretch(dim.rectTransform);
        dim.gameObject.AddComponent<Button>().onClick.AddListener(Hide);

        var panel = NewImage("Panel", root, PanelColor);
        panel.rectTransform.anchorMin = panel.rectTransform.anchorMax = panel.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        panel.rectTransform.sizeDelta = new Vector2(Width, 0f);
        var v = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(12, 12, 12, 12);
        v.spacing = 8f;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _title = NewText("Title", panel.rectTransform, 20f, Gold, TextAlignmentOptions.Center);
        _title.gameObject.AddComponent<LayoutElement>().minHeight = 28f;

        // รายการเลื่อนได้
        var scrollImage = NewImage("List", panel.rectTransform, new Color(0f, 0f, 0f, 0.2f));
        _scrollLayout = scrollImage.gameObject.AddComponent<LayoutElement>();
        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
        viewport.SetParent(scrollImage.rectTransform, false);
        Stretch(viewport);
        _content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
        _content.SetParent(viewport, false);
        _content.anchorMin = new Vector2(0f, 1f);
        _content.anchorMax = new Vector2(1f, 1f);
        _content.pivot = new Vector2(0.5f, 1f);
        _content.sizeDelta = Vector2.zero;
        var cv = _content.gameObject.AddComponent<VerticalLayoutGroup>();
        cv.spacing = 6f;
        cv.padding = new RectOffset(4, 4, 4, 4);
        cv.childControlWidth = cv.childControlHeight = true;
        cv.childForceExpandWidth = true;
        cv.childForceExpandHeight = false;
        _content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var scroll = scrollImage.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = _content;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;

        var cancel = NewImage("Cancel", panel.rectTransform, RowColor);
        cancel.gameObject.AddComponent<LayoutElement>().minHeight = 32f;
        cancel.gameObject.AddComponent<Button>().onClick.AddListener(Hide);
        var cancelText = NewText("Label", cancel.rectTransform, 16f, new Color(0.75f, 0.75f, 0.75f), TextAlignmentOptions.Center);
        cancelText.text = "Cancel";
        Stretch(cancelText.rectTransform);
        cancelText.raycastTarget = false;
    }

    private void AddRow(Entry entry, int index)
    {
        var row = NewImage("Row", _content, RowColor);
        row.gameObject.AddComponent<LayoutElement>().minHeight = RowHeight;
        var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(8, 8, 6, 6);
        h.spacing = 8f;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = true;
        h.childForceExpandHeight = false;

        var button = row.gameObject.AddComponent<Button>();
        button.targetGraphic = row;
        button.onClick.AddListener(() => Pick(index));

        if (entry.icon != null)
        {
            var icon = NewImage("Icon", row.rectTransform, Color.white);
            icon.sprite = entry.icon;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            var le = icon.gameObject.AddComponent<LayoutElement>();
            le.minWidth = le.minHeight = le.preferredWidth = le.preferredHeight = 40f;
        }
        var text = NewText("Text", row.rectTransform, 15f, Color.white, TextAlignmentOptions.MidlineLeft);
        text.raycastTarget = false;
        string color = ColorUtility.ToHtmlStringRGB(entry.color.a > 0f ? entry.color : Color.white);
        text.text = string.IsNullOrEmpty(entry.detail)
            ? $"<color=#{color}>{entry.title}</color>"
            : $"<color=#{color}>{entry.title}</color>\n<size=80%><color=#8888FF>{entry.detail}</color></size>";
    }

    private Image NewImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.color = color;
        return image;
    }

    private TextMeshProUGUI NewText(string name, Transform parent, float size, Color color, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<TextMeshProUGUI>();
        if (_font != null) text.font = _font;
        text.fontSize = size;
        text.color = color;
        text.alignment = align;
        text.richText = true;
        return text;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
}

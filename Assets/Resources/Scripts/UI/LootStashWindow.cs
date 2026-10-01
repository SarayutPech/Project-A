using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// หน้าต่างรายการของที่ดรอป (presentation อย่างเดียว อ่านจาก ILootStashService ไม่แก้ข้อมูลเอง)
// แสดงเป็นแถว: [ไอคอน] ชื่อไอเทม (สีตาม rarity) xจำนวน / ของชนิดเดียวกันรวมเป็นแถวเดียว
// สร้าง UI เองจากโค้ด (ไม่ต้องทำ prefab) วางไว้ใน scene หรือให้ LootChest สร้างให้ก็ได้
// ฟอนต์ default ของ TMP ไม่มีภาษาไทย -> ถ้าชื่อไอเทมเป็นไทยให้ใส่ TMP font ที่มีไทยในช่อง Font
public class LootStashWindow : MonoBehaviour
{
    [Header("Look")]
    [Tooltip("เว้นว่าง = ฟอนต์ default ของ TMP (ไม่มีภาษาไทย)")]
    public TMP_FontAsset font;
    public string title = "Loot";
    public string emptyText = "No items yet";
    public Vector2 panelSize = new Vector2(460f, 560f);
    [Min(16f)] public float rowHeight = 44f;
    [Min(8f)] public float fontSize = 22f;
    public Color panelColor = new Color(0.08f, 0.07f, 0.06f, 0.94f);
    public Color rowColor = new Color(1f, 1f, 1f, 0.04f);
    [Tooltip("ไอคอนที่ใช้ตอนไอเทมไม่มี icon")]
    public Sprite fallbackIcon;

    public bool IsOpen => _canvas != null && _canvas.enabled;
    public Object Opener { get; private set; } // ใครเปิดอยู่ (LootChest ใช้เช็คปิดตอนเดินออกจากระยะ)

    private class Row
    {
        public GameObject root;
        public Image frame;
        public Image icon;
        public TextMeshProUGUI name;
        public TextMeshProUGUI count;
    }

    private Canvas _canvas;
    private RectTransform _content;
    private TextMeshProUGUI _empty;
    private TextMeshProUGUI _header;
    private readonly List<Row> _rows = new List<Row>();
    private readonly List<(string id, int count)> _grouped = new List<(string, int)>();

    private ILootStashService _stash;
    private string _ownerId;

    // หา/สร้างหน้าต่างใน scene เดียวกับ context (ตัวเดียวต่อ scene)
    public static LootStashWindow FindOrCreate(Component context)
    {
        foreach (var w in FindObjectsByType<LootStashWindow>())
            if (w.gameObject.scene == context.gameObject.scene) return w;
        var obj = new GameObject(nameof(LootStashWindow));
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(obj, context.gameObject.scene);
        return obj.AddComponent<LootStashWindow>();
    }

    private void Awake()
    {
        Build();
        _canvas.enabled = false;
    }

    private void OnDestroy() => Unsubscribe();

    private void Update()
    {
        if (IsOpen && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) Close();
    }

    public void Open(Object opener, ILootStashService stash, string ownerId)
    {
        Unsubscribe();
        Opener = opener;
        _stash = stash;
        _ownerId = ownerId;
        _stash.Changed += OnStashChanged;
        EnsureEventSystem();
        _canvas.enabled = true;
        Refresh();
    }

    public void Close()
    {
        Unsubscribe();
        Opener = null;
        if (_canvas != null) _canvas.enabled = false;
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
        if (_stash == null) return;

        // รวมของชนิดเดียวกัน คงลำดับที่ดรอปครั้งแรก
        _grouped.Clear();
        foreach (var drop in _stash.GetPending(_ownerId))
        {
            int i = _grouped.FindIndex(g => g.id == drop.item.itemId);
            if (i >= 0) _grouped[i] = (drop.item.itemId, _grouped[i].count + drop.item.count);
            else _grouped.Add((drop.item.itemId, drop.item.count));
        }

        while (_rows.Count < _grouped.Count) _rows.Add(CreateRow());
        for (int i = 0; i < _rows.Count; i++)
        {
            bool used = i < _grouped.Count;
            _rows[i].root.SetActive(used);
            if (used) Fill(_rows[i], _grouped[i].id, _grouped[i].count);
        }

        _empty.gameObject.SetActive(_grouped.Count == 0);
        _header.text = _grouped.Count > 0 ? $"{title}  <size=70%><color=#999>({_grouped.Count})</color></size>" : title;
    }

    private void Fill(Row row, string itemId, int count)
    {
        ItemDefinition def = ItemDatabase.Get(itemId);
        Color rarity = ItemDefinition.RarityColor(def != null ? def.rarity : ItemRarity.Normal);

        Sprite sprite = def != null && def.icon != null ? def.icon : fallbackIcon;
        row.icon.sprite = sprite;
        row.icon.enabled = sprite != null;
        row.frame.color = new Color(rarity.r * 0.35f, rarity.g * 0.35f, rarity.b * 0.35f, 1f);

        row.name.text = def != null ? def.displayName : $"<i>{itemId}</i>";
        row.name.color = def != null ? rarity : Color.gray;
        row.count.text = count > 1 ? $"x{count}" : "";
    }

    // ---------- Build UI ----------

    private void Build()
    {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 100; // ใต้จอ fade ของ SceneTransition
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        var panel = NewRect("Panel", transform);
        panel.sizeDelta = panelSize;
        panel.gameObject.AddComponent<Image>().color = panelColor;

        // หัว: ชื่อ + ปุ่มปิด
        const float headerH = 52f;
        var header = NewRect("Header", panel);
        Stretch(header, new Vector2(0f, 1f), Vector2.one, new Vector2(16f, -headerH), new Vector2(-56f, 0f));
        _header = NewText(header, fontSize * 1.2f, TextAlignmentOptions.MidlineLeft);
        _header.fontStyle = FontStyles.Bold;
        _header.color = new Color(0.93f, 0.85f, 0.66f);

        var close = NewRect("Close", panel);
        close.anchorMin = close.anchorMax = Vector2.one;
        close.pivot = Vector2.one;
        close.sizeDelta = new Vector2(headerH, headerH);
        close.anchoredPosition = Vector2.zero;
        var closeImg = close.gameObject.AddComponent<Image>();
        closeImg.color = new Color(1f, 1f, 1f, 0f);
        var closeBtn = close.gameObject.AddComponent<Button>();
        closeBtn.targetGraphic = closeImg;
        var cb = closeBtn.colors;
        cb.highlightedColor = new Color(1f, 1f, 1f, 0.12f);
        cb.pressedColor = new Color(1f, 1f, 1f, 0.2f);
        closeBtn.colors = cb;
        closeBtn.onClick.AddListener(Close);
        var x = NewText(close, fontSize * 1.1f, TextAlignmentOptions.Center);
        x.text = "X";
        x.color = new Color(0.8f, 0.8f, 0.8f);

        // เส้นคั่น
        var line = NewRect("Line", panel);
        Stretch(line, new Vector2(0f, 1f), Vector2.one, new Vector2(12f, -headerH - 2f), new Vector2(-12f, -headerH));
        line.gameObject.AddComponent<Image>().color = new Color(0.93f, 0.85f, 0.66f, 0.25f);

        // รายการแบบเลื่อนได้
        var scroll = NewRect("Scroll", panel);
        Stretch(scroll, Vector2.zero, Vector2.one, new Vector2(10f, 10f), new Vector2(-10f, -headerH - 8f));
        var scrollRect = scroll.gameObject.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 30f;

        var viewport = NewRect("Viewport", scroll);
        Stretch(viewport, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        viewport.gameObject.AddComponent<RectMask2D>();
        scrollRect.viewport = viewport;

        _content = NewRect("Content", viewport);
        _content.anchorMin = new Vector2(0f, 1f);
        _content.anchorMax = Vector2.one;
        _content.pivot = new Vector2(0.5f, 1f);
        _content.sizeDelta = Vector2.zero;
        var layout = _content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 4f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        _content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scrollRect.content = _content;

        var empty = NewRect("Empty", scroll);
        Stretch(empty, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        _empty = NewText(empty, fontSize, TextAlignmentOptions.Center);
        _empty.text = emptyText;
        _empty.color = new Color(0.6f, 0.6f, 0.6f);
    }

    private Row CreateRow()
    {
        var row = new Row();
        var rt = NewRect("Row", _content);
        row.root = rt.gameObject;
        row.root.AddComponent<Image>().color = rowColor;
        row.root.AddComponent<LayoutElement>().preferredHeight = rowHeight;
        var h = row.root.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(6, 12, 4, 4);
        h.spacing = 10f;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;

        // กรอบไอคอน (สีตาม rarity) + ไอคอน
        float iconSize = rowHeight - 8f;
        var frame = NewRect("IconFrame", rt);
        row.frame = frame.gameObject.AddComponent<Image>();
        var fl = frame.gameObject.AddComponent<LayoutElement>();
        fl.preferredWidth = fl.minWidth = iconSize;
        fl.preferredHeight = iconSize;
        var icon = NewRect("Icon", frame);
        Stretch(icon, Vector2.zero, Vector2.one, new Vector2(2f, 2f), new Vector2(-2f, -2f));
        row.icon = icon.gameObject.AddComponent<Image>();
        row.icon.preserveAspect = true;
        row.icon.raycastTarget = false;

        var name = NewRect("Name", rt);
        row.name = NewText(name, fontSize, TextAlignmentOptions.MidlineLeft);
        row.name.textWrappingMode = TextWrappingModes.NoWrap;
        row.name.overflowMode = TextOverflowModes.Ellipsis;
        name.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        var count = NewRect("Count", rt);
        row.count = NewText(count, fontSize * 0.9f, TextAlignmentOptions.MidlineRight);
        row.count.color = new Color(0.85f, 0.85f, 0.85f);
        count.gameObject.AddComponent<LayoutElement>().preferredWidth = 60f;
        return row;
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        return (RectTransform)obj.transform;
    }

    private static void Stretch(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
    }

    private TextMeshProUGUI NewText(RectTransform parent, float size, TextAlignmentOptions align)
    {
        TextMeshProUGUI text;
        if (parent.GetComponent<Graphic>() == null) text = parent.gameObject.AddComponent<TextMeshProUGUI>();
        else
        {
            var child = NewRect("Text", parent);
            Stretch(child, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            text = child.gameObject.AddComponent<TextMeshProUGUI>();
        }
        if (font != null) text.font = font;
        text.fontSize = size;
        text.alignment = align;
        text.raycastTarget = false;
        return text;
    }

    // ปุ่ม/scroll ต้องมี EventSystem (ฉากยังไม่มี -> สร้างให้ ใช้ Input System ตัวใหม่)
    private void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        var obj = new GameObject(nameof(EventSystem), typeof(EventSystem), typeof(InputSystemUIInputModule));
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(obj, gameObject.scene);
    }
}

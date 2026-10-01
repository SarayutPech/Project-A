using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// กล่องรายละเอียดตอนชี้ไอเทม/สกิล (ตัวเดียวต่อเครื่อง) ตามเมาส์ และพลิกข้างเองไม่ให้ล้นจอ
// panel ต้องไม่รับ raycast (CanvasGroup.blocksRaycasts = false) ไม่งั้นบัง hover ของช่องข้างใต้
[RequireComponent(typeof(CanvasGroup))]
public class ItemTooltip : UISingleton<ItemTooltip>
{
    public RectTransform panel;
    public TextMeshProUGUI title;
    public TextMeshProUGUI subtitle;
    public TextMeshProUGUI body;
    public TextMeshProUGUI footer;
    [Tooltip("แถบ/กรอบหัวที่ย้อมสีตาม rarity (เว้นว่างได้)")]
    public Image rarityTint;
    [Tooltip("ห่างจากเคอร์เซอร์ (px ของ canvas)")]
    public Vector2 cursorOffset = new Vector2(24f, -24f);

    [Header("Colors")]
    public Color modifierColor = new Color(0.53f, 0.53f, 1f);
    public Color descriptionColor = new Color(0.65f, 0.6f, 0.5f);
    public Color hintColor = new Color(0.55f, 0.55f, 0.55f);

    private CanvasGroup _group;
    private Canvas _canvas;
    private readonly StringBuilder _sb = new StringBuilder();

    public bool IsVisible => _group != null && _group.alpha > 0f;

    protected override void Awake()
    {
        base.Awake();
        if (!IsInstance) return;
        _group = GetComponent<CanvasGroup>();
        _group.blocksRaycasts = false;
        _group.interactable = false;
        // tooltip มี Canvas ซ้อนของตัวเอง (ให้อยู่บนสุด) -> ใช้ root canvas คิดตำแหน่ง
        var parentCanvas = GetComponentInParent<Canvas>();
        _canvas = parentCanvas != null ? parentCanvas.rootCanvas : null;
        if (panel == null) panel = (RectTransform)transform;
        Hide();
    }

    // ---------- API ----------

    // hint = บรรทัดบอกวิธีใช้ เช่น "Right-click to equip"
    public void ShowItem(ItemInstance item, string hint = null, string header = null) =>
        ShowItem(item.Definition, hint, header, item.Level, item.Count);

    // level = level ของ gem (ไอเทมอื่นไม่ใช้)
    public void ShowItem(ItemDefinition def, string hint = null, string header = null, int level = 1, int count = 1)
    {
        if (def == null) { Hide(); return; }
        Color rarity = ItemDefinition.RarityColor(def.rarity);

        _sb.Clear();
        switch (def)
        {
            case EquipmentItem eq:
                foreach (var m in eq.modifiers) Line(StatMath.Describe(m), modifierColor);
                break;
            case ConsumableItem c:
                if (c.healFlat > 0f) Line($"Restores {c.healFlat:0.#} Life", modifierColor);
                if (c.healPercent > 0f) Line($"Restores {c.healPercent:0.#}% of Maximum Life", modifierColor);
                if (c.manaFlat > 0f) Line($"Restores {c.manaFlat:0.#} Mana", modifierColor);
                if (c.manaPercent > 0f) Line($"Restores {c.manaPercent:0.#}% of Maximum Mana", modifierColor);
                if (c.HasBuff)
                {
                    foreach (var m in c.buffs) Line(StatMath.Describe(m), modifierColor);
                    Line($"Lasts {c.buffDuration:0.#} seconds", Color.white);
                }
                if (c.cooldown > 0f) Line($"Cooldown {c.cooldown:0.#}s", hintColor);
                break;
            case ActiveSkillGem ag:
                Line($"Damage: {StatMath.FormatRange(ag.DamageMinAtLevel(level), ag.DamageMaxAtLevel(level))}", Color.white);
                Line($"Attack Time: {ag.duration / Mathf.Max(0.1f, ag.attackSpeed):0.##}s", Color.white);
                if (ag.cooldown > 0.01f) Line($"Cooldown: {ag.cooldown:0.##}s", Color.white);
                if (ag.channel) Line("Channelling (hold to repeat)", hintColor);
                break;
            case SupportGem sg:
                foreach (var line in sg.Describe(level).Split('\n'))
                    if (line.Length > 0) Line(line, modifierColor);
                break;
        }
        if (!string.IsNullOrEmpty(def.description))
        {
            if (_sb.Length > 0) _sb.Append('\n');
            Line($"<i>{def.description}</i>", descriptionColor);
        }

        string sub = def is EquipmentItem e ? EquipmentItem.SlotName(e.slot)
                   : def is SkillGem ? $"{CategoryName(def.Category)}  ·  Level {level}"
                   : CategoryName(def.Category);
        if (def is SkillGem gemDef) rarity = gemDef.gemColor;
        if (!string.IsNullOrEmpty(header)) sub = $"{header}  ·  {sub}";
        string foot = def.IsStackable
            ? $"Stack: {count} / {def.StackLimit}   Weight: {def.weight} B each ({def.weight * count} B)"
            : $"Weight: {def.weight} B";
        if (!string.IsNullOrEmpty(hint)) foot += $"\n<color=#{ColorUtility.ToHtmlStringRGB(hintColor)}>{hint}</color>";
        Show(def.displayName, sub, _sb.ToString(), foot, rarity);
    }

    public void ShowText(string titleText, string subtitleText, string bodyText, Color tint) =>
        Show(titleText, subtitleText, bodyText, null, tint);

    public void Hide()
    {
        if (_group == null) return;
        _group.alpha = 0f;
    }

    public static string CategoryName(ItemCategory c) => c switch
    {
        ItemCategory.Equipment => "Equipment",
        ItemCategory.Usable => "Usable",
        ItemCategory.SkillGem => "Skill Gem",
        ItemCategory.SupportGem => "Support Gem",
        _ => "Other",
    };

    // ---------- Internal ----------

    private void Show(string titleText, string subtitleText, string bodyText, string footerText, Color tint)
    {
        SetText(title, titleText);
        if (title != null) title.color = tint;
        SetText(subtitle, subtitleText);
        SetText(body, bodyText);
        SetText(footer, footerText);
        if (rarityTint != null) rarityTint.color = new Color(tint.r, tint.g, tint.b, rarityTint.color.a);
        transform.SetAsLastSibling();
        _group.alpha = 1f;
        LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
        FollowCursor();
    }

    private void Line(string text, Color color)
    {
        if (_sb.Length > 0) _sb.Append('\n');
        _sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(color)).Append('>').Append(text).Append("</color>");
    }

    private static void SetText(TextMeshProUGUI t, string value)
    {
        if (t == null) return;
        bool has = !string.IsNullOrEmpty(value);
        t.gameObject.SetActive(has);
        if (has) t.text = value;
    }

    private void LateUpdate()
    {
        if (IsVisible) FollowCursor();
    }

    // วางข้างเคอร์เซอร์ ล้นขวา/ล่างแล้วพลิกไปอีกข้าง (UI presentation อ่านเมาส์ได้ ไม่ใช่ gameplay)
    private void FollowCursor()
    {
        if (Mouse.current == null || _canvas == null) return;
        var canvasRect = (RectTransform)_canvas.transform;
        Vector2 mouse = Mouse.current.position.ReadValue();
        Camera cam = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, mouse, cam, out Vector2 local)) return;

        Vector2 size = panel.rect.size;
        Rect bounds = canvasRect.rect;
        bool flipX = local.x + cursorOffset.x + size.x > bounds.xMax;
        bool flipY = local.y + cursorOffset.y - size.y < bounds.yMin;
        panel.pivot = new Vector2(flipX ? 1f : 0f, flipY ? 0f : 1f);
        Vector2 offset = new Vector2(flipX ? -cursorOffset.x : cursorOffset.x, flipY ? -cursorOffset.y : cursorOffset.y);
        Vector2 pos = local + offset;
        pos.x = Mathf.Clamp(pos.x, bounds.xMin + (flipX ? size.x : 0f), bounds.xMax - (flipX ? 0f : size.x));
        pos.y = Mathf.Clamp(pos.y, bounds.yMin + (flipY ? 0f : size.y), bounds.yMax - (flipY ? size.y : 0f));

        // panel อยู่ใต้ canvas ตรงๆ (anchor กลาง) -> แปลงจากพิกัด canvas เป็น anchoredPosition
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.anchoredPosition = pos;
    }
}

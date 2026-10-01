#if UNITY_EDITOR
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// สร้าง prefab ต้นแบบของ UI ในเกม (GameUI) ครั้งเดียว แล้วไปแก้ใน prefab ต่อเอง (ใส่ texture / ย้ายตำแหน่ง / เปลี่ยนฟอนต์)
// เมนู: Tools > Project-A > Build UI Template -> Assets/Resources/Gameobject/Prefab/UI/GameUI.prefab
// ทุกรูปใช้ sprite built-in ของ Unity (UISprite / Background) เป็นตัวแทน: เปลี่ยน Source Image ของแต่ละ Image ได้เลย
//   - หลอด (Fill / Trail) ต้องเป็น Image Type = Filled + มี sprite ถึงจะลดแบบ fillAmount (ไม่มี sprite = ยืดตามสัดส่วน)
//   - cooldown ของช่องสกิล = Filled / Radial 360
// รันซ้ำ = เขียนทับ prefab เดิม (มีถามก่อน) -> แก้ prefab แล้วอย่ารันซ้ำ
// หน้าเลือก/สร้างตัวละคร (prefab + scene แยก) อยู่ที่ GameUITemplateBuilder.CharacterSelect.cs
public static partial class GameUITemplateBuilder
{
    private const string Folder = "Assets/Resources/Gameobject/Prefab/UI";
    private const string PrefabPath = Folder + "/GameUI.prefab";

    private static Sprite _uiSprite;   // กรอบมุมมน 9-slice
    private static Sprite _background; // พื้นหลอด
    private static Sprite _knob;       // วงกลม

    // สีต้นแบบ (ของจริงใช้ texture แทน)
    private static readonly Color PanelColor = new Color(0.07f, 0.06f, 0.05f, 0.94f);
    private static readonly Color FrameColor = new Color(0.93f, 0.85f, 0.66f, 0.35f);
    private static readonly Color Gold = new Color(0.93f, 0.85f, 0.66f);
    private static readonly Color SlotColor = new Color(0f, 0f, 0f, 0.55f);

    [MenuItem("Tools/Project-A/Build UI Template")]
    private static void BuildMenu()
    {
        if (File.Exists(PrefabPath) &&
            !EditorUtility.DisplayDialog("Build UI Template", $"{PrefabPath} มีอยู่แล้ว เขียนทับ? (ที่แก้ไว้ใน prefab จะหาย)", "เขียนทับ", "ยกเลิก"))
            return;
        Build();
    }

    // เพิ่ม component ที่ HUD/กระเป๋าต้องใช้ลง Player.prefab (มีแล้วข้าม) + ช่องสกิลครบ 10
    [MenuItem("Tools/Project-A/Setup Player Prefab For UI")]
    public static void SetupPlayerPrefab()
    {
        const string path = "Assets/Resources/Gameobject/Player.prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            Ensure<Mana>(root);
            Ensure<PlayerConsumables>(root);
            Ensure<PlayerInventory>(root);
            Ensure<PlayerExperience>(root);
            Ensure<PlayerPassives>(root);
            Ensure<LocalPlayerUI>(root);

            var skills = root.GetComponent<PlayerSkills>();
            if (skills != null)
                while (skills.slots.Count < PlayerSkills.MaxSkillSlots)
                    skills.slots.Add(new SkillSlot { name = $"Skill {skills.slots.Count + 1}" });

            PrefabUtility.SaveAsPrefabAsset(root, path);
            Debug.Log($"[{nameof(GameUITemplateBuilder)}] ตั้งค่า {path} แล้ว");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        static void Ensure<T>(GameObject go) where T : Component
        {
            if (go.GetComponent<T>() == null) go.AddComponent<T>();
        }
    }

    private static void LoadSprites()
    {
        _uiSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        _background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
        _knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
        Directory.CreateDirectory(Folder);
    }

    public static GameObject Build()
    {
        LoadSprites();

        var root = new GameObject("GameUI", typeof(RectTransform));
        try
        {
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 90; // ใต้ LootStashWindow (100) และจอ fade
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            root.AddComponent<GraphicRaycaster>();
            root.AddComponent<GameUI>();

            BuildStatusHud(root.transform);
            BuildSkillBar(root.transform);
            BuildCharacterWindow(root.transform);
            BuildInventoryWindow(root.transform);
            BuildSkillGemWindow(root.transform);
            BuildMinimap(root.transform);
            BuildPassiveTreeWindow(root.transform);
            BuildLootStashWindow(root.transform);
            BuildPauseMenu(root.transform);
            BuildKeybindWindow(root.transform);
            BuildToast(root.transform);
            BuildConfirmDialog(root.transform);
            BuildTooltip(root.transform);

            // เกมใช้เมาส์กับ UI: ปิด keyboard navigation (ไม่งั้น WASD ไปเลื่อน slider/ปุ่มที่เพิ่งคลิก)
            foreach (var s in root.GetComponentsInChildren<Selectable>(true)) s.navigation = new Navigation { mode = Navigation.Mode.None };

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            ExportArtList();
            Debug.Log($"[{nameof(GameUITemplateBuilder)}] สร้าง {PrefabPath} แล้ว", prefab);
            return prefab;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    // ---------- HUD: HP / Mana / EXP (บนซ้าย) ----------

    private static void BuildStatusHud(Transform parent)
    {
        var hud = Rect("StatusHud", parent, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(440f, 124f));
        var view = hud.gameObject.AddComponent<StatusHud>();
        Img(hud, _uiSprite, new Color(0f, 0f, 0f, 0.45f), Image.Type.Sliced, raycast: false);

        // ป้ายเลเวล (วงกลมซ้าย)
        var badge = Rect("LevelBadge", hud, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(10f, -10f), new Vector2(76f, 76f));
        Img(badge, _knob, new Color(0.12f, 0.1f, 0.08f, 1f), Image.Type.Simple, raycast: false);
        var badgeRing = Stretch("Ring", badge, Vector2.zero, Vector2.zero);
        Img(badgeRing, _knob, FrameColor, Image.Type.Simple, raycast: false);
        badgeRing.localScale = Vector3.one * 1.08f;
        badgeRing.SetAsFirstSibling();
        var lvCaption = Rect("Caption", badge, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(60f, 16f));
        Text(lvCaption, "LV", 13f, TextAlignmentOptions.Center, new Color(0.7f, 0.65f, 0.55f));
        var lvText = Stretch("Level", badge, new Vector2(0f, 4f), new Vector2(0f, -10f));
        view.levelText = Text(lvText, "1", 30f, TextAlignmentOptions.Center, Gold, bold: true);

        view.healthBar = Bar("HealthBar", hud, new Vector2(98f, -12f), new Vector2(326f, 30f), new Color(0.72f, 0.12f, 0.1f), 16f);
        view.manaBar = Bar("ManaBar", hud, new Vector2(98f, -48f), new Vector2(326f, 24f), new Color(0.16f, 0.32f, 0.85f), 14f);
        view.experienceBar = Bar("ExpBar", hud, new Vector2(98f, -80f), new Vector2(326f, 14f), new Color(0.85f, 0.7f, 0.25f), 10f);
        view.experienceBar.trail.gameObject.SetActive(false);
        view.experienceBar.trail = null;
    }

    // ---------- HUD: สกิล 5 ช่อง (ล่างขวา) ----------

    private static void BuildSkillBar(Transform parent)
    {
        const float slot = 84f, gap = 8f;
        int count = PlayerAttackInput.DefaultSlotsPerSet;
        float width = count * slot + (count - 1) * gap + 24f;

        var bar = Rect("SkillBar", parent, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-24f, 24f), new Vector2(width, slot + 46f));
        var view = bar.gameObject.AddComponent<SkillBarView>();
        Img(bar, _uiSprite, new Color(0f, 0f, 0f, 0.45f), Image.Type.Sliced, raycast: false);

        // กรอบเรืองตอนค้าง Ctrl (ชุดที่ 2)
        var alt = Stretch("AlternateSetIndicator", bar, new Vector2(-3f, -3f), new Vector2(3f, 3f));
        Img(alt, _uiSprite, new Color(0.45f, 0.75f, 1f, 0.55f), Image.Type.Sliced, raycast: false);
        alt.SetAsFirstSibling();
        alt.gameObject.SetActive(false);
        view.alternateSetIndicator = alt.gameObject;

        var setRect = Rect("SetLabel", bar, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(12f, -4f), new Vector2(200f, 30f));
        view.setLabel = Text(setRect, "I", 20f, TextAlignmentOptions.MidlineLeft, Gold, bold: true);
        var hint = Rect("Hint", bar, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-12f, -4f), new Vector2(240f, 30f));
        Text(hint, "Hold Ctrl: Set II", 13f, TextAlignmentOptions.MidlineRight, new Color(0.6f, 0.6f, 0.6f));

        view.slots = new SkillSlotView[count];
        for (int i = 0; i < count; i++)
        {
            var s = Rect($"Slot{i + 1}", bar, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(12f + i * (slot + gap), 10f), new Vector2(slot, slot));
            view.slots[i] = SkillSlot(s);
        }
    }

    private static SkillSlotView SkillSlot(RectTransform s)
    {
        var v = s.gameObject.AddComponent<SkillSlotView>();
        Img(s, _uiSprite, SlotColor, Image.Type.Sliced); // รับ raycast (hover tooltip)

        var empty = Stretch("Empty", s, Vector2.zero, Vector2.zero);
        Text(empty, "-", 26f, TextAlignmentOptions.Center, new Color(1f, 1f, 1f, 0.15f));
        v.emptyState = empty.gameObject;

        var icon = Stretch("Icon", s, new Vector2(6f, 6f), new Vector2(-6f, -6f));
        v.icon = Img(icon, null, Color.white, Image.Type.Simple, raycast: false);
        v.icon.preserveAspect = true;
        v.icon.enabled = false;

        var cd = Stretch("Cooldown", s, new Vector2(6f, 6f), new Vector2(-6f, -6f));
        v.cooldownOverlay = Img(cd, _uiSprite, new Color(0f, 0f, 0f, 0.7f), Image.Type.Filled, raycast: false);
        v.cooldownOverlay.fillMethod = Image.FillMethod.Radial360;
        v.cooldownOverlay.fillOrigin = (int)Image.Origin360.Top;
        v.cooldownOverlay.fillClockwise = false;
        v.cooldownOverlay.enabled = false;
        var cdText = Stretch("CooldownText", s, Vector2.zero, Vector2.zero);
        v.cooldownText = Text(cdText, "", 24f, TextAlignmentOptions.Center, Color.white, bold: true);

        var frame = Stretch("Frame", s, Vector2.zero, Vector2.zero);
        Img(frame, _uiSprite, FrameColor, Image.Type.Sliced, raycast: false).fillCenter = false;

        var active = Stretch("ActiveHighlight", s, new Vector2(-2f, -2f), new Vector2(2f, 2f));
        Img(active, _uiSprite, Gold, Image.Type.Sliced, raycast: false).fillCenter = false;
        active.gameObject.SetActive(false);
        v.activeHighlight = active.gameObject;

        var key = Rect("Key", s, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-2f, 2f), new Vector2(64f, 20f));
        Img(key, _uiSprite, new Color(0f, 0f, 0f, 0.6f), Image.Type.Sliced, raycast: false);
        var keyText = Stretch("Text", key, new Vector2(2f, 0f), new Vector2(-4f, 0f));
        v.keyLabel = Text(keyText, "", 13f, TextAlignmentOptions.MidlineRight, Gold);
        return v;
    }

    // ---------- Character Sheet (ซ้าย) ----------

    private static void BuildCharacterWindow(Transform parent)
    {
        var win = Rect("CharacterWindow", parent, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(24f, -50f), new Vector2(520f, 830f));
        var view = win.gameObject.AddComponent<CharacterWindow>();
        WindowFrame(win, "Character", out view.closeButton, out var title);
        view.nameText = title;

        var level = Rect("Level", win, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-64f, 0f), new Vector2(160f, 56f));
        view.levelText = Text(level, "Level 1", 18f, TextAlignmentOptions.MidlineRight, new Color(0.75f, 0.7f, 0.6f));

        // ช่องสวมใส่รอบตัวละคร (ตำแหน่งจากกลางกรอบ)
        var doll = Rect("PaperDoll", win, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(480f, 380f));
        Img(doll, _uiSprite, new Color(1f, 1f, 1f, 0.03f), Image.Type.Sliced, raycast: false);
        var silhouette = Rect("Silhouette", doll, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(200f, 360f));
        Img(silhouette, _knob, new Color(1f, 1f, 1f, 0.03f), Image.Type.Simple, raycast: false);

        view.slots = new[]
        {
            EquipSlotBox(doll, EquipSlot.Helmet,     new Vector2(0f, 135f),    new Vector2(76f, 76f)),
            EquipSlotBox(doll, EquipSlot.Cloak,      new Vector2(-95f, 125f),  new Vector2(64f, 64f)),
            EquipSlotBox(doll, EquipSlot.Amulet,     new Vector2(90f, 125f),   new Vector2(52f, 52f)),
            EquipSlotBox(doll, EquipSlot.Weapon,     new Vector2(-175f, 35f),  new Vector2(84f, 160f)),
            EquipSlotBox(doll, EquipSlot.BodyArmour, new Vector2(0f, 35f),     new Vector2(96f, 124f)),
            EquipSlotBox(doll, EquipSlot.OffHand,    new Vector2(175f, 35f),   new Vector2(84f, 160f)),
            EquipSlotBox(doll, EquipSlot.Ring,       new Vector2(-82f, 20f),   new Vector2(48f, 48f)),
            EquipSlotBox(doll, EquipSlot.Ring2,      new Vector2(82f, 20f),    new Vector2(48f, 48f)),
            EquipSlotBox(doll, EquipSlot.Belt,       new Vector2(0f, -60f),    new Vector2(96f, 36f)),
            EquipSlotBox(doll, EquipSlot.Gloves,     new Vector2(-175f, -100f), new Vector2(76f, 76f)),
            EquipSlotBox(doll, EquipSlot.Boots,      new Vector2(175f, -100f), new Vector2(76f, 76f)),
        };

        // ค่าสถานะ 2 คอลัมน์ (ชื่อ | ค่า)
        var stats = Rect("Stats", win, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(-40f, 350f));
        Img(stats, _uiSprite, new Color(0f, 0f, 0f, 0.3f), Image.Type.Sliced, raycast: false);
        var names = Stretch("Names", stats, new Vector2(14f, 10f), new Vector2(-14f, -10f));
        view.statNames = Text(names, "", 15f, TextAlignmentOptions.TopLeft, new Color(0.75f, 0.7f, 0.6f));
        var values = Stretch("Values", stats, new Vector2(14f, 10f), new Vector2(-14f, -10f));
        view.statValues = Text(values, "", 15f, TextAlignmentOptions.TopRight, Color.white);
    }

    private static EquipSlotView EquipSlotBox(RectTransform parent, EquipSlot slot, Vector2 pos, Vector2 size)
    {
        var s = Rect(slot.ToString(), parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size);
        var v = s.gameObject.AddComponent<EquipSlotView>();
        v.slot = slot;
        Img(s, _uiSprite, SlotColor, Image.Type.Sliced);

        var empty = Stretch("Empty", s, new Vector2(2f, 2f), new Vector2(-2f, -2f));
        v.slotLabel = Text(empty, EquipmentItem.SlotName(slot), size.x < 60f ? 10f : 12f, TextAlignmentOptions.Center, new Color(1f, 1f, 1f, 0.25f));
        v.emptyState = empty.gameObject;

        var icon = Stretch("Icon", s, new Vector2(5f, 5f), new Vector2(-5f, -5f));
        v.icon = Img(icon, null, Color.white, Image.Type.Simple, raycast: false);
        v.icon.preserveAspect = true;
        v.icon.enabled = false;

        var frame = Stretch("Frame", s, Vector2.zero, Vector2.zero);
        v.frame = Img(frame, _uiSprite, new Color(1f, 1f, 1f, 0.15f), Image.Type.Sliced, raycast: false);
        v.frame.fillCenter = false;
        return v;
    }

    // ---------- กระเป๋า (ขวา) ----------

    private static void BuildInventoryWindow(Transform parent)
    {
        var win = Rect("InventoryWindow", parent, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-24f, 60f), new Vector2(540f, 720f));
        var view = win.gameObject.AddComponent<InventoryWindow>();
        WindowFrame(win, "Inventory", out view.closeButton, out _);

        // แท็บ
        var tabsRow = Rect("Tabs", win, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -62f), new Vector2(-28f, 40f));
        var h = tabsRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 6f;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = true;
        for (int i = 0; i < view.tabs.Length; i++)
        {
            var t = Rect($"Tab_{view.tabs[i].category}", tabsRow, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var img = Img(t, _uiSprite, new Color(1f, 1f, 1f, 0.06f), Image.Type.Sliced);
            var btn = t.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var sel = Stretch("Selected", t, Vector2.zero, Vector2.zero);
            Img(sel, _uiSprite, new Color(Gold.r, Gold.g, Gold.b, 0.22f), Image.Type.Sliced, raycast: false);
            var underline = Rect("Underline", sel, Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, 3f));
            Img(underline, null, Gold, Image.Type.Simple, raycast: false);
            var label = Stretch("Label", t, Vector2.zero, Vector2.zero);
            view.tabs[i].button = btn;
            view.tabs[i].selected = sel.gameObject;
            view.tabs[i].label = Text(label, view.tabs[i].title, 14f, TextAlignmentOptions.Center, Gold);
        }

        // ช่องค้นหา (คำค้นแยกต่อแท็บ)
        view.searchField = SearchField(win, new Vector2(-44f, -110f), new Vector2(-116f, 36f), out view.clearSearchButton);
        view.sortButton = TextButton(win, "Sort", new Vector2(1f, 1f), new Vector2(-14f, -110f), new Vector2(80f, 36f));

        // รายการ (เลื่อนได้)
        var scroll = ScrollList("List", win, new Vector2(12f, 84f), new Vector2(-12f, -154f), out var content);
        view.listContent = content;
        view.rowTemplate = InventoryRow(content);

        // ไอคอนที่ตามเมาส์ตอนลาก (อยู่ใต้ root canvas + canvas ซ้อนให้อยู่บนสุด)
        var ghost = Rect("DragGhost", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(56f, 56f));
        var ghostCanvas = ghost.gameObject.AddComponent<Canvas>();
        ghostCanvas.overrideSorting = true;
        ghostCanvas.sortingOrder = 190;
        view.dragGhost = Img(ghost, null, Color.white, Image.Type.Simple, raycast: false);
        view.dragGhost.preserveAspect = true;
        ghost.gameObject.SetActive(false);

        var empty = Stretch("Empty", scroll, Vector2.zero, Vector2.zero);
        view.emptyText = Text(empty, "Empty", 18f, TextAlignmentOptions.Center, new Color(0.5f, 0.5f, 0.5f));

        // น้ำหนัก
        var wLabel = Rect("WeightLabel", win, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(16f, 44f), new Vector2(90f, 26f));
        Text(wLabel, "Weight", 15f, TextAlignmentOptions.MidlineLeft, new Color(0.75f, 0.7f, 0.6f));
        view.weightBar = Bar("WeightBar", win, Vector2.zero, new Vector2(410f, 24f), view.weightNormal, 14f);
        var wRect = (RectTransform)view.weightBar.transform;
        wRect.anchorMin = wRect.anchorMax = wRect.pivot = new Vector2(1f, 0f);
        wRect.anchoredPosition = new Vector2(-16f, 45f);
        var hint = Rect("Hint", win, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(-32f, 24f));
        Text(hint, "Right-click: equip / use  ·  Drag outside to discard", 13f, TextAlignmentOptions.Center, new Color(0.55f, 0.55f, 0.55f));
    }

    private static InventoryRowView InventoryRow(RectTransform content)
    {
        var row = Rect("RowTemplate", content, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        var v = row.gameObject.AddComponent<InventoryRowView>();
        v.background = Img(row, _uiSprite, v.normalColor, Image.Type.Sliced); // รับ raycast (hover/คลิก)
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = 54f;
        var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(6, 12, 5, 5);
        h.spacing = 10f;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;

        var frame = Rect("IconFrame", row, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        v.iconFrame = Img(frame, _uiSprite, new Color(0.3f, 0.3f, 0.3f), Image.Type.Sliced, raycast: false);
        var fl = frame.gameObject.AddComponent<LayoutElement>();
        fl.minWidth = fl.preferredWidth = 44f;
        fl.preferredHeight = 44f;
        var icon = Stretch("Icon", frame, new Vector2(3f, 3f), new Vector2(-3f, -3f));
        v.icon = Img(icon, null, Color.white, Image.Type.Simple, raycast: false);
        v.icon.preserveAspect = true;
        var count = Rect("Count", frame, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-2f, 0f), new Vector2(40f, 18f));
        v.countText = Text(count, "", 13f, TextAlignmentOptions.BottomRight, Color.white, bold: true);

        var texts = Rect("Texts", row, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        texts.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        var tv = texts.gameObject.AddComponent<VerticalLayoutGroup>();
        tv.childControlWidth = tv.childControlHeight = true;
        tv.childForceExpandWidth = true;
        tv.childForceExpandHeight = false;
        tv.childAlignment = TextAnchor.MiddleLeft;
        var name = Rect("Name", texts, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        v.nameText = Text(name, "Item Name", 18f, TextAlignmentOptions.MidlineLeft, Color.white);
        v.nameText.textWrappingMode = TextWrappingModes.NoWrap;
        v.nameText.overflowMode = TextOverflowModes.Ellipsis;
        var type = Rect("Type", texts, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        v.typeText = Text(type, "Type", 13f, TextAlignmentOptions.MidlineLeft, new Color(0.55f, 0.55f, 0.55f));

        var weight = Rect("Weight", row, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        v.weightText = Text(weight, "0 B", 14f, TextAlignmentOptions.MidlineRight, new Color(0.75f, 0.7f, 0.6f));
        weight.gameObject.AddComponent<LayoutElement>().preferredWidth = 64f;
        return v;
    }

    // ---------- Tooltip ----------

    private static void BuildTooltip(Transform parent)
    {
        var tip = Rect("ItemTooltip", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 1f), Vector2.zero, new Vector2(340f, 100f));
        // canvas ซ้อนให้อยู่บนสุดเสมอ (ทับหน้าต่างของดรอปด้วย)
        var c = tip.gameObject.AddComponent<Canvas>();
        c.overrideSorting = true;
        c.sortingOrder = 200;
        var view = tip.gameObject.AddComponent<ItemTooltip>();
        view.panel = tip;
        Img(tip, _uiSprite, new Color(0.04f, 0.035f, 0.03f, 0.96f), Image.Type.Sliced, raycast: false);
        var v = tip.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(16, 16, 0, 14);
        v.spacing = 6f;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        tip.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var bar = Rect("RarityBar", tip, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        view.rarityTint = Img(bar, null, new Color(1f, 1f, 1f, 0.8f), Image.Type.Simple, raycast: false);
        bar.gameObject.AddComponent<LayoutElement>().preferredHeight = 4f;

        var title = Rect("Title", tip, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        view.title = Text(title, "Item Name", 22f, TextAlignmentOptions.Center, Color.white, bold: true);
        var sub = Rect("Subtitle", tip, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        view.subtitle = Text(sub, "Type", 14f, TextAlignmentOptions.Center, new Color(0.6f, 0.6f, 0.6f));
        var sep = Rect("Separator", tip, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        Img(sep, null, FrameColor, Image.Type.Simple, raycast: false);
        sep.gameObject.AddComponent<LayoutElement>().preferredHeight = 1f;
        var body = Rect("Body", tip, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        view.body = Text(body, "", 16f, TextAlignmentOptions.Center, Color.white);
        var footer = Rect("Footer", tip, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        view.footer = Text(footer, "", 13f, TextAlignmentOptions.Center, new Color(0.75f, 0.7f, 0.6f));
    }

    // ---------- Skill Gems (กลางจอ): ช่องสกิล | gem หลัก + support + link | gem ในกระเป๋า ----------

    private static void BuildSkillGemWindow(Transform parent)
    {
        var win = Rect("SkillGemWindow", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(1060f, 660f));
        var view = win.gameObject.AddComponent<SkillGemWindow>();
        WindowFrame(win, "Skill Gems", out view.closeButton, out _);

        // ซ้าย: รายการช่องสกิล
        var left = Column("SlotsColumn", win, 12f, 260f);
        var leftTitle = Rect("Title", left, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 28f));
        Text(leftTitle, "Skill Slots", 16f, TextAlignmentOptions.MidlineLeft, new Color(0.75f, 0.7f, 0.6f));
        ScrollList("SlotList", left, new Vector2(0f, 0f), new Vector2(0f, -34f), out var slotContent);
        view.slotListContent = slotContent;
        view.slotTemplate = SlotListItem(slotContent);

        // กลาง: ช่อง gem + link
        var mid = Column("SocketsColumn", win, 286f, 400f);
        Img(mid, _uiSprite, new Color(0f, 0f, 0f, 0.25f), Image.Type.Sliced, raycast: false);
        var title = Rect("SlotTitle", mid, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(-20f, 30f));
        view.slotTitle = Text(title, "Skill Slot", 18f, TextAlignmentOptions.Center, Gold, bold: true);

        view.activeSocket = Socket(mid, "ActiveSocket", new Vector2(0f, -50f), 96f, SocketedGem.ActiveSocket, withName: true);

        // เส้น link: ลำต้นจาก gem หลักลงมา + คานแนวนอน + เส้นลงไปแต่ละ support (เส้นของแต่ละ support ติดสีเมื่อ link)
        const float supportSize = 62f, supportGap = 12f, supportsY = -262f, busY = -202f;
        float rowWidth = PlayerSkills.MaxSupportSlots * supportSize + (PlayerSkills.MaxSupportSlots - 1) * supportGap;
        var trunk = Rect("LinkTrunk", mid, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -178f), new Vector2(4f, 24f));
        Img(trunk, null, new Color(1f, 1f, 1f, 0.25f), Image.Type.Simple, raycast: false);
        var bus = Rect("LinkBus", mid, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, busY), new Vector2(rowWidth - supportSize + 4f, 4f));
        Img(bus, null, new Color(1f, 1f, 1f, 0.25f), Image.Type.Simple, raycast: false);

        view.supportSockets = new GemSocketView[PlayerSkills.MaxSupportSlots];
        view.links = new Image[PlayerSkills.MaxSupportSlots];
        for (int i = 0; i < PlayerSkills.MaxSupportSlots; i++)
        {
            float x = -rowWidth * 0.5f + supportSize * 0.5f + i * (supportSize + supportGap);
            var link = Rect($"Link{i + 1}", mid, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(x, busY), new Vector2(6f, busY - supportsY));
            view.links[i] = Img(link, null, new Color(1f, 1f, 1f, 0.12f), Image.Type.Simple, raycast: false);
            view.supportSockets[i] = Socket(mid, $"Support{i + 1}", new Vector2(x, supportsY), supportSize, i, withName: false);
        }
        var supLabel = Rect("SupportsLabel", mid, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, supportsY - supportSize - 4f), new Vector2(-20f, 22f));
        Text(supLabel, "Support Gems (linked)", 13f, TextAlignmentOptions.Center, new Color(0.55f, 0.55f, 0.55f));

        var stats = Rect("Resolved", mid, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(-24f, 150f));
        Img(stats, _uiSprite, new Color(0f, 0f, 0f, 0.3f), Image.Type.Sliced, raycast: false);
        var statsText = Stretch("Text", stats, new Vector2(12f, 8f), new Vector2(-12f, -8f));
        view.resolvedText = Text(statsText, "", 15f, TextAlignmentOptions.TopLeft, new Color(0.75f, 0.7f, 0.6f));

        // ขวา: gem ในกระเป๋าที่ใส่ช่องที่เลือกได้
        var right = Column("PickerColumn", win, 698f, 350f);
        var pTitle = Rect("Title", right, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 28f));
        view.pickerTitle = Text(pTitle, "Skill Gems", 16f, TextAlignmentOptions.MidlineLeft, new Color(0.75f, 0.7f, 0.6f));
        view.searchField = SearchField(right, new Vector2(0f, -32f), new Vector2(0f, 34f), out _);
        var pickScroll = ScrollList("GemList", right, new Vector2(0f, 0f), new Vector2(0f, -72f), out var pickContent);
        view.pickerContent = pickContent;
        view.pickerRowTemplate = InventoryRow(pickContent);
        var pEmpty = Stretch("Empty", pickScroll, Vector2.zero, Vector2.zero);
        view.pickerEmptyText = Text(pEmpty, "No gems in bag", 16f, TextAlignmentOptions.Center, new Color(0.5f, 0.5f, 0.5f));

        var hint = Rect("Hint", win, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -16f), new Vector2(-460f, 24f));
        Text(hint, "Click a socket, then a gem  ·  Right-click a socket to remove", 13f, TextAlignmentOptions.Center, new Color(0.55f, 0.55f, 0.55f));
    }

    // คอลัมน์สูงเต็มหน้าต่าง (ใต้หัว) เริ่มที่ x กว้าง w
    private static RectTransform Column(string name, RectTransform win, float x, float w)
    {
        var col = Rect(name, win, Vector2.zero, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
        col.offsetMin = new Vector2(x, 12f);
        col.offsetMax = new Vector2(x + w, -66f);
        return col;
    }

    private static GemSocketView Socket(RectTransform parent, string name, Vector2 pos, float size, int socket, bool withName)
    {
        var s = Rect(name, parent, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), pos, new Vector2(size, size));
        var v = s.gameObject.AddComponent<GemSocketView>();
        v.socket = socket;

        // กรอบเรืองตอนถูกเลือก (ใหญ่กว่าช่องนิดหน่อย อยู่หลังสุด)
        var sel = Stretch("Selected", s, new Vector2(-5f, -5f), new Vector2(5f, 5f));
        Img(sel, _knob, new Color(Gold.r, Gold.g, Gold.b, 0.45f), Image.Type.Simple, raycast: false);
        v.selectedState = sel.gameObject;

        // พื้นช่อง (รับ raycast คลิก/hover) = frame ที่ย้อมสีตาม gem
        var bg = Stretch("Frame", s, Vector2.zero, Vector2.zero);
        v.frame = Img(bg, _knob, SlotColor, Image.Type.Simple);

        var empty = Stretch("Empty", s, Vector2.zero, Vector2.zero);
        Text(empty, "+", size * 0.4f, TextAlignmentOptions.Center, new Color(1f, 1f, 1f, 0.25f));
        v.emptyState = empty.gameObject;

        var icon = Stretch("Icon", s, new Vector2(size * 0.14f, size * 0.14f), new Vector2(-size * 0.14f, -size * 0.14f));
        v.icon = Img(icon, null, Color.white, Image.Type.Simple, raycast: false);
        v.icon.preserveAspect = true;
        v.icon.enabled = false;

        if (withName)
        {
            var n = Rect("Name", s, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(300f, 24f));
            v.nameText = Text(n, "Skill Gem", 16f, TextAlignmentOptions.Center, Color.white);
        }
        return v;
    }

    private static SkillSlotListItem SlotListItem(RectTransform content)
    {
        var row = Rect("SlotTemplate", content, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        var v = row.gameObject.AddComponent<SkillSlotListItem>();
        var bg = Img(row, _uiSprite, new Color(1f, 1f, 1f, 0.04f), Image.Type.Sliced);
        v.button = row.gameObject.AddComponent<Button>();
        v.button.targetGraphic = bg;
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = 56f;

        var sel = Stretch("Selected", row, Vector2.zero, Vector2.zero);
        Img(sel, _uiSprite, new Color(Gold.r, Gold.g, Gold.b, 0.2f), Image.Type.Sliced, raycast: false);
        v.selectedState = sel.gameObject;

        var frame = Rect("IconFrame", row, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(6f, 0f), new Vector2(44f, 44f));
        Img(frame, _uiSprite, SlotColor, Image.Type.Sliced, raycast: false);
        var icon = Stretch("Icon", frame, new Vector2(3f, 3f), new Vector2(-3f, -3f));
        v.icon = Img(icon, null, Color.white, Image.Type.Simple, raycast: false);
        v.icon.preserveAspect = true;

        var key = Rect("Key", row, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(58f, -4f), new Vector2(-64f, 20f));
        v.keyText = Text(key, "I · LMB", 13f, TextAlignmentOptions.MidlineLeft, new Color(0.75f, 0.7f, 0.6f));
        var links = Rect("Links", row, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-8f, -4f), new Vector2(90f, 20f));
        v.linksText = Text(links, "0/5 links", 12f, TextAlignmentOptions.MidlineRight, new Color(0.55f, 0.55f, 0.55f));
        var name = Rect("Name", row, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(58f, 6f), new Vector2(-64f, 24f));
        v.nameText = Text(name, "Empty", 16f, TextAlignmentOptions.MidlineLeft, Color.white);
        v.nameText.textWrappingMode = TextWrappingModes.NoWrap;
        v.nameText.overflowMode = TextOverflowModes.Ellipsis;
        return v;
    }

    // ---------- Minimap (ขวาบน) + แผนที่เต็มจอ (Tab) ----------

    private static void BuildMinimap(Transform parent)
    {
        // overlay เต็มจอ: สร้างก่อน minimap -> อยู่ใต้ HUD มุมจอ (ไม่รับคลิก ความทึบปรับได้)
        var overlayRoot = Stretch("MapOverlay", parent, new Vector2(80f, 60f), new Vector2(-80f, -60f));
        var overlayGroup = overlayRoot.gameObject.AddComponent<CanvasGroup>();
        var overlayRotator = Rect("Rotator", overlayRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        var overlayMap = Rect("Map", overlayRotator, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(100f, 100f));
        var overlayImage = overlayMap.gameObject.AddComponent<RawImage>();
        overlayImage.raycastTarget = false;
        var overlayPlayer = PlayerArrow(overlayRoot, 18f);

        var root = Rect("Minimap", parent, Vector2.one, Vector2.one, Vector2.one, new Vector2(-24f, -24f), new Vector2(250f, 250f));
        var view = root.gameObject.AddComponent<MinimapView>();
        var miniGroup = root.gameObject.AddComponent<CanvasGroup>();
        Img(root, _uiSprite, new Color(0f, 0f, 0f, 0.5f), Image.Type.Sliced); // รับ raycast (ล้อเมาส์ซูม)

        // กรอบตัดภาพ: เปลี่ยน sprite เป็นวงกลมได้ถ้าอยากได้ minimap กลม
        var viewport = Stretch("Viewport", root, new Vector2(6f, 6f), new Vector2(-6f, -6f));
        Img(viewport, _uiSprite, new Color(0.03f, 0.03f, 0.03f, 1f), Image.Type.Sliced, raycast: false);
        viewport.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        var rotator = Rect("Rotator", viewport, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        var map = Rect("Map", rotator, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(100f, 100f));
        var mapImage = map.gameObject.AddComponent<RawImage>();
        mapImage.raycastTarget = false;
        var player = PlayerArrow(viewport, 12f);

        view.minimap = new MinimapView.Display { viewport = viewport, rotator = rotator, mapImage = mapImage, playerIcon = player, followPlayer = true, group = miniGroup };
        view.overlay = new MinimapView.Display { viewport = overlayRoot, rotator = overlayRotator, mapImage = overlayImage, playerIcon = overlayPlayer, followPlayer = false, group = overlayGroup };

        // ไอคอนต้นแบบ (สีแดง/ส้ม/ฟ้า ตัดกับแผนที่ขาวดำ)
        view.enemyIconTemplate = Dot(viewport, "EnemyIcon", 7f, new Color(0.95f, 0.2f, 0.15f));
        view.eliteIconTemplate = Dot(viewport, "EliteIcon", 11f, new Color(1f, 0.55f, 0.1f));
        view.interactableIconTemplate = Dot(viewport, "InteractableIcon", 10f, new Color(0.3f, 0.75f, 1f));
        view.interactableIconTemplate.sprite = _uiSprite;
        view.interactableIconTemplate.type = Image.Type.Sliced;
        view.interactableIconTemplate.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);

        var border = Stretch("Border", root, Vector2.zero, Vector2.zero);
        Img(border, _uiSprite, FrameColor, Image.Type.Sliced, raycast: false).fillCenter = false;

        var label = Rect("AreaName", root, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 1f), new Vector2(0f, -4f), new Vector2(0f, 22f));
        view.areaLabel = Text(label, "", 14f, TextAlignmentOptions.Center, Gold);
        var tabHint = Rect("TabHint", root, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(0f, 18f));
        Text(tabHint, "Tab: full map", 12f, TextAlignmentOptions.Center, new Color(0.55f, 0.55f, 0.55f));

        // slider ความทึบ overlay: แท่งเล็กแนวตั้งด้านขวาของ minimap
        view.opacitySlider = VerticalSlider(root, "OverlayOpacity", new Vector2(1f, 0.5f), new Vector2(8f, 0f), new Vector2(14f, 180f));
    }

    private static RectTransform PlayerArrow(RectTransform parent, float size)
    {
        var player = Rect("PlayerIcon", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
        Img(player, _knob, new Color(0.2f, 1f, 0.4f), Image.Type.Simple, raycast: false);
        var nose = Rect("Nose", player, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0f), new Vector2(0f, -2f), new Vector2(size * 0.35f, size * 0.75f));
        Img(nose, null, new Color(0.2f, 1f, 0.4f), Image.Type.Simple, raycast: false);
        return player;
    }

    // slider แนวตั้ง (ลากขึ้น = ค่ามาก) pos = จากจุด anchor ไปทางขวา
    private static Slider VerticalSlider(RectTransform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        var r = Rect(name, parent, anchor, anchor, new Vector2(0f, 0.5f), pos, size);
        var bg = Img(r, _background, new Color(0f, 0f, 0f, 0.6f), Image.Type.Sliced);
        var fillArea = Stretch("Fill Area", r, new Vector2(3f, 3f), new Vector2(-3f, -3f));
        var fill = Stretch("Fill", fillArea, Vector2.zero, Vector2.zero);
        Img(fill, _uiSprite, new Color(Gold.r, Gold.g, Gold.b, 0.6f), Image.Type.Sliced, raycast: false);
        var handleArea = Stretch("Handle Slide Area", r, new Vector2(0f, 6f), new Vector2(0f, -6f));
        var handle = Rect("Handle", handleArea, Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(4f, 12f));
        var handleImg = Img(handle, _uiSprite, Gold, Image.Type.Sliced);

        var slider = r.gameObject.AddComponent<Slider>();
        slider.direction = Slider.Direction.BottomToTop;
        slider.fillRect = fill;
        slider.handleRect = handle;
        slider.targetGraphic = handleImg;
        slider.minValue = 0.05f;
        slider.maxValue = 1f;
        slider.value = 0.6f;
        _ = bg;
        return slider;
    }

    private static Image Dot(RectTransform parent, string name, float size, Color color)
    {
        var r = Rect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
        var img = Img(r, _knob, color, Image.Type.Simple, raycast: false);
        r.gameObject.SetActive(false);
        return img;
    }

    // ---------- ช่องค้นหา / รายการเลื่อนได้ ----------

    // ช่องค้นหาชื่อ: [ปุ่ม search = โฟกัสช่องพิมพ์] [ช่องพิมพ์] [x = ล้าง] (pos/size จากขอบบนของ parent กว้างเต็ม parent + size.x)
    private static TMP_InputField SearchField(RectTransform parent, Vector2 pos, Vector2 size, out Button clear)
    {
        var r = Rect("Search", parent, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), pos, size);
        var bg = Img(r, _uiSprite, new Color(0f, 0f, 0f, 0.5f), Image.Type.Sliced);

        var area = Stretch("Text Area", r, new Vector2(38f, 2f), new Vector2(-36f, -2f));
        area.gameObject.AddComponent<RectMask2D>();
        var ph = Stretch("Placeholder", area, Vector2.zero, Vector2.zero);
        var placeholder = Text(ph, "Search...  (\"a ppl\" finds Apple)", 14f, TextAlignmentOptions.MidlineLeft, new Color(1f, 1f, 1f, 0.3f));
        placeholder.fontStyle = FontStyles.Italic;
        var tx = Stretch("Text", area, Vector2.zero, Vector2.zero);
        var text = Text(tx, "", 16f, TextAlignmentOptions.MidlineLeft, Color.white);

        var field = r.gameObject.AddComponent<TMP_InputField>();
        field.targetGraphic = bg;
        field.textViewport = area;
        field.textComponent = text;
        field.placeholder = placeholder;
        field.lineType = TMP_InputField.LineType.SingleLine;
        field.pointSize = 16f;

        // ปุ่ม search: กดแล้วโฟกัสช่องพิมพ์ (เปลี่ยนเป็นรูปแว่นขยายได้)
        var icon = Rect("SearchButton", r, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(4f, 0f), new Vector2(28f, 28f));
        var iconImg = Img(icon, _knob, new Color(Gold.r, Gold.g, Gold.b, 0.5f), Image.Type.Simple);
        var iconBtn = icon.gameObject.AddComponent<Button>();
        iconBtn.targetGraphic = iconImg;
        UnityEditor.Events.UnityEventTools.AddVoidPersistentListener(iconBtn.onClick, field.ActivateInputField);
        var glyph = Stretch("Glyph", icon, Vector2.zero, Vector2.zero);
        Text(glyph, "Q", 15f, TextAlignmentOptions.Center, new Color(0f, 0f, 0f, 0.7f), bold: true);

        var x = Rect("Clear", r, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-4f, 0f), new Vector2(28f, 28f));
        var xImg = Img(x, _uiSprite, new Color(1f, 1f, 1f, 0.06f), Image.Type.Sliced);
        clear = x.gameObject.AddComponent<Button>();
        clear.targetGraphic = xImg;
        var xt = Stretch("X", x, Vector2.zero, Vector2.zero);
        Text(xt, "x", 15f, TextAlignmentOptions.Center, new Color(0.8f, 0.8f, 0.8f));
        return field;
    }

    // พื้นที่รายการเลื่อนขึ้นลง (offset จากขอบ parent) content = ที่วางแถว (VerticalLayout)
    private static RectTransform ScrollList(string name, RectTransform parent, Vector2 offsetMin, Vector2 offsetMax, out RectTransform content)
    {
        var scroll = Stretch(name, parent, offsetMin, offsetMax);
        Img(scroll, _uiSprite, new Color(0f, 0f, 0f, 0.25f), Image.Type.Sliced);
        var sr = scroll.gameObject.AddComponent<ScrollRect>();
        sr.horizontal = false;
        sr.movementType = ScrollRect.MovementType.Clamped;
        sr.scrollSensitivity = 30f;
        var viewport = Stretch("Viewport", scroll, new Vector2(4f, 4f), new Vector2(-4f, -4f));
        viewport.gameObject.AddComponent<RectMask2D>();
        sr.viewport = viewport;
        content = Rect("Content", viewport, new Vector2(0f, 1f), Vector2.one, new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
        var v = content.gameObject.AddComponent<VerticalLayoutGroup>();
        v.spacing = 3f;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        sr.content = content;
        return scroll;
    }

    // ---------- กล่องยืนยัน (กลางจอ มีพื้นทึบกันคลิกทะลุ) ----------

    private static void BuildConfirmDialog(Transform parent)
    {
        var root = Stretch("ConfirmDialog", parent, Vector2.zero, Vector2.zero);
        var view = root.gameObject.AddComponent<ConfirmDialog>();
        Img(root, null, new Color(0f, 0f, 0f, 0.55f), Image.Type.Simple); // บล็อกคลิกทั้งจอ

        var panel = Rect("Panel", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 60f), new Vector2(480f, 280f));
        Img(panel, _uiSprite, PanelColor, Image.Type.Sliced);
        var border = Stretch("Border", panel, Vector2.zero, Vector2.zero);
        Img(border, _uiSprite, FrameColor, Image.Type.Sliced, raycast: false).fillCenter = false;

        var title = Rect("Title", panel, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(-32f, 40f));
        view.titleText = Text(title, "Confirm", 22f, TextAlignmentOptions.Center, Gold, bold: true);
        var msg = Rect("Message", panel, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        msg.offsetMin = new Vector2(24f, 118f);
        msg.offsetMax = new Vector2(-24f, -54f);
        view.messageText = Text(msg, "Are you sure?", 18f, TextAlignmentOptions.Center, Color.white);

        view.confirmButton = TextButton(panel, "Discard", new Vector2(0.5f, 0f), new Vector2(-12f, 18f), new Vector2(150f, 42f), pivotX: 1f);
        view.confirmButton.targetGraphic.color = new Color(0.55f, 0.15f, 0.12f, 1f);
        view.confirmLabel = view.confirmButton.GetComponentInChildren<TextMeshProUGUI>();
        view.cancelButton = TextButton(panel, "Cancel", new Vector2(0.5f, 0f), new Vector2(12f, 18f), new Vector2(150f, 42f), pivotX: 0f);

        // เลือกจำนวน (แสดงเฉพาะตอนถามจำนวน): [slider ------] [ 12 ] / 20
        var amount = Rect("Amount", panel, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 74f), new Vector2(420f, 36f));
        view.amountGroup = amount.gameObject;

        var sliderRect = Rect("Slider", amount, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(250f, 18f));
        Img(sliderRect, _background, new Color(0f, 0f, 0f, 0.6f), Image.Type.Sliced);
        var fillArea = Stretch("Fill Area", sliderRect, new Vector2(3f, 3f), new Vector2(-3f, -3f));
        var fill = Stretch("Fill", fillArea, Vector2.zero, Vector2.zero);
        Img(fill, _uiSprite, new Color(Gold.r, Gold.g, Gold.b, 0.5f), Image.Type.Sliced, raycast: false);
        var handleArea = Stretch("Handle Slide Area", sliderRect, new Vector2(8f, 0f), new Vector2(-8f, 0f));
        var handle = Rect("Handle", handleArea, Vector2.zero, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(16f, 8f));
        var handleImg = Img(handle, _knob, Gold, Image.Type.Simple);
        var slider = sliderRect.gameObject.AddComponent<Slider>();
        slider.direction = Slider.Direction.LeftToRight;
        slider.fillRect = fill;
        slider.handleRect = handle;
        slider.targetGraphic = handleImg;
        slider.wholeNumbers = true;
        slider.minValue = 1f;
        slider.maxValue = 20f;
        slider.value = 20f;
        view.amountSlider = slider;

        var fieldRect = Rect("Field", amount, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(266f, 0f), new Vector2(76f, 34f));
        var fieldBg = Img(fieldRect, _uiSprite, new Color(0f, 0f, 0f, 0.55f), Image.Type.Sliced);
        var area = Stretch("Text Area", fieldRect, new Vector2(6f, 2f), new Vector2(-6f, -2f));
        area.gameObject.AddComponent<RectMask2D>();
        var tx = Stretch("Text", area, Vector2.zero, Vector2.zero);
        var text = Text(tx, "20", 18f, TextAlignmentOptions.Center, Color.white, bold: true);
        var field = fieldRect.gameObject.AddComponent<TMP_InputField>();
        field.targetGraphic = fieldBg;
        field.textViewport = area;
        field.textComponent = text;
        field.contentType = TMP_InputField.ContentType.IntegerNumber;
        field.lineType = TMP_InputField.LineType.SingleLine;
        field.characterLimit = 5;
        field.pointSize = 18f;
        view.amountField = field;

        var max = Rect("Max", amount, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(350f, 0f), new Vector2(70f, 34f));
        view.amountMaxText = Text(max, "/ 20", 18f, TextAlignmentOptions.MidlineLeft, new Color(0.75f, 0.7f, 0.6f));
        amount.gameObject.SetActive(false);
    }

    // ปุ่มข้อความ (anchor = มุมที่ยึด, pivot y ตาม anchor y)
    private static Button TextButton(RectTransform parent, string label, Vector2 anchor, Vector2 pos, Vector2 size, float pivotX = -1f)
    {
        var pivot = new Vector2(pivotX >= 0f ? pivotX : anchor.x, anchor.y);
        var r = Rect(label + "Button", parent, anchor, anchor, pivot, pos, size);
        var img = Img(r, _uiSprite, new Color(Gold.r, Gold.g, Gold.b, 0.18f), Image.Type.Sliced);
        var btn = r.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        var t = Stretch("Label", r, Vector2.zero, Vector2.zero);
        Text(t, label, 16f, TextAlignmentOptions.Center, Gold, bold: true);
        return btn;
    }

    // ---------- Passive Tree (กลางจอ เกือบเต็ม) ----------

    private static void BuildPassiveTreeWindow(Transform parent)
    {
        var win = Rect("PassiveTreeWindow", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(1500f, 880f));
        var view = win.gameObject.AddComponent<PassiveTreeWindow>();
        WindowFrame(win, "Passive Tree", out view.closeButton, out _);

        var points = Rect("Points", win, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 0f), new Vector2(500f, 56f));
        view.pointsText = Text(points, "Passive Points: 0", 20f, TextAlignmentOptions.Center, Gold, bold: true);
        view.resetButton = TextButton(win, "Reset", new Vector2(1f, 1f), new Vector2(-60f, -8f), new Vector2(100f, 40f));
        var hint = Rect("Hint", win, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(-32f, 22f));
        Text(hint, "Click: allocate  ·  Right-click: refund  ·  Drag: move  ·  Wheel: zoom", 13f, TextAlignmentOptions.Center, new Color(0.55f, 0.55f, 0.55f));

        // พื้นที่ tree: พื้นรับ raycast (ลากเลื่อน) + ตัดขอบ
        var viewport = Stretch("Viewport", win, new Vector2(12f, 38f), new Vector2(-12f, -62f));
        Img(viewport, _uiSprite, new Color(0.02f, 0.02f, 0.03f, 0.98f), Image.Type.Sliced);
        viewport.gameObject.AddComponent<RectMask2D>();
        view.viewport = viewport;
        var content = Rect("Content", viewport, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        view.content = content;
        view.lineLayer = Rect("Lines", content, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        view.nodeLayer = Rect("Nodes", content, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

        var line = Rect("LineTemplate", content, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(100f, 6f));
        view.lineTemplate = Img(line, null, Color.white, Image.Type.Simple, raycast: false);

        view.smallTemplate = PassiveNodeTemplate(content, "SmallTemplate", 30f);
        view.notableTemplate = PassiveNodeTemplate(content, "NotableTemplate", 52f);
        view.keystoneTemplate = PassiveNodeTemplate(content, "KeystoneTemplate", 72f);
    }

    // node: [กรอบ Start] [กรอบ Available] [กรอบทอง Allocated] [พื้นวงกลม] [ไอคอน]
    private static PassiveNodeView PassiveNodeTemplate(RectTransform parent, string name, float size)
    {
        var r = Rect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
        var v = r.gameObject.AddComponent<PassiveNodeView>();

        var start = Stretch("Start", r, new Vector2(-8f, -8f), new Vector2(8f, 8f));
        Img(start, _knob, new Color(0.3f, 1f, 0.45f, 0.8f), Image.Type.Simple, raycast: false);
        v.startState = start.gameObject;
        var avail = Stretch("Available", r, new Vector2(-4f, -4f), new Vector2(4f, 4f));
        Img(avail, _knob, new Color(1f, 1f, 1f, 0.55f), Image.Type.Simple, raycast: false);
        v.availableState = avail.gameObject;
        var alloc = Stretch("Allocated", r, new Vector2(-4f, -4f), new Vector2(4f, 4f));
        Img(alloc, _knob, Gold, Image.Type.Simple, raycast: false);
        v.allocatedState = alloc.gameObject;

        var fill = Stretch("Fill", r, Vector2.zero, Vector2.zero);
        v.fill = Img(fill, _knob, v.lockedColor, Image.Type.Simple); // รับ raycast (คลิก/hover)
        var icon = Stretch("Icon", r, new Vector2(size * 0.18f, size * 0.18f), new Vector2(-size * 0.18f, -size * 0.18f));
        v.icon = Img(icon, null, Color.white, Image.Type.Simple, raycast: false);
        v.icon.preserveAspect = true;
        v.icon.enabled = false;
        return v;
    }

    // ---------- กล่องของดรอป (Hideout) ----------

    private static void BuildLootStashWindow(Transform parent)
    {
        var win = Rect("LootStashWindow", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(480f, 600f));
        var view = win.gameObject.AddComponent<LootStashWindow>();
        WindowFrame(win, "Loot", out view.closeButton, out view.titleText);
        view.takeAllButton = TextButton(win, "Take All", new Vector2(1f, 1f), new Vector2(-56f, -8f), new Vector2(120f, 40f));

        var scroll = ScrollList("List", win, new Vector2(12f, 40f), new Vector2(-12f, -64f), out var content);
        view.listContent = content;
        view.rowTemplate = InventoryRow(content);
        var empty = Stretch("Empty", scroll, Vector2.zero, Vector2.zero);
        view.emptyText = Text(empty, "No items yet", 18f, TextAlignmentOptions.Center, new Color(0.5f, 0.5f, 0.5f));
        var hint = Rect("Hint", win, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(-32f, 22f));
        Text(hint, "Click an item to take it", 13f, TextAlignmentOptions.Center, new Color(0.55f, 0.55f, 0.55f));
    }

    // ---------- ข้อความแจ้งเตือน (กลางจอบน) ----------

    private static void BuildToast(Transform parent)
    {
        var r = Rect("Toast", parent, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -140f), new Vector2(520f, 46f));
        var c = r.gameObject.AddComponent<Canvas>();
        c.overrideSorting = true;
        c.sortingOrder = 210;
        var view = r.gameObject.AddComponent<UIToast>();
        Img(r, _uiSprite, new Color(0f, 0f, 0f, 0.7f), Image.Type.Sliced, raycast: false);
        var t = Stretch("Text", r, new Vector2(12f, 0f), new Vector2(-12f, 0f));
        view.messageText = Text(t, "Message", 18f, TextAlignmentOptions.Center, new Color(1f, 0.85f, 0.5f), bold: true);
    }

    // ---------- Export รายการ art ----------

    private const string ArtExportFolder = "ArtExport/UI"; // นอก Assets (Unity ไม่ import)

    [MenuItem("Tools/Project-A/Export UI Art List")]
    public static void ExportArtMenu()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null && AssetDatabase.LoadAssetAtPath<GameObject>(CharacterSelectPrefabPath) == null)
        {
            EditorUtility.DisplayDialog("Export UI Art", "ยังไม่มี UI prefab (Build UI Template / Build Character Select UI ก่อน)", "OK");
            return;
        }
        string path = ExportArtList();
        EditorUtility.RevealInFinder(path);
    }

    // prefab UI ที่ export (GameUI = โฟลเดอร์ละหน้าต่างที่ root แบบเดิม / ตัวอื่น = ArtExport/UI/<prefab>/<หน้าต่าง>)
    private static string[] ArtExportPrefabs => new[] { PrefabPath, CharacterSelectPrefabPath };

    // เขียน ArtExport/UI/UI-Art-List.md (ทุก Image/RawImage: หน้าต่าง / path / ขนาด px ที่ 1920x1080 / ชนิด) + PNG เปล่าตามขนาดจริงแยกโฟลเดอร์ตามหน้าต่าง
    // (ไอเทม/สกิล/passive node ใช้รูปจาก asset ของตัวเอง ไม่อยู่ในรายการนี้ / รูป race อยู่หัวข้อ Races)
    public static string ExportArtList()
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ArtExportFolder));
        if (Directory.Exists(root)) Directory.Delete(root, true);
        Directory.CreateDirectory(root);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# UI Art List");
        sb.AppendLine();
        sb.AppendLine("ขนาดเป็น px ที่ความละเอียดอ้างอิง 1920x1080 / Sliced = ทำเป็น 9-slice (ตั้ง Border ใน Sprite Editor) / Filled = หลอด/วง cooldown (ต้องมี sprite) / Template = ต้นแบบที่ถูก clone ตอนเล่น");
        sb.AppendLine("PNG ในโฟลเดอร์แต่ละหน้าต่าง = กรอบเปล่าขนาดจริง ใช้เป็นพื้นวาด แล้วลากรูปใส่ Source Image ของ object ตาม path");
        foreach (string prefabPath in ArtExportPrefabs)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) continue;
            sb.AppendLine();
            sb.AppendLine($"# {prefab.name}.prefab");
            string prefabDir = prefabPath == PrefabPath ? root : Path.Combine(root, prefab.name);
            ExportPrefabArt(prefab, prefabDir, sb);
        }
        ExportRaceArt(root, sb);

        string md = Path.Combine(root, "UI-Art-List.md");
        File.WriteAllText(md, sb.ToString());
        Debug.Log($"[{nameof(GameUITemplateBuilder)}] export art list -> {root}");
        return md;
    }

    private static void ExportPrefabArt(GameObject prefab, string prefabDir, System.Text.StringBuilder sb)
    {
        var rootRect = (RectTransform)prefab.transform;
        foreach (Transform window in prefab.transform)
        {
            sb.AppendLine();
            sb.AppendLine($"## {window.name}");
            sb.AppendLine();
            sb.AppendLine("| Path | Size (px) | Type | Color | Note |");
            sb.AppendLine("|---|---|---|---|---|");
            string dir = Path.Combine(prefabDir, window.name);
            Directory.CreateDirectory(dir);
            foreach (var g in window.GetComponentsInChildren<Graphic>(true))
            {
                if (g is TMP_Text) continue;
                Vector2 size = ComputeSize((RectTransform)g.transform, rootRect);
                string type = g is Image img ? (img.type == Image.Type.Filled ? $"Filled ({img.fillMethod})" : img.type.ToString()) : "RawImage (runtime texture)";
                string rel = AnimationUtility.CalculateTransformPath(g.transform, prefab.transform);
                string note = rel.Contains("Template") ? "Template" : "";
                if (g is Image i2 && i2.sprite == null) note += (note.Length > 0 ? ", " : "") + "no sprite yet";
                sb.AppendLine($"| `{rel}` | {Mathf.RoundToInt(size.x)} x {Mathf.RoundToInt(size.y)} | {type} | #{ColorUtility.ToHtmlStringRGBA(g.color)} | {note} |");
                if (g is Image && size.x >= 2f && size.y >= 2f) WritePlaceholder(Path.Combine(dir, $"{rel.Replace('/', '_')}_{Mathf.RoundToInt(size.x)}x{Mathf.RoundToInt(size.y)}.png"), size);
            }
        }
    }

    // ขนาดตอน layout จริงโดยประมาณ: root = 1920x1080, ลูก = ขนาดพ่อ × (anchorMax - anchorMin) + sizeDelta (แถวใน layout ใช้ LayoutElement)
    private static Vector2 ComputeSize(RectTransform rt, RectTransform root)
    {
        if (rt == root) return new Vector2(1920f, 1080f);
        var parent = rt.parent as RectTransform;
        Vector2 parentSize = parent != null ? ComputeSize(parent, root) : new Vector2(1920f, 1080f);
        Vector2 size = Vector2.Scale(parentSize, rt.anchorMax - rt.anchorMin) + rt.sizeDelta;
        var le = rt.GetComponent<LayoutElement>();
        if (parent != null && parent.GetComponent<HorizontalOrVerticalLayoutGroup>() != null)
        {
            if (parent.GetComponent<VerticalLayoutGroup>() != null) size.x = parentSize.x;
            if (le != null && le.preferredHeight > 0f) size.y = le.preferredHeight;
            if (le != null && le.preferredWidth > 0f) size.x = le.preferredWidth;
        }
        return new Vector2(Mathf.Max(0f, size.x), Mathf.Max(0f, size.y));
    }

    // PNG โปร่งใส + ขอบ 1px (กรอบให้วาด)
    private static void WritePlaceholder(string file, Vector2 size)
    {
        int w = Mathf.Clamp(Mathf.RoundToInt(size.x), 2, 2048), h = Mathf.Clamp(Mathf.RoundToInt(size.y), 2, 2048);
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var px = new Color32[w * h];
        var edge = new Color32(255, 0, 255, 255);
        var fill = new Color32(128, 128, 128, 40);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = x == 0 || y == 0 || x == w - 1 || y == h - 1 ? edge : fill;
        tex.SetPixels32(px);
        File.WriteAllBytes(file, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }

    // ---------- ชิ้นส่วนร่วม ----------

    // กรอบหน้าต่าง: พื้น + หัว (ชื่อ) + ปุ่ม X + เส้นคั่น
    private static void WindowFrame(RectTransform win, string titleText, out Button close, out TextMeshProUGUI title)
    {
        Img(win, _uiSprite, PanelColor, Image.Type.Sliced); // รับ raycast กันคลิกทะลุไปตีศัตรู
        var border = Stretch("Border", win, Vector2.zero, Vector2.zero);
        Img(border, _uiSprite, FrameColor, Image.Type.Sliced, raycast: false).fillCenter = false;

        var header = Rect("Header", win, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 56f));
        var t = Stretch("Title", header, new Vector2(18f, 0f), new Vector2(-64f, 0f));
        title = Text(t, titleText, 24f, TextAlignmentOptions.MidlineLeft, Gold, bold: true);

        var x = Rect("Close", win, Vector2.one, Vector2.one, Vector2.one, new Vector2(-6f, -6f), new Vector2(44f, 44f));
        var img = Img(x, _uiSprite, new Color(1f, 1f, 1f, 0.05f), Image.Type.Sliced);
        close = x.gameObject.AddComponent<Button>();
        close.targetGraphic = img;
        var xt = Stretch("X", x, Vector2.zero, Vector2.zero);
        Text(xt, "X", 20f, TextAlignmentOptions.Center, new Color(0.8f, 0.8f, 0.8f));

        var line = Rect("Line", win, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -56f), new Vector2(-24f, 1f));
        Img(line, null, FrameColor, Image.Type.Simple, raycast: false);
    }

    // หลอด: Background / Trail / Fill / Frame / Label (pos/size จากมุมบนซ้ายของ parent)
    private static UIBar Bar(string name, RectTransform parent, Vector2 pos, Vector2 size, Color color, float fontSize)
    {
        var r = Rect(name, parent, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), pos, size);
        var bar = r.gameObject.AddComponent<UIBar>();
        Img(r, _background, new Color(0.05f, 0.05f, 0.05f, 0.9f), Image.Type.Sliced, raycast: false);

        var trail = Stretch("Trail", r, new Vector2(2f, 2f), new Vector2(-2f, -2f));
        bar.trail = FilledHorizontal(Img(trail, _background, new Color(1f, 0.95f, 0.8f, 0.55f), Image.Type.Filled, raycast: false));
        var fill = Stretch("Fill", r, new Vector2(2f, 2f), new Vector2(-2f, -2f));
        bar.fill = FilledHorizontal(Img(fill, _background, color, Image.Type.Filled, raycast: false));
        var frame = Stretch("Frame", r, Vector2.zero, Vector2.zero);
        Img(frame, _uiSprite, FrameColor, Image.Type.Sliced, raycast: false).fillCenter = false;
        var label = Stretch("Label", r, Vector2.zero, Vector2.zero);
        bar.label = Text(label, "", fontSize, TextAlignmentOptions.Center, Color.white);
        return bar;
    }

    private static Image FilledHorizontal(Image img)
    {
        img.fillMethod = Image.FillMethod.Horizontal;
        img.fillOrigin = (int)Image.OriginHorizontal.Left;
        img.fillAmount = 1f;
        return img;
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        var obj = new GameObject(name, typeof(RectTransform));
        obj.layer = LayerMask.NameToLayer("UI");
        var rt = (RectTransform)obj.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    // เต็ม parent เว้นขอบ (offsetMin = ซ้ายล่าง, offsetMax = ขวาบน)
    private static RectTransform Stretch(string name, Transform parent, Vector2 offsetMin, Vector2 offsetMax)
    {
        var rt = Rect(name, parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
        return rt;
    }

    private static Image Img(RectTransform rt, Sprite sprite, Color color, Image.Type type, bool raycast = true)
    {
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.type = sprite != null ? type : Image.Type.Simple;
        img.raycastTarget = raycast;
        return img;
    }

    private static TextMeshProUGUI Text(RectTransform rt, string text, float size, TextAlignmentOptions align, Color color, bool bold = false)
    {
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = size;
        t.alignment = align;
        t.color = color;
        t.raycastTarget = false;
        if (bold) t.fontStyle = FontStyles.Bold;
        return t;
    }
}
#endif

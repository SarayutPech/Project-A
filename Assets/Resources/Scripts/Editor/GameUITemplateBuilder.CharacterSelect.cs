#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// หน้าเลือก/สร้างตัวละคร: prefab CharacterSelectUI (Canvas แยกจาก GameUI เพราะไม่มี player) + scene CharacterSelect (scene แรกของ build)
// เมนู: Tools > Project-A > Build Character Select UI   = สร้าง/เขียนทับ prefab (แก้ art ใน prefab ต่อเอง แล้วอย่ารันซ้ำ)
//       Tools > Project-A > Build Character Select Scene = สร้าง config + race ตัวอย่าง (ถ้ายังไม่มี) + scene + ใส่เป็น scene แรกใน Build Profiles
public static partial class GameUITemplateBuilder
{
    private const string CharacterSelectPrefabPath = Folder + "/CharacterSelectUI.prefab";
    private const string CharacterSelectScenePath = "Assets/Scenes/CharacterSelect.unity";
    private const string CharacterConfigFolder = "Assets/Resources/Gameobject/ScriptAbleObject/Characters";
    private const string CharacterConfigPath = CharacterConfigFolder + "/CharacterCreationConfig.asset";
    private const string EdgeFadePath = Folder + "/Art/EdgeFade.png";

    private static readonly Color ScreenColor = new Color(0.035f, 0.03f, 0.028f, 1f);

    [MenuItem("Tools/Project-A/Build Character Select UI")]
    private static void BuildCharacterSelectMenu()
    {
        if (File.Exists(CharacterSelectPrefabPath) &&
            !EditorUtility.DisplayDialog("Build Character Select UI", $"{CharacterSelectPrefabPath} มีอยู่แล้ว เขียนทับ? (ที่แก้ไว้ใน prefab จะหาย)", "เขียนทับ", "ยกเลิก"))
            return;
        BuildCharacterSelectUI();
    }

    [MenuItem("Tools/Project-A/Build Character Select Scene")]
    private static void BuildCharacterSelectSceneMenu()
    {
        if (File.Exists(CharacterSelectScenePath) &&
            !EditorUtility.DisplayDialog("Build Character Select Scene", $"{CharacterSelectScenePath} มีอยู่แล้ว เขียนทับ?", "เขียนทับ", "ยกเลิก"))
            return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        BuildCharacterSelectScene();
    }

    public static void BuildCharacterSelectScene()
    {
        EnsureCharacterConfig();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterSelectPrefabPath);
        if (prefab == null) prefab = BuildCharacterSelectUI();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camObj = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)) { tag = "MainCamera" };
        var cam = camObj.GetComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        new GameObject(nameof(EventSystem), typeof(EventSystem), typeof(InputSystemUIInputModule));
        PrefabUtility.InstantiatePrefab(prefab, scene);
        EditorSceneManager.SaveScene(scene, CharacterSelectScenePath);

        // scene แรกของ build (เปิดเกม = หน้าเลือกตัวละคร)
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        scenes.RemoveAll(s => s.path == CharacterSelectScenePath);
        scenes.Insert(0, new EditorBuildSettingsScene(CharacterSelectScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
        Debug.Log($"[{nameof(GameUITemplateBuilder)}] สร้าง {CharacterSelectScenePath} แล้ว (scene แรกใน Build Profiles)");
    }

    // config + race ตัวอย่าง 5 ตัว (จุด start วนตาม node start ที่มีใน passive tree) สร้างเฉพาะตอนยังไม่มี
    public static CharacterCreationConfig EnsureCharacterConfig()
    {
        var config = AssetDatabase.LoadAssetAtPath<CharacterCreationConfig>(CharacterConfigPath);
        if (config != null) return config;

        Directory.CreateDirectory(CharacterConfigFolder + "/Races");
        var starts = new List<string>();
        var tree = Resources.Load<PassiveTree>(PassiveTree.DefaultResourcePath);
        if (tree != null) foreach (var n in tree.StartNodes) starts.Add(n.id);

        config = ScriptableObject.CreateInstance<CharacterCreationConfig>();
        var samples = new (string id, string name, string desc, Color color)[]
        {
            ("human", "Human", "Adaptable survivors of the old kingdoms. Balanced in body and mind.", new Color(0.93f, 0.85f, 0.66f)),
            ("elf", "Elf", "Swift and precise. Masters of the bow and the blade's edge.", new Color(0.55f, 0.9f, 0.6f)),
            ("dwarf", "Dwarf", "Stout and unyielding. Their strength is carved from stone.", new Color(0.95f, 0.6f, 0.35f)),
            ("orc", "Orc", "Born for war. Raw power that breaks any shield.", new Color(0.85f, 0.3f, 0.25f)),
            ("undead", "Undead", "Bound by forbidden magic. Their minds burn colder than death.", new Color(0.55f, 0.6f, 0.95f)),
        };
        for (int i = 0; i < samples.Length; i++)
        {
            var race = ScriptableObject.CreateInstance<CharacterRace>();
            race.id = samples[i].id;
            race.displayName = samples[i].name;
            race.description = samples[i].desc;
            race.accentColor = samples[i].color;
            race.passiveStartNode = starts.Count > 0 ? starts[i % starts.Count] : "";
            AssetDatabase.CreateAsset(race, $"{CharacterConfigFolder}/Races/{samples[i].name}.asset");
            config.races.Add(race);
        }
        AssetDatabase.CreateAsset(config, CharacterConfigPath);
        AssetDatabase.SaveAssets();
        return config;
    }

    public static GameObject BuildCharacterSelectUI()
    {
        LoadSprites();
        var edgeFade = EnsureEdgeFadeSprite();
        var root = new GameObject("CharacterSelectUI", typeof(RectTransform));
        try
        {
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            root.AddComponent<GraphicRaycaster>();
            var screen = root.AddComponent<CharacterSelectScreen>();

            var bg = Stretch("Background", root.transform, Vector2.zero, Vector2.zero);
            screen.background = Img(bg, null, ScreenColor, Image.Type.Simple, raycast: false);
            screen.background.preserveAspect = false;

            BuildSelectPanel(root.transform, screen);
            BuildCreatePanel(root.transform, screen, edgeFade);

            foreach (var s in root.GetComponentsInChildren<Selectable>(true)) s.navigation = new Navigation { mode = Navigation.Mode.None };

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, CharacterSelectPrefabPath);
            ExportArtList();
            Debug.Log($"[{nameof(GameUITemplateBuilder)}] สร้าง {CharacterSelectPrefabPath} แล้ว", prefab);
            return prefab;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    // ---------- หน้าเลือกตัวละคร: รายการ slot ด้านขวา + Play / Delete ----------

    private static void BuildSelectPanel(Transform parent, CharacterSelectScreen screen)
    {
        var panel = Stretch("SelectPanel", parent, Vector2.zero, Vector2.zero);
        screen.selectPanel = panel.gameObject;
        ScreenTitle(panel, "Select Character");

        var list = Rect("SlotPanel", panel, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-120f, 40f), new Vector2(600f, 720f));
        Img(list, _uiSprite, PanelColor, Image.Type.Sliced);
        var border = Stretch("Border", list, Vector2.zero, Vector2.zero);
        Img(border, _uiSprite, FrameColor, Image.Type.Sliced, raycast: false).fillCenter = false;
        var count = Rect("Count", list, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(-40f, 44f));
        screen.slotCountText = Text(count, "Characters 0 / 6", 20f, TextAlignmentOptions.MidlineLeft, Gold, bold: true);

        ScrollList("List", list, new Vector2(12f, 12f), new Vector2(-12f, -58f), out var content);
        content.GetComponent<VerticalLayoutGroup>().spacing = 6f;
        screen.slotListContent = content;
        screen.slotTemplate = SlotRow(content);

        screen.playButton = TextButton(panel, "Play", new Vector2(1f, 0f), new Vector2(-120f, 70f), new Vector2(380f, 72f), pivotX: 1f);
        screen.playButton.GetComponentInChildren<TextMeshProUGUI>().fontSize = 28f;
        screen.deleteButton = TextButton(panel, "Delete", new Vector2(1f, 0f), new Vector2(-516f, 70f), new Vector2(204f, 72f), pivotX: 1f);
        screen.deleteButton.targetGraphic.color = new Color(0.55f, 0.15f, 0.12f, 0.6f);
        screen.deleteLabel = screen.deleteButton.GetComponentInChildren<TextMeshProUGUI>();
        var hint = Rect("Hint", panel, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-120f, 30f), new Vector2(600f, 26f));
        Text(hint, "Double-click a character to play", 14f, TextAlignmentOptions.Center, new Color(0.55f, 0.55f, 0.55f));
    }

    // แถว slot: [รูป race] ชื่อ / Level x Race  หรือ  "+ Create New Character"
    private static CharacterSlotView SlotRow(RectTransform content)
    {
        var row = Rect("SlotTemplate", content, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        var v = row.gameObject.AddComponent<CharacterSlotView>();
        Img(row, _uiSprite, SlotColor, Image.Type.Sliced); // รับคลิก
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = 96f;

        var sel = Stretch("Selected", row, Vector2.zero, Vector2.zero);
        Img(sel, _uiSprite, Gold, Image.Type.Sliced, raycast: false).fillCenter = false;
        v.selectedState = sel.gameObject;

        var filled = Stretch("Filled", row, Vector2.zero, Vector2.zero);
        v.filledState = filled.gameObject;
        var portrait = Rect("Portrait", filled, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(76f, 76f));
        v.portrait = Img(portrait, null, Color.white, Image.Type.Simple, raycast: false);
        v.portrait.preserveAspect = true;
        var name = Rect("Name", filled, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 0.5f), new Vector2(102f, 15f), new Vector2(-120f, 36f));
        v.nameText = Text(name, "Character", 24f, TextAlignmentOptions.MidlineLeft, Gold, bold: true);
        v.nameText.textWrappingMode = TextWrappingModes.NoWrap;
        v.nameText.overflowMode = TextOverflowModes.Ellipsis;
        var detail = Rect("Detail", filled, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 0.5f), new Vector2(102f, -18f), new Vector2(-120f, 28f));
        v.detailText = Text(detail, "Level 1  Human", 17f, TextAlignmentOptions.MidlineLeft, new Color(0.75f, 0.72f, 0.65f));

        var empty = Stretch("Empty", row, Vector2.zero, Vector2.zero);
        v.emptyState = empty.gameObject;
        Text(empty, "+  Create New Character", 20f, TextAlignmentOptions.Center, new Color(0.6f, 0.58f, 0.52f));
        empty.gameObject.SetActive(false);
        return v;
    }

    // ---------- หน้าสร้างตัวละคร: carousel race + ชื่อ/คำอธิบาย + ช่องชื่อ + Create ----------

    private static void BuildCreatePanel(Transform parent, CharacterSelectScreen screen, Sprite edgeFade)
    {
        var panel = Stretch("CreatePanel", parent, Vector2.zero, Vector2.zero);
        screen.createPanel = panel.gameObject;

        // การ์ดกลาง 380x520 กึ่งกลางที่ y +110 / ข้อมูลอยู่ใต้การ์ดกลาง
        var area = Stretch("Carousel", panel, Vector2.zero, Vector2.zero);
        var carousel = area.gameObject.AddComponent<RaceCarousel>();
        screen.carousel = carousel;
        carousel.cardLayer = Rect("Cards", area, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 110f), Vector2.zero);
        carousel.cardTemplate = RaceCard(area);

        // ไล่สีพื้นหลังทับการ์ดริม = จางหายไปทางขอบจอ (ใส่รูป gradient ของตัวเองได้)
        var left = Rect("EdgeFadeLeft", panel, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), new Vector2(240f, 0f), new Vector2(480f, 0f));
        Img(left, edgeFade, ScreenColor, Image.Type.Simple, raycast: false);
        var right = Rect("EdgeFadeRight", panel, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(-240f, 0f), new Vector2(480f, 0f));
        Img(right, edgeFade, ScreenColor, Image.Type.Simple, raycast: false);
        right.localScale = new Vector3(-1f, 1f, 1f);

        ScreenTitle(panel, "Create Character");

        var center = new Vector2(0.5f, 0.5f);
        var raceName = Rect("RaceName", panel, center, center, center, new Vector2(0f, -185f), new Vector2(460f, 50f));
        screen.raceNameText = Text(raceName, "Race", 34f, TextAlignmentOptions.Center, Gold, bold: true);
        carousel.prevButton = TextButton(panel, "Prev", center, new Vector2(-290f, -185f), new Vector2(64f, 56f));
        carousel.prevButton.GetComponentInChildren<TextMeshProUGUI>().text = "<";
        carousel.nextButton = TextButton(panel, "Next", center, new Vector2(290f, -185f), new Vector2(64f, 56f));
        carousel.nextButton.GetComponentInChildren<TextMeshProUGUI>().text = ">";

        var desc = Rect("RaceDescription", panel, center, center, center, new Vector2(0f, -245f), new Vector2(760f, 64f));
        screen.raceDescriptionText = Text(desc, "Description", 17f, TextAlignmentOptions.Top, new Color(0.82f, 0.8f, 0.75f));

        screen.nameField = NameField(panel, new Vector2(0f, -318f), new Vector2(400f, 50f));
        screen.createButton = TextButton(panel, "Create", center, new Vector2(0f, -390f), new Vector2(260f, 60f));
        screen.createButton.GetComponentInChildren<TextMeshProUGUI>().fontSize = 22f;
        var error = Rect("Error", panel, center, center, center, new Vector2(0f, -442f), new Vector2(700f, 30f));
        screen.errorText = Text(error, "", 16f, TextAlignmentOptions.Center, new Color(1f, 0.45f, 0.4f));

        screen.backButton = TextButton(panel, "Back", new Vector2(0f, 1f), new Vector2(40f, -40f), new Vector2(150f, 50f), pivotX: 0f);
        panel.gameObject.SetActive(false);
    }

    // การ์ด race: [แสงเลือก (สี accent)] [พื้น] [portrait รับคลิก] [กรอบ] [ชื่อ]
    private static RaceCardView RaceCard(RectTransform parent)
    {
        var r = Rect("CardTemplate", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 110f), new Vector2(380f, 520f));
        var v = r.gameObject.AddComponent<RaceCardView>();
        v.group = r.gameObject.AddComponent<CanvasGroup>();

        var glow = Stretch("Selected", r, new Vector2(-10f, -10f), new Vector2(10f, 10f));
        v.accentGraphic = Img(glow, _uiSprite, new Color(Gold.r, Gold.g, Gold.b, 0.9f), Image.Type.Sliced, raycast: false);
        v.selectedState = glow.gameObject;
        var back = Stretch("Back", r, Vector2.zero, Vector2.zero);
        Img(back, _uiSprite, PanelColor, Image.Type.Sliced, raycast: false);
        var portrait = Stretch("Portrait", r, new Vector2(8f, 8f), new Vector2(-8f, -8f));
        v.portrait = Img(portrait, null, new Color(0.35f, 0.33f, 0.3f), Image.Type.Simple); // รับคลิก
        var border = Stretch("Border", r, Vector2.zero, Vector2.zero);
        Img(border, _uiSprite, FrameColor, Image.Type.Sliced, raycast: false).fillCenter = false;
        var name = Rect("Name", r, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(-20f, 44f));
        v.nameText = Text(name, "Race", 26f, TextAlignmentOptions.Center, Color.white, bold: true);
        return v;
    }

    private static TMP_InputField NameField(RectTransform parent, Vector2 pos, Vector2 size)
    {
        var center = new Vector2(0.5f, 0.5f);
        var r = Rect("NameField", parent, center, center, center, pos, size);
        var bg = Img(r, _uiSprite, new Color(0f, 0f, 0f, 0.6f), Image.Type.Sliced);
        var border = Stretch("Border", r, Vector2.zero, Vector2.zero);
        Img(border, _uiSprite, FrameColor, Image.Type.Sliced, raycast: false).fillCenter = false;
        var area = Stretch("Text Area", r, new Vector2(14f, 2f), new Vector2(-14f, -2f));
        area.gameObject.AddComponent<RectMask2D>();
        var ph = Stretch("Placeholder", area, Vector2.zero, Vector2.zero);
        var placeholder = Text(ph, "Character name", 20f, TextAlignmentOptions.Center, new Color(1f, 1f, 1f, 0.3f));
        placeholder.fontStyle = FontStyles.Italic;
        var tx = Stretch("Text", area, Vector2.zero, Vector2.zero);
        var text = Text(tx, "", 20f, TextAlignmentOptions.Center, Color.white);

        var field = r.gameObject.AddComponent<TMP_InputField>();
        field.targetGraphic = bg;
        field.textViewport = area;
        field.textComponent = text;
        field.placeholder = placeholder;
        field.lineType = TMP_InputField.LineType.SingleLine;
        field.pointSize = 20f;
        field.characterLimit = 20;
        return field;
    }

    private static void ScreenTitle(RectTransform panel, string title)
    {
        var t = Rect("Title", panel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -36f), new Vector2(800f, 60f));
        Text(t, title, 40f, TextAlignmentOptions.Center, Gold, bold: true);
    }

    // gradient แนวนอน ทึบซ้าย -> ใสขวา (สีขาว ย้อมด้วย Image.color)
    private static Sprite EnsureEdgeFadeSprite()
    {
        if (!File.Exists(EdgeFadePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(EdgeFadePath));
            const int w = 256, h = 4;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px = new Color32[w * h];
            for (int x = 0; x < w; x++)
            {
                float t = 1f - x / (w - 1f);
                var c = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.SmoothStep(0f, 1f, t) * 255f));
                for (int y = 0; y < h; y++) px[y * w + x] = c;
            }
            tex.SetPixels32(px);
            File.WriteAllBytes(EdgeFadePath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(EdgeFadePath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(EdgeFadePath);
            importer.textureType = TextureImporterType.Sprite;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(EdgeFadePath);
    }

    // ---------- Export: รูปของแต่ละ race (อยู่ใน CharacterRace asset ไม่ใช่ prefab) ----------

    private static void ExportRaceArt(string root, System.Text.StringBuilder sb)
    {
        var config = AssetDatabase.LoadAssetAtPath<CharacterCreationConfig>(CharacterConfigPath);
        if (config == null) return;

        Vector2 portraitSize = new Vector2(364f, 504f);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterSelectPrefabPath);
        var card = prefab != null ? prefab.GetComponentInChildren<RaceCardView>(true) : null;
        if (card != null && card.portrait != null) portraitSize = ComputeSize((RectTransform)card.portrait.transform, (RectTransform)prefab.transform);
        Vector2 bgSize = new Vector2(1920f, 1080f);

        sb.AppendLine();
        sb.AppendLine("# Races (CharacterRace asset)");
        sb.AppendLine();
        sb.AppendLine($"ใส่รูปที่ช่อง Portrait / Background ของ asset ใน `{CharacterConfigFolder}/Races` (เพิ่ม race = สร้าง asset ใหม่แล้วใส่ใน CharacterCreationConfig.races)");
        sb.AppendLine("Portrait = การ์ดกลาง carousel (การ์ดข้างใช้รูปเดียวกันย่อลง) / Background = พื้นหลังทั้งจอตอนเลือก race นั้น (ไม่ใส่ก็ได้)");
        sb.AppendLine();
        sb.AppendLine("| Race | Asset | Portrait (px) | Background (px) | Note |");
        sb.AppendLine("|---|---|---|---|---|");
        string dir = Path.Combine(root, "Races");
        Directory.CreateDirectory(dir);
        string ps = $"{Mathf.RoundToInt(portraitSize.x)} x {Mathf.RoundToInt(portraitSize.y)}";
        foreach (var race in config.races)
        {
            if (race == null) continue;
            var missing = new List<string>();
            if (race.portrait == null) missing.Add("no portrait yet");
            if (race.background == null) missing.Add("no background (optional)");
            sb.AppendLine($"| {race.DisplayName} | `{AssetDatabase.GetAssetPath(race)}` | {ps} | 1920 x 1080 | {string.Join(", ", missing)} |");
            WritePlaceholder(Path.Combine(dir, $"{race.Id}_Portrait_{Mathf.RoundToInt(portraitSize.x)}x{Mathf.RoundToInt(portraitSize.y)}.png"), portraitSize);
            WritePlaceholder(Path.Combine(dir, $"{race.Id}_Background_1920x1080.png"), bgSize);
        }
    }
}
#endif

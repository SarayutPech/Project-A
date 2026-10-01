#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// เมนู Esc (PauseMenu) + หน้าตั้งปุ่ม (KeybindWindow) ใน GameUI prefab
// GameUI.prefab ที่แก้ art ไว้แล้ว: Tools > Project-A > Add Pause Menu To GameUI = เติมเฉพาะ 2 หน้าต่างนี้ (มีแล้วข้าม) ไม่ทับของเดิม
public static partial class GameUITemplateBuilder
{
    [MenuItem("Tools/Project-A/Add Pause Menu To GameUI")]
    public static void AddPauseMenuToGameUI()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
        {
            EditorUtility.DisplayDialog("Add Pause Menu", "ยังไม่มี GameUI prefab (Build UI Template ก่อน)", "OK");
            return;
        }
        LoadSprites();
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            // วางก่อน Toast (Toast / ConfirmDialog / Tooltip ต้องอยู่บนสุด)
            var toast = root.transform.Find("Toast");
            int insertAt = toast != null ? toast.GetSiblingIndex() : root.transform.childCount;
            bool changed = false;
            if (root.GetComponentInChildren<PauseMenu>(true) == null)
            {
                BuildPauseMenu(root.transform).SetSiblingIndex(insertAt++);
                changed = true;
            }
            if (root.GetComponentInChildren<KeybindWindow>(true) == null)
            {
                BuildKeybindWindow(root.transform).SetSiblingIndex(insertAt);
                changed = true;
            }
            if (!changed)
            {
                Debug.Log($"[{nameof(GameUITemplateBuilder)}] GameUI มี PauseMenu / KeybindWindow อยู่แล้ว");
                return;
            }
            foreach (var s in root.GetComponentsInChildren<Selectable>(true)) s.navigation = new Navigation { mode = Navigation.Mode.None };
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log($"[{nameof(GameUITemplateBuilder)}] เพิ่ม PauseMenu / KeybindWindow ลง {PrefabPath} แล้ว");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        ExportArtList();
    }

    // ---------- Pause (Esc): พื้นมืดทั้งจอ + กล่องปุ่มกลางจอ ----------

    private static Transform BuildPauseMenu(Transform parent)
    {
        var root = Stretch("PauseMenu", parent, Vector2.zero, Vector2.zero);
        var view = root.gameObject.AddComponent<PauseMenu>();
        Img(root, null, new Color(0f, 0f, 0f, 0.6f), Image.Type.Simple); // บล็อกคลิกทั้งจอ

        var center = new Vector2(0.5f, 0.5f);
        var panel = Rect("Panel", root, center, center, center, new Vector2(0f, 20f), new Vector2(420f, 400f));
        WindowFrame(panel, "Paused", out view.closeButton, out _);

        view.resumeButton = MenuButton(panel, "Resume", "ResumeButton", -84f);
        view.controlsButton = MenuButton(panel, "Controls", "ControlsButton", -158f);
        view.hideoutButton = MenuButton(panel, "Return to Hideout", "HideoutButton", -232f);
        view.characterSelectButton = MenuButton(panel, "Save & Character Select", "CharacterSelectButton", -306f);
        return root;

        static Button MenuButton(RectTransform panel, string label, string name, float y)
        {
            var b = TextButton(panel, label, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(340f, 58f));
            b.name = name;
            b.GetComponentInChildren<TextMeshProUGUI>().fontSize = 20f;
            return b;
        }
    }

    // ---------- Controls: รายการปุ่มแยกหัวข้อ + Reset ----------

    private static Transform BuildKeybindWindow(Transform parent)
    {
        var center = new Vector2(0.5f, 0.5f);
        var win = Rect("KeybindWindow", parent, center, center, center, new Vector2(0f, 10f), new Vector2(620f, 780f));
        var view = win.gameObject.AddComponent<KeybindWindow>();
        WindowFrame(win, "Controls", out view.closeButton, out _);
        view.resetButton = TextButton(win, "Reset", new Vector2(1f, 1f), new Vector2(-56f, -8f), new Vector2(110f, 40f));

        ScrollList("List", win, new Vector2(12f, 40f), new Vector2(-12f, -64f), out var content);
        view.listContent = content;

        // หัวข้อ (Movement / Skills / Interface)
        var header = Rect("HeaderTemplate", content, Vector2.zero, Vector2.zero, center, Vector2.zero, Vector2.zero);
        view.headerTemplate = Text(header, "Category", 20f, TextAlignmentOptions.BottomLeft, Gold, bold: true);
        view.headerTemplate.margin = new Vector4(8f, 0f, 0f, 4f);
        header.gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;

        view.rowTemplate = KeybindRow(content);

        var hint = Rect("Hint", win, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(-32f, 22f));
        Text(hint, "Click a key to change it  ·  Esc: cancel  ·  Red = used twice", 13f, TextAlignmentOptions.Center, new Color(0.55f, 0.55f, 0.55f));
        return win;
    }

    // แถว: [แถบแดง = ชน] ชื่อ action ............ [ ปุ่ม ]
    private static KeybindRowView KeybindRow(RectTransform content)
    {
        var row = Rect("RowTemplate", content, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        var v = row.gameObject.AddComponent<KeybindRowView>();
        Img(row, _uiSprite, new Color(0f, 0f, 0f, 0.35f), Image.Type.Sliced, raycast: false);
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = 46f;

        var conflict = Rect("Conflict", row, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(4f, 0f));
        Img(conflict, null, new Color(1f, 0.35f, 0.3f), Image.Type.Simple, raycast: false);
        v.conflictState = conflict.gameObject;
        conflict.gameObject.SetActive(false);

        var label = Rect("Label", row, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(-240f, 0f));
        v.labelText = Text(label, "Action", 17f, TextAlignmentOptions.MidlineLeft, Color.white);

        var key = Rect("KeyButton", row, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-8f, 0f), new Vector2(200f, 36f));
        var keyImg = Img(key, _uiSprite, new Color(Gold.r, Gold.g, Gold.b, 0.18f), Image.Type.Sliced);
        v.keyButton = key.gameObject.AddComponent<Button>();
        v.keyButton.targetGraphic = keyImg;
        var kt = Stretch("Key", key, new Vector2(6f, 0f), new Vector2(-6f, 0f));
        v.keyText = Text(kt, "Key", 17f, TextAlignmentOptions.Center, Color.white, bold: true);
        return v;
    }
}
#endif

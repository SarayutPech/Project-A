using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

// root ของ UI ในเกมทั้งหมด (Canvas เดียว) ตัวเดียวต่อเครื่อง อยู่ข้าม scene (DontDestroyOnLoad) เหมือน player
// LocalPlayerUI (บน player) สร้างจาก prefab แล้วเรียก Bind(player) -> ส่งต่อให้ทุกส่วนที่เป็น IPlayerUI
// prefab ต้นแบบสร้างจากเมนู Tools > Project-A > Build UI Template แล้วแก้ texture/ตำแหน่งใน prefab ได้เลย
// ปุ่ม: I = กระเป๋า, C = Character Sheet, K = Skill Gems, P = Passive Tree, Tab = แผนที่เต็มจอ (overlay), Esc = ปิดหน้าต่างทั้งหมด (rebind ได้ใน Inspector)
// โหลด scene ใหม่ = ปิดหน้าต่างทั้งหมด (HUD / minimap ยังแสดงตามปกติ)
public class GameUI : UISingleton<GameUI>
{
    public const string ResourcePath = "Gameobject/Prefab/UI/GameUI";

    public InputAction toggleInventory = new InputAction("Toggle Inventory", InputActionType.Button, "<Keyboard>/i");
    public InputAction toggleCharacter = new InputAction("Toggle Character", InputActionType.Button, "<Keyboard>/c");
    public InputAction toggleSkillGems = new InputAction("Toggle Skill Gems", InputActionType.Button, "<Keyboard>/k");
    public InputAction togglePassives = new InputAction("Toggle Passive Tree", InputActionType.Button, "<Keyboard>/p");
    public InputAction toggleMap = new InputAction("Toggle Map Overlay", InputActionType.Button, "<Keyboard>/tab");
    public InputAction closeWindows = new InputAction("Close Windows", InputActionType.Button, "<Keyboard>/escape");

    public GameObject BoundPlayer { get; private set; }

    // ไว้เรียกตอนโหลด scene (เช่น minimap ถ่ายแผนที่ใหม่)
    public static event System.Action SceneChanged;

    protected override void Awake()
    {
        base.Awake();
        if (!IsInstance) return;
        if (transform.parent != null) transform.SetParent(null, false);
        DontDestroyOnLoad(gameObject);
        EnsureEventSystem();
    }

    private void OnEnable()
    {
        toggleInventory.Enable();
        toggleCharacter.Enable();
        toggleSkillGems.Enable();
        toggleMap.Enable();
        togglePassives.Enable();
        closeWindows.Enable();
        SceneManager.sceneLoaded += OnSceneLoaded;
        // Esc ไม่ให้ rebind (กันผู้เล่นเปิดเมนูไม่ได้)
        KeyBindings.Register(KeyBindings.Interface, toggleInventory, "Inventory");
        KeyBindings.Register(KeyBindings.Interface, toggleCharacter, "Character");
        KeyBindings.Register(KeyBindings.Interface, toggleSkillGems, "Skill Gems");
        KeyBindings.Register(KeyBindings.Interface, togglePassives, "Passive Tree");
        KeyBindings.Register(KeyBindings.Interface, toggleMap, "Map Overlay");
    }

    private void OnDisable()
    {
        KeyBindings.Unregister(toggleInventory);
        KeyBindings.Unregister(toggleCharacter);
        KeyBindings.Unregister(toggleSkillGems);
        KeyBindings.Unregister(togglePassives);
        KeyBindings.Unregister(toggleMap);
        toggleInventory.Disable();
        toggleCharacter.Disable();
        toggleSkillGems.Disable();
        toggleMap.Disable();
        togglePassives.Disable();
        closeWindows.Disable();
        SceneManager.sceneLoaded -= OnSceneLoaded;
        LocalInputGate.KeyboardCaptured = false;
    }

    protected override void OnDestroy()
    {
        if (IsInstance) Bind(null);
        base.OnDestroy();
    }

    public void Bind(GameObject player)
    {
        BoundPlayer = player;
        foreach (var ui in GetComponentsInChildren<IPlayerUI>(true)) ui.Bind(player);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single) return;
        UIWindows.CloseAll();
        SceneChanged?.Invoke();
    }

    private void Update()
    {
        // กำลังพิมพ์ในช่อง search -> ไม่ใช่ปุ่มลัด และบอก input ของเกมว่าคีย์บอร์ดถูก UI จับอยู่
        // คลิก slider/ปุ่มแล้วมันถูก "เลือก" ค้าง -> UI module เอา WASD/ลูกศรไปเลื่อนค่า/โฟกัส (บัค slider minimap)
        // เกมนี้ใช้เมาส์กับ UI อย่างเดียว: ปล่อยเมาส์แล้วยกเลิกการเลือก ยกเว้นช่องพิมพ์
        // EventSystem ของ scene ก่อน (เช่นหน้าเลือกตัวละคร) ยังอยู่ตอน Awake แล้วถูก unload ทีหลัง -> สร้างของตัวเองแทน
        if (EventSystem.current == null) EnsureEventSystem();
        ReleaseUiSelection();
        bool typing = IsTyping();
        LocalInputGate.KeyboardCaptured = typing;
        // Esc ที่ใช้ยกเลิกการตั้งปุ่ม (หน้า Controls) ไม่นับ
        bool rebindEsc = KeyBindings.IsRebinding || Time.frameCount - KeyBindings.LastRebindFrame <= 1;
        if (closeWindows.WasPressedThisFrame() && !rebindEsc)
        {
            if (typing) EventSystem.current.SetSelectedGameObject(null);
            else HandleEscape();
        }
        if (typing || GamePause.IsPaused) return;

        if (toggleInventory.WasPressedThisFrame() && InventoryWindow.Instance != null) InventoryWindow.Instance.Toggle();
        if (toggleCharacter.WasPressedThisFrame() && CharacterWindow.Instance != null) CharacterWindow.Instance.Toggle();
        if (toggleSkillGems.WasPressedThisFrame() && SkillGemWindow.Instance != null) SkillGemWindow.Instance.Toggle();
        if (toggleMap.WasPressedThisFrame() && MinimapView.Instance != null) MinimapView.Instance.ToggleOverlay();
        if (togglePassives.WasPressedThisFrame() && PassiveTreeWindow.Instance != null) PassiveTreeWindow.Instance.Toggle();
    }

    // Esc: ปิดชั้นบนสุดก่อน -> กล่องยืนยัน > Controls > Pause (= เล่นต่อ) > หน้าต่างอื่นทั้งหมด > ไม่มีอะไรเปิด = เปิด Pause
    private static void HandleEscape()
    {
        var pause = PauseMenu.Instance;
        if (ConfirmDialog.Instance != null && ConfirmDialog.Instance.IsOpen) ConfirmDialog.Instance.Close();
        else if (KeybindWindow.Instance != null && KeybindWindow.Instance.IsOpen) KeybindWindow.Instance.Close();
        else if (pause != null && pause.IsOpen) pause.Close();
        else if (UIWindows.AnyOpen) UIWindows.CloseAll();
        else if (pause != null && !SceneTransition.IsTransitioning) pause.Open();
    }

    private static void ReleaseUiSelection()
    {
        var es = EventSystem.current;
        var selected = es != null ? es.currentSelectedGameObject : null;
        if (selected == null || selected.GetComponent<TMP_InputField>() != null) return;
        if (Mouse.current != null && Mouse.current.leftButton.isPressed) return; // ยังลาก slider อยู่
        es.SetSelectedGameObject(null);
    }

    private static bool IsTyping()
    {
        var es = EventSystem.current;
        if (es == null || es.currentSelectedGameObject == null) return false;
        var field = es.currentSelectedGameObject.GetComponent<TMP_InputField>();
        return field != null && field.isFocused;
    }

    // ปุ่ม/hover ต้องมี EventSystem (ไว้ใต้ GameUI -> ข้าม scene ไปด้วย ไม่ซ้ำกับของ scene)
    private void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        var obj = new GameObject(nameof(EventSystem), typeof(EventSystem), typeof(InputSystemUIInputModule));
        obj.transform.SetParent(transform, false);
    }
}

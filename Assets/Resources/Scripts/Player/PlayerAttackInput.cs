using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// ปุ่มของ skill slot 1 ช่อง (index ตรงกับ PlayerSkills.slots)
[System.Serializable]
public class SkillSlotBinding
{
    [Tooltip("ปุ่มของ slot นี้ (กด + เพิ่ม binding / แก้ path ได้ หรือ rebind ตอนเล่นผ่าน action.PerformInteractiveRebinding())")]
    public InputAction action;

    public SkillSlotBinding() => action = new InputAction("Skill", InputActionType.Button);

    public SkillSlotBinding(string name, params string[] bindings)
    {
        action = new InputAction(name, InputActionType.Button);
        foreach (var b in bindings) action.AddBinding(b);
    }
}

// ตัวรับ input สกิลของ local player (ฝั่ง client) -> ส่งเป็น "คำขอใช้สกิลช่อง N + ทิศ" ให้ PlayerCombat ตัดสิน (กฎข้อ 1, 4)
// ปุ่ม 1 ชุด = Slot Bindings (ค่าเริ่มต้น 5 ปุ่ม) กด Alternate Set ค้าง (Ctrl) = ปุ่มเดิมแต่ใช้สกิลชุดที่ 2
//   ปุ่มที่ i -> PlayerSkills.slots[i] / ค้าง Ctrl -> slots[i + จำนวนปุ่ม] (5 ปุ่ม = ช่อง 0-4 และ 5-9)
// ค่าเริ่มต้น: คลิกซ้าย / คลิกขวา / Q / E / R (gamepad RT / LT / RB / LB / Y)
// ทิศ: เมาส์/คีย์บอร์ด = หันไปทางเคอร์เซอร์บนระนาบพื้นระดับเท้า / gamepad = ทางที่หันอยู่
// ภายหลังการตั้งปุ่มเป็น setting ของเครื่องผู้เล่น (เซฟ binding override ลงเครื่อง) ไม่ต้องส่งไป server
[RequireComponent(typeof(PlayerCombat))]
public class PlayerAttackInput : MonoBehaviour
{
    public const int DefaultSlotsPerSet = 5;

    public List<SkillSlotBinding> slotBindings = DefaultBindings();
    [Tooltip("กดค้าง = ใช้สกิลชุดที่ 2 (ปล่อย = กลับชุดแรก)")]
    public InputAction alternateSetAction = new InputAction("Alternate Skill Set", InputActionType.Button, "<Keyboard>/ctrl");

    [Tooltip("กดค้าง = ใช้ซ้ำเมื่อท่าเดิมจบ (สกิลที่เปิด Channel จะวนท่าเดิมไปเรื่อยๆ แทน)")]
    public bool holdToRepeat = true;
    [Tooltip("หันตีไปทางเคอร์เซอร์เมาส์ (ปิด = ตีทางที่หันอยู่)")]
    public bool aimAtCursor = true;
    [Tooltip("ถ้าเว้นว่างจะใช้ Camera.main")]
    public Camera aimCamera;
    [Tooltip("คลิกโดน UI หรือคลิก object ที่ interact ได้ในระยะ (portal, กล่องของดรอป) ไม่นับเป็นการใช้สกิล")]
    public bool ignoreUiAndPortalClicks = true;

    private PlayerCombat _combat;


    private static List<SkillSlotBinding> DefaultBindings() => new List<SkillSlotBinding>
    {
        new SkillSlotBinding("Skill 1", "<Mouse>/leftButton", "<Gamepad>/rightTrigger"),
        new SkillSlotBinding("Skill 2", "<Mouse>/rightButton", "<Gamepad>/leftTrigger"),
        new SkillSlotBinding("Skill 3", "<Keyboard>/q", "<Gamepad>/rightShoulder"),
        new SkillSlotBinding("Skill 4", "<Keyboard>/e", "<Gamepad>/leftShoulder"),
        new SkillSlotBinding("Skill 5", "<Keyboard>/r", "<Gamepad>/buttonNorth"),
    };

    // จำนวนปุ่มต่อชุด (= ช่องบน HUD) / ชุดที่กำลังใช้ (0 = ชุดแรก, 1 = ค้าง Ctrl) ให้ HUD อ่านไปแสดง
    public int SlotsPerSet => slotBindings.Count;
    public bool AlternateSetHeld => alternateSetAction != null && alternateSetAction.IsPressed();
    public int ActiveSet => AlternateSetHeld ? 1 : 0;
    public int SlotIndex(int button, int set) => button + set * SlotsPerSet;

    // ชื่อปุ่มสำหรับ HUD (เช่น "LMB", "Q") ตามอุปกรณ์ที่กำหนด (null = binding แรก)
    public string KeyLabel(int button)
    {
        if (button < 0 || button >= slotBindings.Count || slotBindings[button].action == null) return "";
        return slotBindings[button].action.GetBindingDisplayString(0, InputBinding.DisplayStringOptions.DontIncludeInteractions);
    }

    // ชื่อปุ่มสลับชุดสกิล (เช่น "Ctrl") ตาม binding ปัจจุบัน
    public string AlternateKeyLabel =>
        alternateSetAction != null && alternateSetAction.bindings.Count > 0
            ? alternateSetAction.GetBindingDisplayString(0, InputBinding.DisplayStringOptions.DontIncludeInteractions)
            : "";

    private void Reset() => slotBindings = DefaultBindings();

    private void Awake()
    {
        _combat = GetComponent<PlayerCombat>();
        // ข้อมูลเก่าที่ตั้งไว้ไม่ครบ 5 ปุ่ม -> เติมปุ่มเริ่มต้นที่ขาด
        var defaults = DefaultBindings();
        for (int i = slotBindings.Count; i < defaults.Count; i++) slotBindings.Add(defaults[i]);
    }

    private void OnEnable()
    {
        foreach (var b in slotBindings)
        {
            b.action?.Enable();
            KeyBindings.Register(KeyBindings.Skills, b.action);
        }
        alternateSetAction?.Enable();
        KeyBindings.Register(KeyBindings.Skills, alternateSetAction);
    }

    private void OnDisable()
    {
        foreach (var b in slotBindings)
        {
            KeyBindings.Unregister(b.action);
            b.action?.Disable();
        }
        KeyBindings.Unregister(alternateSetAction);
        alternateSetAction?.Disable();
    }

    private void Update()
    {
        if (LocalInputGate.GameplayBlocked)
        {
            // pause กลางท่า channel = เหมือนปล่อยปุ่ม
            if (_combat.Attack.IsAttacking) _combat.UpdateHeld(false, transform.forward, null);
            return;
        }
        Camera cam = aimCamera != null ? aimCamera : Camera.main;

        // ระหว่างท่า: ส่งแค่ว่าปุ่มของ slot ที่กำลังใช้ยังกดค้างไหม + ทิศ (ท่า channel วนต่อ/หยุดตามนี้)
        int active = _combat.ActiveSlot;
        if (_combat.Attack.IsAttacking)
        {
            InputAction a = ActionOf(active);
            bool stillHeld = a != null && a.IsPressed();
            _combat.UpdateHeld(stillHeld, stillHeld ? Aim(a, cam) : transform.forward, stillHeld ? AimPoint(a, cam) : null);
            return;
        }

        // กดใหม่ในเฟรมนี้มาก่อน แล้วค่อยกดค้าง (ใช้ซ้ำ) / slot เลขน้อยได้ก่อนถ้าพร้อมกัน
        if (TryUse(cam, repeat: false)) return;
        if (holdToRepeat) TryUse(cam, repeat: true);
    }

    private bool TryUse(Camera cam, bool repeat)
    {
        int set = ActiveSet;
        for (int i = 0; i < slotBindings.Count; i++)
        {
            InputAction a = slotBindings[i].action;
            if (a == null) continue;
            bool triggered = repeat ? a.IsPressed() : a.WasPressedThisFrame();
            if (!triggered) continue;
            if (ignoreUiAndPortalClicks && IsMouse(a) && IsClickOnUiOrInteractable(cam)) continue;
            if (LocalInputGate.KeyboardCaptured && a.activeControl != null && a.activeControl.device is Keyboard) continue;
            if (LocalInputGate.PointerCaptured && IsMouse(a)) continue;

            if (_combat.RequestAttack(SlotIndex(i, set), Aim(a, cam), a.IsPressed(), AimPoint(a, cam))) return true;
        }
        return false;
    }

    // ปุ่มของ slot (ทั้ง 2 ชุดใช้ปุ่มเดียวกัน) -> ปล่อย Ctrl กลางท่า channel ก็ยังค้างต่อได้ตราบที่ยังกดปุ่มเดิม
    private InputAction ActionOf(int slot) =>
        slot >= 0 && slotBindings.Count > 0 ? slotBindings[slot % slotBindings.Count].action : null;

    private static bool IsMouse(InputAction a) => a.activeControl != null && a.activeControl.device is Mouse;
    private static bool IsGamepad(InputAction a) => a.activeControl != null && a.activeControl.device is Gamepad;

    // เมาส์/คีย์บอร์ด = ไปทางเคอร์เซอร์ (แบบ PoE กด Q ก็ออกไปทางเมาส์) / gamepad = ทางที่หันอยู่
    private Vector3 Aim(InputAction a, Camera cam)
    {
        if (!IsGamepad(a) && aimAtCursor && cam != null && TryCursorOnGround(cam, out Vector3 point))
        {
            Vector3 toCursor = point - transform.position;
            toCursor.y = 0f;
            if (toCursor.sqrMagnitude > 0.01f) return toCursor;
        }
        return transform.forward;
    }

    // จุดเคอร์เซอร์บนพื้น (สกิลวาร์ปใช้เลือกเป้าใกล้เคอร์เซอร์) gamepad = ไม่มี (server เลือกเป้าหน้าตัว)
    private Vector3? AimPoint(InputAction a, Camera cam)
    {
        if (IsGamepad(a) || !aimAtCursor || cam == null) return null;
        return TryCursorOnGround(cam, out Vector3 point) ? point : (Vector3?)null;
    }

    private bool IsClickOnUiOrInteractable(Camera cam)
    {
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return true;
        if (cam == null || Mouse.current == null) return false;
        return ClickInteractable.IsInteractableUnder(cam.ScreenPointToRay(Mouse.current.position.ReadValue()), cam.farClipPlane);
    }

    // จุดที่เคอร์เซอร์ชี้บนระนาบแนวนอนระดับเท้า (ไม่ raycast collider -> ต้นไม้/หน้าผาไม่ทำให้ทิศเพี้ยน)
    private bool TryCursorOnGround(Camera cam, out Vector3 point)
    {
        point = default;
        if (Mouse.current == null) return false;
        Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
        var plane = new Plane(Vector3.up, transform.position);
        if (!plane.Raycast(ray, out float distance)) return false;
        point = ray.GetPoint(distance);
        return true;
    }
}

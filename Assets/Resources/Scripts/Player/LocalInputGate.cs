using UnityEngine.InputSystem;

// สถานะ input ของเครื่องนี้ที่ระบบอ่านปุ่มต้องเคารพ เช่นกำลังพิมพ์ในช่อง search -> คีย์บอร์ดไม่ใช่การเดิน/ใช้สกิล
// UI เป็นคนตั้งค่า (GameUI / GamePause) ฝั่ง input อ่านอย่างเดียว ไม่ต้องอ้าง type ของ UI (เป็นของ local client เท่านั้น server ไม่ใช้)
public static class LocalInputGate
{
    // true = UI จับคีย์บอร์ดอยู่ (กำลังพิมพ์)
    public static bool KeyboardCaptured { get; set; }

    // true = UI กำลังลากของด้วยเมาส์ (กดค้างลากออกนอกหน้าต่าง) -> ปุ่มเมาส์ไม่ใช่การโจมตี
    public static bool PointerCaptured { get; set; }

    // true = เปิดเมนู pause อยู่ -> ไม่รับ input gameplay เลย (เดิน/สกิล/dash)
    public static bool GameplayBlocked { get; set; }

    // คีย์บอร์ดสำหรับ gameplay (null ตอน UI จับคีย์บอร์ด)
    public static Keyboard Keyboard => KeyboardCaptured || GameplayBlocked ? null : Keyboard.current;

    // action นี้ใช้เป็น input gameplay ได้ไหมตอนนี้ (ปุ่มที่กำลังกดมาจากคีย์บอร์ดขณะพิมพ์ = ไม่ได้)
    public static bool Allows(InputAction action)
    {
        if (GameplayBlocked || action == null) return false;
        var control = action.activeControl;
        return !(KeyboardCaptured && control != null && control.device is Keyboard);
    }

    public static bool Pressed(InputAction action) => action != null && action.WasPressedThisFrame() && Allows(action);
    public static bool Held(InputAction action) => action != null && action.IsPressed() && Allows(action);
}

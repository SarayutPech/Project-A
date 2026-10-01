using UnityEngine.InputSystem;

// สถานะ input ของเครื่องนี้ที่ระบบอ่านปุ่มต้องเคารพ เช่นกำลังพิมพ์ในช่อง search -> คีย์บอร์ดไม่ใช่การเดิน/ใช้สกิล
// UI เป็นคนตั้งค่า (GameUI) ฝั่ง input อ่านอย่างเดียว ไม่ต้องอ้าง type ของ UI (เป็นของ local client เท่านั้น server ไม่ใช้)
public static class LocalInputGate
{
    // true = UI จับคีย์บอร์ดอยู่ (กำลังพิมพ์)
    public static bool KeyboardCaptured { get; set; }

    // true = UI กำลังลากของด้วยเมาส์ (กดค้างลากออกนอกหน้าต่าง) -> ปุ่มเมาส์ไม่ใช่การโจมตี
    public static bool PointerCaptured { get; set; }

    // คีย์บอร์ดสำหรับ gameplay (null ตอน UI จับคีย์บอร์ด)
    public static Keyboard Keyboard => KeyboardCaptured ? null : Keyboard.current;
}

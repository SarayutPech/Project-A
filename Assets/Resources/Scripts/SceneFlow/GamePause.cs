using UnityEngine;

// pause ของเครื่องนี้ (PauseMenu เป็นคนเรียก) = บล็อก input gameplay ของ local player + หยุดเวลาถ้าเล่นคนเดียว
// เล่นหลายคน (ภายหลังทำ Netcode) หยุดเวลาของ server ไม่ได้: เปิดเมนูได้ บล็อกแค่ input ของเรา เกมเดินต่อ
public static class GamePause
{
    public static bool IsPaused { get; private set; }
    public static event System.Action<bool> Changed;

    // TODO Netcode: false เมื่อต่อ session หลายคนอยู่ (เช่น NetworkManager.Singleton.IsListening)
    public static bool CanFreezeTime => true;

    public static void Set(bool paused)
    {
        if (IsPaused == paused) return;
        IsPaused = paused;
        LocalInputGate.GameplayBlocked = paused;
        Time.timeScale = paused && CanFreezeTime ? 0f : 1f;
        Changed?.Invoke(paused);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        IsPaused = false;
        Changed = null;
        LocalInputGate.GameplayBlocked = false;
        Time.timeScale = 1f;
    }
}

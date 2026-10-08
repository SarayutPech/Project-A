using UnityEngine;
using UnityEngine.SceneManagement;

// ใส่ CinematicCameraController ให้กล้อง iso ของทุกฉากอัตโนมัติ (ไม่ต้องวางมือในแต่ละ scene)
// - ฉากที่ไม่มี IsometricCameraController (เช่น CharacterSelect) จะไม่ถูกใส่
// - กล้อง iso ผูกกับ map ของแต่ละฉาก (subscribe MapGenerated / MapBuildAnimator) จึงไม่ DontDestroyOnLoad กล้อง
//   แต่ CinematicCameraController.Instance มีตัวเดียวเสมอ + จำสถานะเปิด/ปิดข้ามฉาก
// - ฝั่งภาพล้วน: server แบบ headless ไม่สร้าง
public static class CinematicCameraBootstrap
{
#if !UNITY_SERVER
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        Application.quitting += OnQuitting;
        Ensure();
    }

    private static void OnQuitting()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Application.quitting -= OnQuitting;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Ensure();

    // เรียกเองได้ถ้าสร้างกล้องทีหลังฉากโหลด (เช่นกล้อง iso ถูก spawn กลางทาง)
    public static void Ensure()
    {
        var iso = Object.FindFirstObjectByType<IsometricCameraController>();
        if (iso == null) return;

        // ตัวเดิมค้างอยู่บนกล้องของฉากที่เพิ่งถูกทำลาย Unity เคลียร์ Instance ให้เอง
        var existing = CinematicCameraController.Instance;
        if (existing != null && existing.gameObject == iso.gameObject) return;
        if (existing != null) Object.Destroy(existing);

        // ตัวซ้ำที่วางมือไว้บนกล้องอื่น: Awake ของมันจะทำลายตัวเองถ้ามี Instance อยู่แล้ว
        if (iso.TryGetComponent<CinematicCameraController>(out _)) return;
        iso.gameObject.AddComponent<CinematicCameraController>();
    }
#endif
}

using UnityEngine;
using UnityEngine.SceneManagement;

// เซฟ checkpoint ทุกครั้งที่เข้า scene ในรายการ (Hideout) = progress ตัวละคร + กล่อง loot + กระเป๋า + setting
// ไม่ต้องวาง component ใน scene (ฟัง sceneLoaded เองตั้งแต่เริ่มเกม)
public static class CheckpointSave
{
    public static readonly string[] Scenes = { "Hideout" };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded; // domain reload ปิด = กันลงซ้ำ
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single || System.Array.IndexOf(Scenes, scene.name) < 0) return;
        GameServices.SaveAll();
    }
}

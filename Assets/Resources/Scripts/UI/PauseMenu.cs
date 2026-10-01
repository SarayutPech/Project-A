using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// เมนู Esc (อยู่ใน GameUI prefab): Resume / Controls / Return to Hideout / Save & Character Select
// เปิด = GamePause (เล่นคนเดียว = หยุดเวลา + บล็อก input gameplay) / ปิด = เล่นต่อ / ปุ่ม Esc จัดการที่ GameUI
public class PauseMenu : UIWindow<PauseMenu>
{
    public Button resumeButton;
    public Button controlsButton;
    public Button hideoutButton;
    public Button characterSelectButton;
    public string hideoutScene = "Hideout";
    public string characterSelectScene = "CharacterSelect";

    protected override void Awake()
    {
        base.Awake();
        if (!IsInstance) return;
        if (resumeButton != null) resumeButton.onClick.AddListener(Close);
        if (controlsButton != null) controlsButton.onClick.AddListener(OpenControls);
        if (hideoutButton != null) hideoutButton.onClick.AddListener(ReturnToHideout);
        if (characterSelectButton != null) characterSelectButton.onClick.AddListener(SaveAndCharacterSelect);
    }

    protected override void OnDestroy()
    {
        if (resumeButton != null) resumeButton.onClick.RemoveListener(Close);
        if (controlsButton != null) controlsButton.onClick.RemoveListener(OpenControls);
        if (hideoutButton != null) hideoutButton.onClick.RemoveListener(ReturnToHideout);
        if (characterSelectButton != null) characterSelectButton.onClick.RemoveListener(SaveAndCharacterSelect);
        if (IsInstance && IsOpen) GamePause.Set(false);
        base.OnDestroy();
    }

    protected override void OnOpenChanged(bool open)
    {
        GamePause.Set(open);
        if (open)
        {
            // อยู่ Hideout แล้ว = ปุ่มกลับ Hideout ใช้ไม่ได้
            if (hideoutButton != null) hideoutButton.interactable = SceneManager.GetActiveScene().name != hideoutScene && Application.CanStreamedLevelBeLoaded(hideoutScene);
            if (characterSelectButton != null) characterSelectButton.interactable = Application.CanStreamedLevelBeLoaded(characterSelectScene);
        }
        else if (KeybindWindow.Instance != null && KeybindWindow.Instance.IsOpen) KeybindWindow.Instance.Close();
    }

    public void OpenControls()
    {
        if (KeybindWindow.Instance != null) KeybindWindow.Instance.Open();
    }

    public void ReturnToHideout()
    {
        if (SceneTransition.IsTransitioning) return;
        Close();
        SceneTransition.Load(hideoutScene);
    }

    // ข้อมูลตัวละครถูกเขียนลง service ทุกครั้งที่เปลี่ยนอยู่แล้ว -> ที่นี่ยืนยันลงที่เก็บถาวร (GameServices.SaveAll / WebGL = IndexedDB)
    // แล้วทำลาย player + GameUI ที่อยู่ข้าม scene (ตอนจอดำ) ก่อนโหลดหน้าเลือกตัวละคร
    public void SaveAndCharacterSelect()
    {
        if (SceneTransition.IsTransitioning) return;
        GameServices.SaveAll();
        Close();
        SceneTransition.Load(characterSelectScene, EndLocalSession);
    }

    private static void EndLocalSession()
    {
        var ui = GameUI.Instance;
        if (ui != null && ui.BoundPlayer != null) Destroy(ui.BoundPlayer);
        foreach (var m in FindObjectsByType<PlayerMovement>())
            if (m.persistAcrossScenes && m.gameObject.scene.name == "DontDestroyOnLoad") Destroy(m.gameObject);
        if (ui != null) Destroy(ui.gameObject);
        GameServices.LocalPlayerId = GameServices.DefaultPlayerId;
    }
}

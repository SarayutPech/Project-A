using UnityEngine;

// ประตู/portal สลับ scene: player ต้องอยู่ในระยะ interactRange แล้วคลิกซ้ายที่ object นี้ (ระบบคลิกอยู่ใน ClickInteractable)
// ต้องมี Collider (ใช้เป็นเป้าคลิก ตั้งเป็น trigger ก็ได้) และ scene ปลายทางต้องอยู่ใน Build Profiles (Scene List)
// ลาก scene ใส่ช่อง Destination Scene ใน Inspector ได้เลย ชื่อจะถูกเก็บลง destinationSceneName ให้อัตโนมัติ
public class ScenePortal : ClickInteractable
{
#if UNITY_EDITOR
    [Tooltip("scene ปลายทาง (ลากไฟล์ .unity มาใส่)")]
    [SerializeField] private UnityEditor.SceneAsset destinationScene;
#endif
    [Tooltip("ชื่อ scene ปลายทางที่ใช้ตอนรันจริง (ตั้งให้เองจากช่องด้านบน)")]
    public string destinationSceneName;

    protected override void Interact() => Enter();

    public void Enter()
    {
        SceneTransition.Load(destinationSceneName);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (destinationScene != null) destinationSceneName = destinationScene.name;
    }
#endif
}

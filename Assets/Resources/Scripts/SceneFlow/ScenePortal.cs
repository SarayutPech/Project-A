using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// ประตู/portal สลับ scene: player ต้องอยู่ในระยะ interactRange แล้วคลิกซ้ายที่ object นี้
// ต้องมี Collider (ใช้เป็นเป้าคลิก ตั้งเป็น trigger ก็ได้) และ scene ปลายทางต้องอยู่ใน Build Profiles (Scene List)
// ลาก scene ใส่ช่อง Destination Scene ใน Inspector ได้เลย ชื่อจะถูกเก็บลง destinationSceneName ให้อัตโนมัติ
[RequireComponent(typeof(Collider))]
public class ScenePortal : MonoBehaviour
{
#if UNITY_EDITOR
    [Tooltip("scene ปลายทาง (ลากไฟล์ .unity มาใส่)")]
    [SerializeField] private UnityEditor.SceneAsset destinationScene;
#endif
    [Tooltip("ชื่อ scene ปลายทางที่ใช้ตอนรันจริง (ตั้งให้เองจากช่องด้านบน)")]
    public string destinationSceneName;

    [Header("Interaction")]
    [Tooltip("ระยะห่างสูงสุดจาก player ถึงขอบ object (วัดแนวนอน)")]
    [Min(0f)] public float interactRange = 2.5f;
    [Tooltip("ถ้าเว้นว่างจะใช้ Camera.main")]
    public Camera raycastCamera;
    [Tooltip("object ที่แสดงตอน player อยู่ในระยะ เช่น ป้ายชื่อ/แสง (เว้นว่างได้)")]
    public GameObject inRangeIndicator;

    private Collider _collider;

    public bool PlayerInRange
    {
        get
        {
            var player = PlayerMovement.Instance;
            if (player == null) return false;

            Vector3 p = player.transform.position;
            Vector3 closest = _collider.bounds.ClosestPoint(p);
            closest.y = p.y = 0f;
            return (closest - p).sqrMagnitude <= interactRange * interactRange;
        }
    }

    private void Awake()
    {
        _collider = GetComponent<Collider>();
        if (inRangeIndicator != null) inRangeIndicator.SetActive(false);
    }

    private void Update()
    {
        bool inRange = !SceneTransition.IsTransitioning && PlayerInRange;
        if (inRangeIndicator != null && inRangeIndicator.activeSelf != inRange) inRangeIndicator.SetActive(inRange);

        if (inRange && ClickedThisFrame()) Enter();
    }

    public void Enter()
    {
        SceneTransition.Load(destinationSceneName);
    }

    private bool ClickedThisFrame()
    {
        var mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return false;

        // คลิกโดน UI อยู่ไม่นับ
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return false;

        Camera cam = raycastCamera != null ? raycastCamera : Camera.main;
        if (cam == null) return false;

        // เช็คเฉพาะ collider ของตัวเอง ไม่โดนของอื่นบัง (กล้อง isometric มีของบังบ่อย)
        Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
        return _collider.Raycast(ray, out _, cam.farClipPlane);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (destinationScene != null) destinationSceneName = destinationScene.name;
    }

    private void OnDrawGizmosSelected()
    {
        var col = GetComponent<Collider>();
        if (col == null) return;
        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.6f);
        Bounds b = col.bounds;
        Gizmos.DrawWireCube(b.center, new Vector3(b.size.x + interactRange * 2f, b.size.y, b.size.z + interactRange * 2f));
    }
#endif
}

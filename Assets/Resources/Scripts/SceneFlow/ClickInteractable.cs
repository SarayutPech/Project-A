using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// ฐานของ object ที่ player ต้องอยู่ในระยะ interactRange แล้วคลิกซ้ายเพื่อใช้ (portal, กล่องของดรอป, NPC ฯลฯ)
// ต้องมี Collider (ใช้เป็นเป้าคลิก ตั้งเป็น trigger ก็ได้)
// ตัวรับคลิกฝั่ง client (presentation/input) -> ภายหลังทำ Netcode ให้ Interact ส่งคำขอไป server แล้ว server เช็คระยะซ้ำ (กฎข้อ 8)
[RequireComponent(typeof(Collider))]
public abstract class ClickInteractable : MonoBehaviour
{
    [Header("Interaction")]
    [Tooltip("ระยะห่างสูงสุดจาก player ถึงขอบ object (วัดแนวนอน)")]
    [Min(0f)] public float interactRange = 2.5f;
    [Tooltip("ถ้าเว้นว่างจะใช้ Camera.main")]
    public Camera raycastCamera;
    [Tooltip("object ที่แสดงตอน player อยู่ในระยะ เช่น ป้ายชื่อ/แสง (เว้นว่างได้)")]
    public GameObject inRangeIndicator;

    protected Collider Collider { get; private set; }

    // local player (ตัวที่เครื่องนี้ควบคุม) อยู่ในระยะ
    public bool PlayerInRange
    {
        get
        {
            var player = PlayerMovement.Instance;
            if (player == null) return false;

            Vector3 p = player.transform.position;
            Vector3 closest = Collider.bounds.ClosestPoint(p);
            closest.y = p.y = 0f;
            return (closest - p).sqrMagnitude <= interactRange * interactRange;
        }
    }

    // ตัวที่เปิดอยู่ทั้งหมด ให้ระบบคลิกอื่น (เช่น PlayerAttackInput) รู้ว่าคลิกนี้เป็นการ interact ไม่ใช่โจมตี
    private static readonly List<ClickInteractable> _active = new List<ClickInteractable>();

    // ray นี้โดน object ที่ player อยู่ในระยะ (คลิกแล้วจะ interact)
    public static bool IsInteractableUnder(Ray ray, float maxDistance)
    {
        foreach (var it in _active)
            if (it.CanInteract && it.PlayerInRange && it.Collider.Raycast(ray, out _, maxDistance)) return true;
        return false;
    }

    protected virtual bool CanInteract => !SceneTransition.IsTransitioning;

    protected abstract void Interact();

    protected virtual void Awake()
    {
        Collider = GetComponent<Collider>();
        if (inRangeIndicator != null) inRangeIndicator.SetActive(false);
    }

    protected virtual void OnEnable() => _active.Add(this);
    protected virtual void OnDisable() => _active.Remove(this);

    protected virtual void Update()
    {
        bool inRange = CanInteract && PlayerInRange;
        if (inRangeIndicator != null && inRangeIndicator.activeSelf != inRange) inRangeIndicator.SetActive(inRange);

        if (inRange && ClickedThisFrame()) Interact();
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
        return Collider.Raycast(ray, out _, cam.farClipPlane);
    }

#if UNITY_EDITOR
    protected virtual void OnDrawGizmosSelected()
    {
        var col = GetComponent<Collider>();
        if (col == null) return;
        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.6f);
        Bounds b = col.bounds;
        Gizmos.DrawWireCube(b.center, new Vector3(b.size.x + interactRange * 2f, b.size.y, b.size.z + interactRange * 2f));
    }
#endif
}

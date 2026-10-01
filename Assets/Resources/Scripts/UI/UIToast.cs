using TMPro;
using UnityEngine;

// ข้อความแจ้งเตือนสั้นๆ กลางจอบน เช่น "Bag is full" (ตัวเดียวต่อเครื่อง) ขึ้นแล้วค่อยๆ จางหาย ข้อความซ้ำ = ต่อเวลา
[RequireComponent(typeof(CanvasGroup))]
public class UIToast : UISingleton<UIToast>
{
    public TextMeshProUGUI messageText;
    [Min(0.1f)] public float showTime = 1.8f;
    [Min(0.05f)] public float fadeTime = 0.4f;

    private CanvasGroup _group;
    private float _hideAt;

    protected override void Awake()
    {
        base.Awake();
        if (!IsInstance) return;
        _group = GetComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.blocksRaycasts = false;
        _group.interactable = false;
    }

    // เรียกได้จากทุกที่ ไม่มี toast ใน prefab = log แทน
    public static void Show(string message)
    {
        if (Instance == null)
        {
            Debug.Log($"[Toast] {message}");
            return;
        }
        Instance.ShowMessage(message);
    }

    public void ShowMessage(string message)
    {
        if (messageText != null) messageText.text = message;
        _hideAt = Time.unscaledTime + showTime;
        _group.alpha = 1f;
        transform.SetAsLastSibling();
    }

    private void Update()
    {
        if (_group == null || _group.alpha <= 0f) return;
        float left = _hideAt - Time.unscaledTime;
        _group.alpha = left >= 0f ? 1f : Mathf.Clamp01(1f + left / fadeTime);
    }
}

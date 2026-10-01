using TMPro;
using UnityEngine;
using UnityEngine.UI;

// หลอด HP / Mana / EXP / น้ำหนัก (presentation อย่างเดียว)
// fill: ถ้า Image เป็น Type = Filled (ต้องมี sprite) ใช้ fillAmount / ไม่งั้นยืด RectTransform ตามสัดส่วน
// trail: หลอดตามหลัง (เช่นสีขาวค่อยๆ ลดตอนโดนตี) เว้นว่างได้
public class UIBar : MonoBehaviour
{
    public Image fill;
    [Tooltip("หลอดที่ค่อยๆ ไล่ตามค่าจริง (เว้นว่างได้)")]
    public Image trail;
    public TextMeshProUGUI label;
    [Tooltip("ความเร็วหลอดตามหลัง (สัดส่วน/วินาที)")]
    [Min(0.01f)] public float trailSpeed = 0.6f;
    [Tooltip("ค่าหลอดหลักไล่ไปหาค่าจริงเร็วแค่ไหน 0 = ทันที")]
    [Min(0f)] public float smoothing = 12f;

    private float _value = -1f;
    private float _shown;
    private float _trail;

    public void Set(float current, float max, string text = null)
    {
        _value = max > 0f ? Mathf.Clamp01(current / max) : 0f;
        if (label != null && text != null && label.text != text) label.text = text;
    }

    private void LateUpdate()
    {
        if (_value < 0f) return;
        float dt = Time.unscaledDeltaTime;
        _shown = smoothing > 0f ? Mathf.Lerp(_shown, _value, 1f - Mathf.Exp(-smoothing * dt)) : _value;
        if (Mathf.Abs(_shown - _value) < 0.001f) _shown = _value;
        _trail = _trail > _shown ? Mathf.Max(_shown, _trail - trailSpeed * dt) : _shown;
        Apply(fill, _shown);
        Apply(trail, _trail);
    }

    private static void Apply(Image image, float t)
    {
        if (image == null) return;
        if (image.type == Image.Type.Filled && image.sprite != null)
        {
            image.fillAmount = t;
            return;
        }
        var rt = image.rectTransform;
        if (rt.anchorMax.x != t) rt.anchorMax = new Vector2(t, rt.anchorMax.y);
    }
}

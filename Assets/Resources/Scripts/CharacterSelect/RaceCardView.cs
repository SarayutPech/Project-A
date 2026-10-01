using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// การ์ด race 1 ใบใน RaceCarousel (ต้นแบบอยู่ใน CharacterSelectUI prefab ถูก clone ต่อ race)
// ตำแหน่ง/ขนาด/ความจาง ถูกตั้งโดย RaceCarousel ทุกเฟรม -> แต่ง art ในต้นแบบได้ แต่อย่าใส่ animation ที่ขยับ RectTransform ของ root
public class RaceCardView : MonoBehaviour, IPointerClickHandler
{
    public Image portrait;
    public TextMeshProUGUI nameText;
    [Tooltip("กรอบ/แสงที่แสดงเฉพาะการ์ดที่เลือก")]
    public GameObject selectedState;
    [Tooltip("ย้อมสี accent ของ race (ว่างได้)")]
    public Graphic accentGraphic;
    [Tooltip("race ไม่มี portrait ใช้รูปนี้")]
    public Sprite fallbackPortrait;
    public CanvasGroup group;

    public int Index { get; private set; }
    public System.Action<RaceCardView> Clicked;

    public RectTransform Rect => (RectTransform)transform;

    private Color _placeholderColor;
    private bool _captured;
    private Color _tint = Color.white;

    public void Set(CharacterRace race, int index)
    {
        Index = index;
        if (portrait != null)
        {
            // ยังไม่มีรูป = ใช้สีพื้นของต้นแบบ (ไม่งั้นเป็นสี่เหลี่ยมขาว)
            if (!_captured)
            {
                _placeholderColor = portrait.color;
                _captured = true;
            }
            portrait.sprite = race.portrait != null ? race.portrait : fallbackPortrait;
            _tint = portrait.sprite != null ? Color.white : _placeholderColor;
        }
        if (nameText != null) nameText.text = race.DisplayName;
        if (accentGraphic != null)
        {
            var c = race.accentColor;
            c.a = accentGraphic.color.a;
            accentGraphic.color = c;
        }
    }

    // ห่างจากกลาง = มืดลง (brightness 0..1) / จางลง (alpha)
    public void SetLook(float alpha, float brightness, bool selected)
    {
        if (group != null)
        {
            group.alpha = alpha;
            group.blocksRaycasts = alpha > 0.05f;
        }
        if (portrait != null) portrait.color = new Color(_tint.r * brightness, _tint.g * brightness, _tint.b * brightness, 1f);
        if (selectedState != null && selectedState.activeSelf != selected) selectedState.SetActive(selected);
    }

    public void OnPointerClick(PointerEventData e)
    {
        if (e.button == PointerEventData.InputButton.Left) Clicked?.Invoke(this);
    }
}

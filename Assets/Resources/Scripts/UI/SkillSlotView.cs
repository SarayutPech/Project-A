using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// ช่องสกิล 1 ช่องบน HUD (presentation) SkillBarView เป็นคนเติมข้อมูล / ชี้แล้วโชว์ชื่อ+รายละเอียด gem
// คลิก = เปิดหน้าต่าง Skill Gems ที่ช่องนี้ (เลือก gem หลัก / support ใส่ช่อง)
public class SkillSlotView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    public Image icon;
    [Tooltip("ทับไอคอนตอน cooldown (Image Type = Filled / Radial 360 ต้องมี sprite)")]
    public Image cooldownOverlay;
    public TextMeshProUGUI cooldownText;
    public TextMeshProUGUI keyLabel;
    [Tooltip("แสดงตอนช่องว่าง (ไม่มี gem)")]
    public GameObject emptyState;
    [Tooltip("แสดงตอนกำลังใช้สกิลช่องนี้")]
    public GameObject activeHighlight;

    private ResolvedSkill _skill;

    // index ของ PlayerSkills.slots ที่ช่องนี้แสดงอยู่ (เปลี่ยนตามชุด I/II)
    public int SlotIndex { get; private set; } = -1;

    public void Set(int slotIndex, ResolvedSkill skill, string key, float cooldownRemaining, float cooldownTotal, bool active)
    {
        SlotIndex = slotIndex;
        _skill = skill;
        bool has = skill != null && skill.Gem != null;
        if (icon != null)
        {
            icon.sprite = has ? skill.Gem.icon : null;
            icon.enabled = has && skill.Gem.icon != null;
        }
        if (emptyState != null && emptyState.activeSelf == has) emptyState.SetActive(!has);
        if (keyLabel != null && keyLabel.text != key) keyLabel.text = key;
        if (activeHighlight != null && activeHighlight.activeSelf != active) activeHighlight.SetActive(active);

        bool cooling = has && cooldownRemaining > 0f && cooldownTotal > 0f;
        if (cooldownOverlay != null)
        {
            cooldownOverlay.enabled = cooling;
            if (cooling) cooldownOverlay.fillAmount = Mathf.Clamp01(cooldownRemaining / cooldownTotal);
        }
        if (cooldownText != null)
        {
            string t = cooling && cooldownRemaining >= 0.1f ? cooldownRemaining.ToString(cooldownRemaining < 1f ? "0.0" : "0") : "";
            if (cooldownText.text != t) cooldownText.text = t;
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_skill == null || _skill.Gem == null || ItemTooltip.Instance == null) return;
        var gem = _skill.Gem;
        string body = $"Level {_skill.Level}\nDamage: {StatMath.FormatRange(_skill.DamageMin, _skill.DamageMax)}\nAttack Speed: {_skill.AttackSpeed:0.##}x";
        if (_skill.ScaledCooldown > 0.01f) body += $"\nCooldown: {_skill.ScaledCooldown:0.##}s";
        if (!string.IsNullOrEmpty(gem.description)) body += $"\n\n<i><color=#A0A0A0>{gem.description}</color></i>";
        body += "\n\n<color=#8C8C8C>Click to change gems</color>";
        ItemTooltip.Instance.ShowText(gem.displayName, "Skill", body, gem.gemColor);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || SlotIndex < 0 || SkillGemWindow.Instance == null) return;
        if (ItemTooltip.Instance != null) ItemTooltip.Instance.Hide();
        SkillGemWindow.Instance.OpenFor(SlotIndex);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (ItemTooltip.Instance != null) ItemTooltip.Instance.Hide();
    }
}

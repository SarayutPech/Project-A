using TMPro;
using UnityEngine;
using UnityEngine.UI;

// แถวช่องสกิล 1 ช่อง (ด้านซ้ายของหน้าต่าง Skill Gems) เช่น "I · LMB  Flicker Strike" คลิก = เลือกช่องนี้
public class SkillSlotListItem : MonoBehaviour
{
    public Button button;
    public Image icon;
    public TextMeshProUGUI keyText;
    public TextMeshProUGUI nameText;
    [Tooltip("จำนวน support ที่ใส่ เช่น \"3/5 links\"")]
    public TextMeshProUGUI linksText;
    public GameObject selectedState;

    public int SlotIndex { get; private set; }

    public void Set(int slotIndex, string key, ActiveSkillGem gem, int supports, bool selected)
    {
        SlotIndex = slotIndex;
        if (keyText != null) keyText.text = key;
        if (nameText != null)
        {
            nameText.text = gem != null ? gem.displayName : "<i>Empty</i>";
            nameText.color = gem != null ? gem.gemColor : new Color(1f, 1f, 1f, 0.35f);
        }
        if (icon != null)
        {
            icon.sprite = gem != null ? gem.icon : null;
            icon.enabled = gem != null && gem.icon != null;
        }
        if (linksText != null)
        {
            linksText.text = $"{supports}/{PlayerSkills.MaxSupportSlots} links";
        }
        if (selectedState != null) selectedState.SetActive(selected);
    }
}

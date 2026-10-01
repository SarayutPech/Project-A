using TMPro;
using UnityEngine;

// แถบสกิล 5 ช่องมุมล่างขวา: แสดงชุดที่ PlayerAttackInput กำลังใช้ (ค้าง Ctrl = ชุดที่ 2 ปล่อย = กลับชุดแรก)
// ช่องที่ i ของ HUD = ปุ่มที่ i ของ PlayerAttackInput -> PlayerSkills.slots[i + ชุด × 5]
public class SkillBarView : UISingleton<SkillBarView>, IPlayerUI
{
    public SkillSlotView[] slots = new SkillSlotView[PlayerAttackInput.DefaultSlotsPerSet];
    [Tooltip("ป้ายบอกชุด เช่น I / II (เว้นว่างได้)")]
    public TextMeshProUGUI setLabel;
    [Tooltip("แสดงตอนอยู่ชุดที่ 2 เช่นกรอบเรืองแสง (เว้นว่างได้)")]
    public GameObject alternateSetIndicator;
    public string[] setNames = { "I", "II" };
    [Tooltip("คำนำหน้าปุ่มตอนชุดที่ 2")]
    public string alternatePrefix = "Ctrl+";

    private PlayerAttackInput _input;
    private PlayerSkills _skills;
    private PlayerCombat _combat;

    public void Bind(GameObject player)
    {
        _input = player != null ? player.GetComponent<PlayerAttackInput>() : null;
        _skills = player != null ? player.GetComponent<PlayerSkills>() : null;
        _combat = player != null ? player.GetComponent<PlayerCombat>() : null;
    }

    private void LateUpdate()
    {
        if (_input == null || _skills == null) return;

        int set = _input.ActiveSet;
        if (setLabel != null)
        {
            string s = set < setNames.Length ? setNames[set] : (set + 1).ToString();
            if (setLabel.text != s) setLabel.text = s;
        }
        if (alternateSetIndicator != null && alternateSetIndicator.activeSelf != (set == 1)) alternateSetIndicator.SetActive(set == 1);

        int activeSlot = _combat != null ? _combat.ActiveSlot : -1;
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] == null) continue;
            int index = _input.SlotIndex(i, set);
            SkillSlot s = _skills.GetSlot(index);
            ResolvedSkill skill = s != null ? s.Resolved : null;
            float remaining = skill != null ? _skills.Attack.CooldownRemaining(skill.Gem) : 0f;
            float total = skill != null ? skill.ScaledCooldown : 0f;
            string key = i < _input.SlotsPerSet ? _input.KeyLabel(i) : "";
            if (set == 1 && key.Length > 0) key = alternatePrefix + key;
            slots[i].Set(index, skill, key, remaining, total, index == activeSlot);
        }
    }
}

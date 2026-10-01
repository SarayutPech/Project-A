using TMPro;
using UnityEngine;

// HUD มุมบนซ้าย: HP / Mana / EXP + เลเวล (อ่านค่าจาก component ของ local player อย่างเดียว)
// ภายหลังทำ Netcode: ค่าพวกนี้มาจาก NetworkVariable ที่ server sync มา โค้ดนี้ไม่ต้องแก้
public class StatusHud : UISingleton<StatusHud>, IPlayerUI
{
    public UIBar healthBar;
    public UIBar manaBar;
    public UIBar experienceBar;
    public TextMeshProUGUI levelText;

    private Health _health;
    private Mana _mana;
    private PlayerExperience _exp;

    public void Bind(GameObject player)
    {
        _health = player != null ? player.GetComponent<Health>() : null;
        _mana = player != null ? player.GetComponent<Mana>() : null;
        _exp = player != null ? player.GetComponent<PlayerExperience>() : null;
        if (manaBar != null) manaBar.gameObject.SetActive(player == null || _mana != null);
    }

    private void LateUpdate()
    {
        if (_health != null && healthBar != null)
            healthBar.Set(_health.Current, _health.maxHealth, $"{Mathf.CeilToInt(_health.Current)} / {Mathf.CeilToInt(_health.maxHealth)}");

        if (_mana != null && manaBar != null)
            manaBar.Set(_mana.Current, _mana.maxMana, $"{Mathf.FloorToInt(_mana.Current)} / {Mathf.FloorToInt(_mana.maxMana)}");

        if (_exp != null)
        {
            if (experienceBar != null)
                experienceBar.Set(_exp.Normalized, 1f, _exp.IsMaxLevel ? "MAX" : $"{_exp.Experience} / {_exp.ExpToNextLevel}  ({_exp.Normalized * 100f:0.0}%)");
            if (levelText != null)
            {
                string lv = _exp.Level.ToString();
                if (levelText.text != lv) levelText.text = lv;
            }
        }
    }
}

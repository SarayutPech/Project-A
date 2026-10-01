using UnityEngine;

// เลเวล/EXP ของ player (simulation ฝั่ง server) ได้ EXP เมื่อศัตรูตายโดยดาเมจครั้งสุดท้ายมาจากตัวนี้
// เลเวลขึ้น -> modifier แหล่ง "level" ใน PlayerStats (HP/มานาต่อเลเวล) / เซฟผ่าน ICharacterService
// ภายหลังทำ Netcode: แบ่ง EXP ให้ทั้ง party ในระยะ, Level/Experience เป็น NetworkVariable
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerStats))]
public class PlayerExperience : MonoBehaviour
{
    [Header("Curve")]
    [Min(1)] public int maxLevel = 100;
    [Tooltip("EXP ที่ต้องใช้จาก level 1 -> 2")]
    [Min(1f)] public float baseExpToLevel = 100f;
    [Tooltip("EXP ต่อเลเวลโตแบบ ฐาน × level^ค่านี้")]
    [Min(1f)] public float expGrowth = 1.6f;

    [Header("Per Level")]
    [Min(0f)] public float healthPerLevel = 8f;
    [Min(0f)] public float manaPerLevel = 4f;

    [Header("Kill Reward")]
    [Tooltip("EXP ต่อศัตรู 1 ตัว (ศัตรูที่มี ExperienceReward ใช้ค่าของตัวมันเอง)")]
    [Min(0)] public int defaultExpPerKill = 10;
    [Tooltip("ตัวคูณ EXP ของ elite")]
    [Min(1f)] public float eliteExpMultiplier = 3f;

    public int Level => _progress.level;
    public long Experience => _progress.experience;
    public long ExpToNextLevel => ExpRequired(_progress.level);
    public bool IsMaxLevel => _progress.level >= maxLevel;
    public float Normalized => IsMaxLevel ? 1f : Mathf.Clamp01((float)_progress.experience / System.Math.Max(1L, ExpToNextLevel));

    public event System.Action<PlayerExperience> Changed;
    public event System.Action<PlayerExperience> LeveledUp;

    private const string Source = "level";
    private CharacterProgress _progress = CharacterProgress.New;
    private PlayerStats _stats;
    private string OwnerId => GameServices.LocalPlayerId; // TODO Netcode: id ของเจ้าของตัวนี้

    private void Awake()
    {
        _stats = GetComponent<PlayerStats>();
        _progress = GameServices.Character.Load(OwnerId);
        _progress.level = Mathf.Clamp(_progress.level, 1, maxLevel);
        ApplyLevelStats(refill: true);
    }

    private void OnEnable() => Health.AnyDied += OnAnyDied;
    private void OnDisable() => Health.AnyDied -= OnAnyDied;

    public long ExpRequired(int level) => (long)Mathf.Round(baseExpToLevel * Mathf.Pow(Mathf.Max(1, level), expGrowth));

    // ---------- server ----------

    public void AddExperience(long amount)
    {
        if (amount <= 0 || IsMaxLevel) return;
        _progress.experience += amount;
        bool leveled = false;
        while (!IsMaxLevel && _progress.experience >= ExpToNextLevel)
        {
            _progress.experience -= ExpToNextLevel;
            _progress.level++;
            leveled = true;
        }
        if (IsMaxLevel) _progress.experience = 0;

        GameServices.Character.Save(OwnerId, _progress);
        if (leveled)
        {
            ApplyLevelStats(refill: true);
            LeveledUp?.Invoke(this);
        }
        Changed?.Invoke(this);
    }

    private void OnAnyDied(Health dead)
    {
        if (dead.team == Team.Player || dead.LastDamage.source != gameObject) return;

        var reward = dead.GetComponent<ExperienceReward>();
        float exp = reward != null ? reward.experience : defaultExpPerKill;
        var rank = dead.GetComponent<EnemyRank>();
        if (rank != null && rank.IsElite) exp *= eliteExpMultiplier;
        AddExperience((long)Mathf.Round(exp));
    }

    // HP/มานาจากเลเวล (เลเวลขึ้นเติมเต็ม แบบ PoE)
    private void ApplyLevelStats(bool refill)
    {
        _stats.RemoveModifiers(Source);
        int bonusLevels = _progress.level - 1;
        if (bonusLevels > 0)
        {
            _stats.AddModifier(Source, new StatModifier(StatType.MaxHealth, ModifierType.Flat, healthPerLevel * bonusLevels));
            _stats.AddModifier(Source, new StatModifier(StatType.MaxMana, ModifierType.Flat, manaPerLevel * bonusLevels));
        }
        if (!refill) return;
        var health = GetComponent<Health>();
        if (health != null && !health.IsDead) health.Heal(health.maxHealth);
        var mana = GetComponent<Mana>();
        if (mana != null) mana.Refill();
    }

    [ContextMenu("Add 50 EXP")]
    private void DebugAddExp() => AddExperience(50);

    [ContextMenu("Reset Level")]
    private void DebugReset()
    {
        _progress = CharacterProgress.New;
        GameServices.Character.Save(OwnerId, _progress);
        ApplyLevelStats(refill: true);
        Changed?.Invoke(this);
    }
}

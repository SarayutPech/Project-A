using System.Collections.Generic;
using UnityEngine;

public enum Team
{
    Player,
    Enemy,
}

public struct DamageInfo
{
    public float amount;
    public GameObject source;   // ผู้ทำดาเมจ (null ได้ เช่นกับดัก)
    public Vector3 direction;   // ทิศที่โดนตี (แนวนอน) ไว้ให้ฝั่งภาพทำ hit reaction
}

// HP ของตัวละคร (ทั้ง player และศัตรู) = simulation ล้วน ตัดสินฝั่ง server
// ApplyDamage / Revive ต้องเรียกจากโค้ดฝั่ง server เท่านั้น (ตอนนี้ยังไม่มี netcode = เครื่องเดียวเป็น server)
// ภายหลังทำ Netcode: Current เป็น NetworkVariable, client อ่านอย่างเดียว
[DisallowMultipleComponent]
public class Health : MonoBehaviour
{
    public Team team = Team.Enemy;
    [Min(1f)] public float maxHealth = 100f;

    public float Current { get; private set; }
    public bool IsDead => Current <= 0f;
    public float Normalized => maxHealth > 0f ? Current / maxHealth : 0f;
    // นับครั้งที่โดนตี (เพิ่มทีละ 1) ให้ฝั่งภาพเทียบค่าเก่าเพื่อเล่น hit effect / sync ผ่านเน็ตเป็นตัวเลขตัวเดียวได้
    public int HitCount { get; private set; }

    // ดาเมจครั้งล่าสุด (เช่นใช้ทิศกระเด็นตอนตาย)
    public DamageInfo LastDamage { get; private set; }

    public event System.Action<Health, DamageInfo> Damaged;
    public event System.Action<Health> Died;
    public event System.Action<Health> Revived;
    // ทุกตัวในเกมโดนดาเมจ (ฝั่งภาพเช่นเลขดาเมจ subscribe ที่เดียว ไม่ต้องไล่ subscribe ทีละตัว) อย่าลืม -= ตอนเลิกใช้
    public static event System.Action<Health, DamageInfo> AnyDamaged;

    // ตัวละครที่ยังเปิดอยู่ทั้งหมด ให้ AI หาเป้าโดยไม่ต้องพึ่ง PlayerMovement.Instance (กฎข้อ 3)
    // ตอนนี้เป็นรายการรวมทั้ง process: ถ้าภายหลังรันหลาย map instance ใน process เดียวให้ย้ายไปเก็บใน context ของ instance
    private static readonly List<Health> _all = new List<Health>();
    public static IReadOnlyList<Health> All => _all;

    private void Awake() => Current = maxHealth;

    private void OnEnable() => _all.Add(this);
    private void OnDisable() => _all.Remove(this);

    // คืน true ถ้าโดนจริง (ตายแล้ว/ดาเมจ <= 0 ไม่นับ)
    public bool ApplyDamage(in DamageInfo info)
    {
        if (IsDead || info.amount <= 0f) return false;

        Current = Mathf.Max(0f, Current - info.amount);
        HitCount++;
        LastDamage = info;
        Damaged?.Invoke(this, info);
        AnyDamaged?.Invoke(this, info);
        if (Current <= 0f) Died?.Invoke(this);
        return true;
    }

    // เปลี่ยน max HP (เช่นศัตรู elite) refill = เติมเต็ม / ไม่งั้นคงสัดส่วน HP เดิม
    public void SetMaxHealth(float value, bool refill)
    {
        float ratio = Normalized;
        maxHealth = Mathf.Max(1f, value);
        if (IsDead) return;
        Current = refill ? maxHealth : Mathf.Max(1f, maxHealth * ratio);
    }

    // เติม HP (ยา/สกิลฟื้น) ตายแล้วไม่ฟื้น คืนจำนวนที่เติมได้จริง
    public float Heal(float amount)
    {
        if (IsDead || amount <= 0f) return 0f;
        float before = Current;
        Current = Mathf.Min(maxHealth, Current + amount);
        return Current - before;
    }

    public void Revive(float fraction = 1f)
    {
        bool wasDead = IsDead;
        Current = Mathf.Clamp(maxHealth * fraction, 1f, maxHealth);
        if (wasDead) Revived?.Invoke(this);
    }

    [ContextMenu("Kill")]
    private void DebugKill() => ApplyDamage(new DamageInfo { amount = Current });

    [ContextMenu("Revive")]
    private void DebugRevive() => Revive();
}

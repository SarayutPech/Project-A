using UnityEngine;

// มานาของตัวละคร = simulation ล้วน ตัดสินฝั่ง server (แบบเดียวกับ Health)
// max / regen มาจาก PlayerStats (ห้ามแก้ maxMana ตรงๆ sheet จะเขียนทับ) ใช้/เติมผ่าน TrySpend / Restore
// ภายหลังทำ Netcode: Current เป็น NetworkVariable, client อ่านอย่างเดียว
[DisallowMultipleComponent]
public class Mana : MonoBehaviour
{
    [Min(0f)] public float maxMana = 50f;
    [Tooltip("ฟื้นต่อวินาที")]
    [Min(0f)] public float regenPerSecond = 2f;

    public float Current { get; private set; }
    public float Normalized => maxMana > 0f ? Current / maxMana : 0f;

    private Health _health;

    private void Awake()
    {
        _health = GetComponent<Health>();
        Current = maxMana;
    }

    // tick ฟิสิกส์ ไม่ผูก frame rate (กฎข้อ 9) ตายแล้วไม่ฟื้น
    private void FixedUpdate()
    {
        if (regenPerSecond <= 0f || Current >= maxMana) return;
        if (_health != null && _health.IsDead) return;
        Current = Mathf.Min(maxMana, Current + regenPerSecond * Time.fixedDeltaTime);
    }

    // ใช้มานา ไม่พอ = ไม่หักเลย คืน false
    public bool TrySpend(float amount)
    {
        if (amount <= 0f) return true;
        if (Current < amount) return false;
        Current -= amount;
        return true;
    }

    // เติมมานา คืนจำนวนที่เติมได้จริง
    public float Restore(float amount)
    {
        if (amount <= 0f) return 0f;
        float before = Current;
        Current = Mathf.Min(maxMana, Current + amount);
        return Current - before;
    }

    // จาก PlayerStats: refill = เติมเต็ม / ไม่งั้นคงสัดส่วนเดิม
    public void SetMax(float max, float regen, bool refill)
    {
        float ratio = Normalized;
        maxMana = Mathf.Max(0f, max);
        regenPerSecond = Mathf.Max(0f, regen);
        Current = refill ? maxMana : maxMana * ratio;
    }

    public void Refill() => Current = maxMana;
}

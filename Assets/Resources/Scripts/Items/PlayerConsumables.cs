using System.Collections.Generic;
using UnityEngine;

// ใช้ของกดใช้ (simulation ฝั่ง server): เช็ค cooldown ต่อชนิด -> เติม HP / ใส่บัฟชั่วคราวเข้า PlayerStats -> หมดเวลาถอดเอง
// ตัวรับ input (hotbar/ปุ่มยา) เรียก RequestUse เท่านั้น ห้ามเติม HP เอง (กฎข้อ 1, 4)
// การหักจำนวนไอเทมทำโดยผู้เรียก (inventory) หลัง RequestUse คืน true -> ภายหลังทำ Netcode เป็น Rpc "ขอใช้ไอเทม id X" แล้ว server หักของเอง
[DisallowMultipleComponent]
[RequireComponent(typeof(Health))]
public class PlayerConsumables : MonoBehaviour
{
    private Health _health;
    private PlayerStats _stats;

    private readonly Dictionary<string, float> _readyTime = new Dictionary<string, float>(); // item id -> เวลาที่ใช้ได้อีก
    private readonly Dictionary<string, float> _buffEnd = new Dictionary<string, float>();   // แหล่งบัฟ -> เวลาหมด
    private readonly List<string> _expired = new List<string>();

    public event System.Action<PlayerConsumables, ConsumableItem> Used;

    private void Awake()
    {
        _health = GetComponent<Health>();
        _stats = GetComponent<PlayerStats>();
    }

    public bool IsReady(ConsumableItem item) =>
        item != null && (!_readyTime.TryGetValue(item.id, out float t) || Time.time >= t);

    public float CooldownRemaining(ConsumableItem item) =>
        item != null && _readyTime.TryGetValue(item.id, out float t) ? Mathf.Max(0f, t - Time.time) : 0f;

    // คืน true ถ้าใช้สำเร็จ (ผู้เรียกหักไอเทม 1 ชิ้น)
    public bool RequestUse(ConsumableItem item)
    {
        if (item == null || _health.IsDead || !IsReady(item)) return false;

        if (item.HasHeal)
            _health.Heal(item.healFlat + _health.maxHealth * item.healPercent * 0.01f);

        if (item.HasBuff && _stats != null)
        {
            string source = "buff:" + item.id;
            _stats.RemoveModifiers(source); // ใช้ซ้ำ = รีเวลา ไม่ซ้อน
            foreach (var mod in item.buffs) _stats.AddModifier(source, mod);
            _buffEnd[source] = Time.time + item.buffDuration;
        }

        _readyTime[item.id] = Time.time + item.cooldown;
        Used?.Invoke(this, item);
        return true;
    }

    // ถอดบัฟที่หมดเวลา (tick ฟิสิกส์ ไม่ผูก frame rate กฎข้อ 9)
    private void FixedUpdate()
    {
        if (_buffEnd.Count == 0) return;
        _expired.Clear();
        foreach (var kv in _buffEnd)
            if (Time.time >= kv.Value) _expired.Add(kv.Key);
        foreach (var source in _expired)
        {
            _buffEnd.Remove(source);
            if (_stats != null) _stats.RemoveModifiers(source);
        }
    }
}

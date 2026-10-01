using System.Collections.Generic;
using UnityEngine;

// ใส่ที่ศัตรู: ตายแล้วสุ่มของจาก LootTable ส่งเข้ากล่องรับของ (ILootStashService) ของผู้เล่น -> ไปเปิดดูที่ hideout (LootChest)
// สุ่ม/สร้าง drop ฝั่ง server เท่านั้น (ภายหลังทำ Netcode: เช็ค IsServer ก่อน) client ไม่ได้สร้างของเอง (กฎข้อ 1, 7)
[RequireComponent(typeof(Health))]
public class LootDropper : MonoBehaviour
{
    public LootTable table;
    [Tooltip("elite สุ่มกี่เท่าของตัวปกติ")]
    [Min(1)] public int eliteRollMultiplier = 3;

    // ของดรอป (ตำแหน่งที่ตาย, ไอเทม) ให้ฝั่งภาพทำป้าย/เสียงได้ ไม่มีผลกับ gameplay อย่าลืม -= ตอนเลิกใช้
    public static event System.Action<Vector3, ItemStack> AnyDropped;

    private Health _health;
    private System.Random _rng;
    private readonly List<ItemStack> _results = new List<ItemStack>();

    private void Awake()
    {
        _health = GetComponent<Health>();
        _rng = new System.Random(unchecked(System.Environment.TickCount * 31 + GetEntityId().GetHashCode()));
    }

    private void OnEnable() => _health.Died += OnDied;
    private void OnDisable() => _health.Died -= OnDied;

    private void OnDied(Health health)
    {
        if (table == null) return;

        var rank = GetComponent<EnemyRank>();
        int multiplier = rank != null && rank.IsElite ? eliteRollMultiplier : 1;

        _results.Clear();
        table.Roll(_rng, _results, multiplier);
        if (_results.Count == 0) return;

        string owner = ResolveOwner(health.LastDamage.source);
        var stash = GameServices.LootStash;
        foreach (var stack in _results)
        {
            stash.Add(owner, stack);
            AnyDropped?.Invoke(transform.position, stack);
        }
    }

    // ของเป็นของคนที่ฆ่า ตอนนี้มีผู้เล่นเครื่องเดียว -> LocalPlayerId
    // ภายหลังทำ Netcode: หา owner จาก NetworkObject ของ source (หรือแจกทุกคนในปาร์ตี้แบบ PoE)
    private static string ResolveOwner(GameObject killer) => GameServices.LocalPlayerId;
}

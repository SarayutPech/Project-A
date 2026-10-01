using System.Collections.Generic;
using UnityEngine;

// ตารางดรอป: สุ่มกี่ครั้ง แต่ละครั้งได้ไอเทมไหน (ตาม weight) กี่ชิ้น
// สุ่มด้วย System.Random ที่ผู้เรียกส่งมา (ฝั่ง server เท่านั้น client ไม่สุ่มของเอง)
[CreateAssetMenu(menuName = "Items/Loot Table", fileName = "LootTable_")]
public class LootTable : ScriptableObject
{
    [System.Serializable]
    public struct Entry
    {
        public ItemDefinition item;
        [Tooltip("น้ำหนักโอกาส (เทียบกับ entry อื่นในตาราง) 0 = ไม่ดรอป")]
        [Min(0f)] public float weight;
        [Tooltip("จำนวนต่อครั้ง (min, max) รวมปลาย")]
        public Vector2Int countRange;
    }

    [Tooltip("โอกาสที่การสุ่มแต่ละครั้งจะได้ของ (1 = ได้ทุกครั้ง)")]
    [Range(0f, 1f)] public float dropChance = 0.35f;
    [Tooltip("สุ่มกี่ครั้งต่อการตาย 1 ตัว (min, max)")]
    public Vector2Int rolls = new Vector2Int(1, 1);
    public List<Entry> entries = new List<Entry>();

    // สุ่มของใส่ results (ไม่ล้างของเดิม) rollMultiplier = คูณจำนวนครั้ง เช่น elite ดรอปเยอะกว่า
    public void Roll(System.Random rng, List<ItemStack> results, int rollMultiplier = 1)
    {
        float totalWeight = 0f;
        foreach (var e in entries)
            if (e.item != null) totalWeight += e.weight;
        if (totalWeight <= 0f) return;

        int min = Mathf.Max(0, Mathf.Min(rolls.x, rolls.y)), max = Mathf.Max(rolls.x, rolls.y);
        int count = rng.Next(min, max + 1) * Mathf.Max(1, rollMultiplier);
        for (int i = 0; i < count; i++)
        {
            if (rng.NextDouble() >= dropChance) continue;

            double pick = rng.NextDouble() * totalWeight;
            foreach (var e in entries)
            {
                if (e.item == null || e.weight <= 0f) continue;
                pick -= e.weight;
                if (pick > 0) continue;

                int lo = Mathf.Max(1, Mathf.Min(e.countRange.x, e.countRange.y));
                int hi = Mathf.Max(lo, Mathf.Max(e.countRange.x, e.countRange.y));
                results.Add(new ItemStack(e.item.id, rng.Next(lo, hi + 1)));
                break;
            }
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e.countRange == Vector2Int.zero) e.countRange = Vector2Int.one; // entry ใหม่ใน Inspector
            entries[i] = e;
        }
    }
#endif
}

using UnityEngine;

// วางไว้ใน scene ของ map: เข้า map ใหม่ = ทิ้งของที่ดรอปจาก map ก่อนที่ยังไม่ได้เก็บในกล่อง (LootChest)
// ตัดสินฝั่ง server (ภายหลังทำ Netcode: เช็ค IsServer / ล้างของทุกคนที่เข้า instance นี้แทน LocalPlayerId)
public class MapLootReset : MonoBehaviour
{
    [Tooltip("generate map ใหม่ใน scene เดิม (regenerate/seed ใหม่) ก็ล้างด้วย")]
    public bool clearOnRegenerate = true;
    [Tooltip("ถ้าเว้นว่างจะหาในฉากเดียวกัน")]
    public ProceduralMapGenerator generator;

    private ProceduralMapGenerator _gen;

    // Awake: ล้างก่อนศัตรูเกิด (EnemySpawner เกิดตอน Start หรือหลัง animation) ของที่ดรอปใน map นี้ไม่โดนลบ
    private void Awake()
    {
        Clear();

        _gen = generator;
        if (_gen == null)
            foreach (var g in FindObjectsByType<ProceduralMapGenerator>())
                if (g.gameObject.scene == gameObject.scene) { _gen = g; break; }
        if (_gen != null && clearOnRegenerate) _gen.MapGenerated += Clear;
    }

    private void OnDestroy()
    {
        if (_gen != null) _gen.MapGenerated -= Clear;
    }

    private void Clear()
    {
        int removed = GameServices.LootStash.Clear(GameServices.LocalPlayerId);
        if (removed > 0) Debug.Log($"[{nameof(MapLootReset)}] เข้า map ใหม่ ลบของเก่าที่ยังไม่เก็บ {removed} ชิ้น", this);
    }
}

using System.Collections.Generic;
using UnityEngine;

// กลุ่มศัตรูที่เกิดด้วยกัน (simulation ฝั่ง server) ไม่ใช่ component -> ไม่มี object ค้างในฉาก
// ใช้จุดกลาง pack เป็นจุดเดินเล่นร่วมกัน และให้ตัวที่เจอ player ก่อนเรียกเพื่อนมารุม
public class EnemyPack
{
    public int Id { get; }
    public Vector3 Center { get; }
    public IReadOnlyList<EnemyAI> Members => _members;

    private readonly List<EnemyAI> _members = new List<EnemyAI>();

    public EnemyPack(int id, Vector3 center)
    {
        Id = id;
        Center = center;
    }

    public void Add(EnemyAI member)
    {
        if (!_members.Contains(member)) _members.Add(member);
    }

    public void Remove(EnemyAI member) => _members.Remove(member);

    // ตัว source เริ่มไล่เป้า -> ทุกตัวที่ยังไม่ได้ไล่ไล่ตาม
    public void Alert(Health target, EnemyAI source)
    {
        // copy ก่อนวน เผื่อตัวไหนตาย/ถูกลบระหว่างนั้น
        foreach (var member in _members.ToArray())
            if (member != null && member != source) member.Alert(target);
    }
}

using System.Collections.Generic;
using UnityEngine;

// ตั้งค่าหน้าสร้าง/เลือกตัวละคร: จำนวน slot, กติกาชื่อ, รายการ race (ลำดับในลิสต์ = ลำดับใน carousel / race แรก = ที่เลือกไว้ตอนเปิด)
// ฝั่ง server ใช้ตัวเดียวกันตรวจคำขอสร้างตัวละคร (CharacterCreation)
[CreateAssetMenu(menuName = "Project-A/Character Creation Config", fileName = "CharacterCreationConfig")]
public class CharacterCreationConfig : ScriptableObject
{
    public const string DefaultResourcePath = "Gameobject/ScriptAbleObject/Characters/CharacterCreationConfig";

    [Min(1)] public int maxCharacterSlots = 6;
    [Min(1)] public int nameMinLength = 3;
    [Min(1)] public int nameMaxLength = 20;
    public List<CharacterRace> races = new List<CharacterRace>();

    public CharacterRace GetRace(string raceId)
    {
        if (string.IsNullOrEmpty(raceId)) return null;
        foreach (var r in races)
            if (r != null && r.Id == raceId) return r;
        return null;
    }

    private static CharacterCreationConfig _default;
    public static CharacterCreationConfig LoadDefault()
    {
        if (_default == null) _default = Resources.Load<CharacterCreationConfig>(DefaultResourcePath);
        return _default;
    }
}

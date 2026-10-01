using System.Collections.Generic;
using UnityEngine;

// ความคืบหน้าของตัวละคร (เซฟ/ส่งผ่านเน็ต/เก็บ DB ได้ตรงๆ)
[System.Serializable]
public struct CharacterProgress
{
    public int level;
    public long experience; // EXP สะสมใน level ปัจจุบัน (ไม่ใช่ยอดรวมทั้งหมด)

    public static CharacterProgress New => new CharacterProgress { level = 1, experience = 0 };
}

// ตัวละคร 1 ตัวในหน้าเลือกตัว (id = ownerId ของ inventory / progress / passive)
[System.Serializable]
public struct CharacterInfo
{
    public string id;
    public string name;
    public string raceId;
    public int level;
}

// ข้อมูลตัวละครของผู้เล่น (ตอนนี้ local JSON ภายหลังเป็น backend) กติกาการเก็บเลเวลอยู่ที่ PlayerExperience ฝั่ง server
public interface ICharacterService
{
    // ---------- รายชื่อตัวละครของบัญชี (หน้าเลือกตัว) กติกาชื่อ/จำนวน slot ตรวจที่ CharacterCreation ----------

    // เรียงตามลำดับที่สร้าง
    IReadOnlyList<CharacterInfo> ListCharacters(string accountId);
    bool TryGetCharacter(string characterId, out CharacterInfo info);
    // ฝั่ง server เท่านั้น (ผ่าน CharacterCreation.TryCreate ที่ตรวจแล้ว)
    CharacterInfo CreateCharacter(string accountId, string name, string raceId);
    // ฝั่ง server เท่านั้น: ลบได้เฉพาะตัวละครของบัญชีนั้น
    bool DeleteCharacter(string accountId, string characterId);

    CharacterProgress Load(string ownerId);
    // ฝั่ง server เท่านั้น
    void Save(string ownerId, CharacterProgress progress);

    // passive tree: id ของ node ที่ลงแต้ม (รวมจุด start) แยกจาก level/EXP -> ระบบไหนเซฟก็ไม่ทับกัน
    IReadOnlyList<string> LoadPassives(string ownerId);
    // ฝั่ง server เท่านั้น
    void SavePassives(string ownerId, IReadOnlyList<string> nodeIds);
}

// เก็บในเครื่องเป็น JSON (persistentDataPath/character.json)
public class LocalCharacterService : ICharacterService
{
    [System.Serializable]
    private class Entry
    {
        public string ownerId;
        // ว่าง = ตัวละครที่ไม่ได้สร้างจากหน้าเลือกตัว (DefaultPlayerId) ไม่แสดงในรายชื่อ
        public string accountId;
        public string name;
        public string raceId;
        public CharacterProgress progress;
        public List<string> passives = new List<string>();
    }

    [System.Serializable]
    private class SaveData
    {
        public List<Entry> characters = new List<Entry>();
    }

    private readonly string _fileName;
    private readonly SaveData _data;

    public LocalCharacterService(string fileName = "character.json")
    {
        _fileName = fileName;
        _data = LocalJson.Load<SaveData>(fileName);
    }

    public IReadOnlyList<CharacterInfo> ListCharacters(string accountId)
    {
        var list = new List<CharacterInfo>();
        foreach (var e in _data.characters)
            if (!string.IsNullOrEmpty(e.accountId) && e.accountId == accountId) list.Add(ToInfo(e));
        return list;
    }

    public bool TryGetCharacter(string characterId, out CharacterInfo info)
    {
        var e = _data.characters.Find(c => c.ownerId == characterId);
        info = e != null && !string.IsNullOrEmpty(e.accountId) ? ToInfo(e) : default;
        return e != null && !string.IsNullOrEmpty(e.accountId);
    }

    public CharacterInfo CreateCharacter(string accountId, string name, string raceId)
    {
        var e = new Entry
        {
            ownerId = System.Guid.NewGuid().ToString("N"),
            accountId = accountId,
            name = name,
            raceId = raceId,
            progress = CharacterProgress.New,
        };
        _data.characters.Add(e);
        LocalJson.Save(_fileName, _data);
        return ToInfo(e);
    }

    public bool DeleteCharacter(string accountId, string characterId)
    {
        int removed = _data.characters.RemoveAll(c => c.ownerId == characterId && !string.IsNullOrEmpty(c.accountId) && c.accountId == accountId);
        if (removed > 0) LocalJson.Save(_fileName, _data);
        return removed > 0;
    }

    private static CharacterInfo ToInfo(Entry e) => new CharacterInfo
    {
        id = e.ownerId,
        name = e.name,
        raceId = e.raceId,
        level = Mathf.Max(1, e.progress.level),
    };

    public CharacterProgress Load(string ownerId)
    {
        var e = _data.characters.Find(c => c.ownerId == ownerId);
        return e != null && e.progress.level >= 1 ? e.progress : CharacterProgress.New;
    }

    public IReadOnlyList<string> LoadPassives(string ownerId)
    {
        var e = _data.characters.Find(c => c.ownerId == ownerId);
        return e != null && e.passives != null ? e.passives : (IReadOnlyList<string>)System.Array.Empty<string>();
    }

    public void SavePassives(string ownerId, IReadOnlyList<string> nodeIds)
    {
        var e = _data.characters.Find(c => c.ownerId == ownerId);
        if (e == null)
        {
            e = new Entry { ownerId = ownerId, progress = CharacterProgress.New };
            _data.characters.Add(e);
        }
        e.passives = new List<string>(nodeIds);
        LocalJson.Save(_fileName, _data);
    }

    public void Save(string ownerId, CharacterProgress progress)
    {
        var e = _data.characters.Find(c => c.ownerId == ownerId);
        if (e == null) _data.characters.Add(new Entry { ownerId = ownerId, progress = progress });
        else e.progress = progress;
        LocalJson.Save(_fileName, _data);
    }
}

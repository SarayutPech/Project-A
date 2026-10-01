using System.Collections.Generic;

// ความคืบหน้าของตัวละคร (เซฟ/ส่งผ่านเน็ต/เก็บ DB ได้ตรงๆ)
[System.Serializable]
public struct CharacterProgress
{
    public int level;
    public long experience; // EXP สะสมใน level ปัจจุบัน (ไม่ใช่ยอดรวมทั้งหมด)

    public static CharacterProgress New => new CharacterProgress { level = 1, experience = 0 };
}

// ข้อมูลตัวละครของผู้เล่น (ตอนนี้ local JSON ภายหลังเป็น backend) กติกาการเก็บเลเวลอยู่ที่ PlayerExperience ฝั่ง server
public interface ICharacterService
{
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

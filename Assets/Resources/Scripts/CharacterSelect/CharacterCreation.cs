using System.Collections.Generic;

// กติกาสร้าง/ลบตัวละคร (ฝั่ง server ตัดสิน กฎข้อ 8) UI ส่งแค่ ชื่อ + race id มา
// ภายหลังมี backend: ย้ายไปตรวจที่ backend (ชื่อซ้ำต้องเช็คทั้งเกม ไม่ใช่แค่ในบัญชี)
public static class CharacterCreation
{
    public static bool TryCreate(ICharacterService characters, CharacterCreationConfig config, string accountId,
        string name, string raceId, out CharacterInfo created, out string error)
    {
        created = default;
        name = name != null ? name.Trim() : "";
        error = ValidateName(config, name);
        if (error != null) return false;

        if (config.GetRace(raceId) == null)
        {
            error = "Unknown race";
            return false;
        }

        IReadOnlyList<CharacterInfo> existing = characters.ListCharacters(accountId);
        if (existing.Count >= config.maxCharacterSlots)
        {
            error = "No free character slot";
            return false;
        }
        foreach (var c in existing)
        {
            if (string.Equals(c.name, name, System.StringComparison.OrdinalIgnoreCase))
            {
                error = "That name is already taken";
                return false;
            }
        }

        created = characters.CreateCharacter(accountId, name, raceId);
        return true;
    }

    // null = ใช้ได้ / ตัวอักษร (รวมภาษาไทย) ตัวเลข _ - เท่านั้น ห้ามเว้นวรรค
    public static string ValidateName(CharacterCreationConfig config, string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length < config.nameMinLength) return $"Name needs at least {config.nameMinLength} characters";
        if (name.Length > config.nameMaxLength) return $"Name can be at most {config.nameMaxLength} characters";
        foreach (char ch in name)
            if (!char.IsLetterOrDigit(ch) && ch != '_' && ch != '-' && !IsThaiMark(ch))
                return "Use letters, numbers, _ or - only";
        return null;
    }

    // สระบน/ล่าง + วรรณยุกต์ไทยไม่ใช่ IsLetterOrDigit (เป็น NonSpacingMark)
    private static bool IsThaiMark(char ch) => ch >= '฀' && ch <= '๿';

    // ลบตัวละคร + ของทั้งหมดของมัน (ลบได้เฉพาะตัวของบัญชีนั้น)
    public static bool TryDelete(string accountId, string characterId)
    {
        if (!GameServices.Character.DeleteCharacter(accountId, characterId)) return false;
        GameServices.Inventory.DeleteOwner(characterId);
        GameServices.LootStash.Clear(characterId);
        return true;
    }
}

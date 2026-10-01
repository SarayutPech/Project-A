// ค้นหาชื่อแบบ SQL LIKE: เว้นวรรค = ตัวแทน "อะไรก็ได้" คำต้องเจอตามลำดับ ไม่สนตัวพิมพ์เล็ก/ใหญ่
// เช่น "A ppl" = LIKE '%A%ppl%' -> เจอ "Apple" / "ppl A" ไม่เจอ (ลำดับกลับกัน) / ค่าว่าง = เจอทุกอย่าง
public static class SearchQuery
{
    public static bool Matches(string text, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        if (string.IsNullOrEmpty(text)) return false;

        int from = 0;
        foreach (var token in query.Split(' ', System.StringSplitOptions.RemoveEmptyEntries))
        {
            int at = text.IndexOf(token, from, System.StringComparison.OrdinalIgnoreCase);
            if (at < 0) return false;
            from = at + token.Length;
        }
        return true;
    }
}

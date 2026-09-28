using System.Collections.Generic;
using UnityEngine;

// เลขดาเมจลอยขึ้นเหนือตัวที่โดนตี (presentation อย่างเดียว ไม่มีผลกับ gameplay / server headless ปิดตัวเอง)
// ฟัง Health.AnyDamaged ที่เดียว ไม่ต้องใส่อะไรที่ศัตรู / ใช้ BillboardText แบบ pool (ไม่สร้าง/ลบ object ทุกครั้งที่ตี กฎข้อ 10)
// วางไว้ที่ object ไหนก็ได้ในฉากที่มีการต่อสู้ (1 ตัวต่อฉาก)
public class DamageNumbers : MonoBehaviour
{
    [Header("Who")]
    [Tooltip("แสดงดาเมจที่ศัตรูโดน")]
    public bool showEnemyDamage = true;
    [Tooltip("แสดงดาเมจที่ player โดน")]
    public bool showPlayerDamage = true;

    [Header("Look")]
    public Color enemyDamageColor = new Color(1f, 0.95f, 0.8f);
    [Tooltip("ดาเมจก้อนใหญ่ (≥ Big Hit) สีนี้")]
    public Color bigHitColor = new Color(1f, 0.75f, 0.15f);
    public Color playerDamageColor = new Color(1f, 0.3f, 0.25f);
    [Min(0.1f)] public float fontSize = 16f;
    [Tooltip("ดาเมจเท่านี้ขึ้นไปตัวใหญ่ + สี Big Hit")]
    [Min(0f)] public float bigHitThreshold = 20f;
    [Tooltip("ขยายสูงสุดกี่เท่าสำหรับดาเมจก้อนใหญ่")]
    [Min(1f)] public float bigHitScale = 1.4f;
    [Tooltip("วาดทับทุกอย่าง ไม่โดนต้นไม้/หน้าผาบัง")]
    public bool alwaysOnTop = true;
    [Tooltip("ขนาดบนจอคงที่ตอนกล้องซูมเข้า/ออก")]
    public bool keepScreenSize = true;

    [Header("Motion")]
    [Min(0.1f)] public float lifetime = 0.9f;
    [Tooltip("ลอยขึ้นสูงสุดกี่หน่วย")]
    public float riseHeight = 1.3f;
    [Tooltip("กระจายซ้ายขวาสุ่ม (กันเลขซ้อนกันตอนโดนหลายจังหวะ)")]
    [Min(0f)] public float spread = 0.5f;
    [Tooltip("ช่วงแรกตัวเลขเด้งใหญ่แล้วหดกลับ (สัดส่วนของอายุ)")]
    [Range(0f, 0.5f)] public float popTime = 0.15f;
    [Min(1)] public int poolSize = 40;

    private class Entry
    {
        public BillboardText text;
        public Vector3 start;
        public Vector3 drift;
        public float age;
        public float size;
        public Color color;
        public bool alive;
    }

    private readonly List<Entry> _entries = new List<Entry>();
    private int _cursor;
    // สุ่มของภาพเอง ไม่แตะ UnityEngine.Random ของ gameplay
    private readonly System.Random _rng = new System.Random();

    private void OnEnable()
    {
        if (!Application.isEditor && SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            enabled = false; // server headless
            return;
        }
        Health.AnyDamaged += OnAnyDamaged;
    }

    private void OnDisable()
    {
        Health.AnyDamaged -= OnAnyDamaged;
        foreach (var e in _entries)
        {
            e.alive = false;
            if (e.text != null) e.text.gameObject.SetActive(false);
        }
    }

    private void OnAnyDamaged(Health target, DamageInfo info)
    {
        bool isPlayer = target.team == Team.Player;
        if (isPlayer ? !showPlayerDamage : !showEnemyDamage) return;

        Entry e = Next();
        bool big = !isPlayer && info.amount >= bigHitThreshold;
        float t = bigHitThreshold > 0f ? Mathf.Clamp01(info.amount / (bigHitThreshold * 2f)) : 0f;

        e.size = fontSize * Mathf.Lerp(1f, bigHitScale, t);
        e.start = TopOf(target);
        float angle = (float)(_rng.NextDouble() * Mathf.PI * 2f);
        e.drift = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * spread * (float)_rng.NextDouble();
        e.age = 0f;
        e.alive = true;

        var text = e.text;
        text.transform.position = e.start;
        text.gameObject.SetActive(true);
        text.Text = Mathf.Max(1, Mathf.RoundToInt(info.amount)).ToString();
        if (text.Label != null)
        {
            e.color = isPlayer ? playerDamageColor : big ? bigHitColor : enemyDamageColor;
            text.Label.color = e.color;
            text.Label.fontSize = e.size;
        }
    }

    // ตำแหน่งเหนือหัว: บนสุดของ collider (ศพที่ปิด collider แล้วใช้ความสูงคร่าวๆ)
    private static Vector3 TopOf(Health target)
    {
        var col = target.GetComponent<Collider>();
        if (col != null && col.enabled) return new Vector3(target.transform.position.x, col.bounds.max.y + 0.2f, target.transform.position.z);
        return target.transform.position + Vector3.up * 2f;
    }

    // เอาตัวว่างใน pool / เต็มแล้วเอาตัวเก่าสุด
    private Entry Next()
    {
        foreach (var e in _entries)
            if (!e.alive) return e;
        if (_entries.Count < poolSize) return Create();

        _cursor = (_cursor + 1) % _entries.Count;
        return _entries[_cursor];
    }

    private Entry Create()
    {
        var go = new GameObject("Damage Number");
        go.transform.SetParent(transform, false);
        go.SetActive(false);
        var text = go.AddComponent<BillboardText>();
        text.bob = false;
        text.pulse = false;
        text.bold = true;
        text.offset = Vector3.zero;
        text.outlineWidth = 0.18f;
        text.thickness = 0.25f; // หน้าตัวอักษรหนาขึ้น สีเลขเห็นชัดกว่าขอบ
        text.keepScreenSize = keepScreenSize;
        text.alwaysOnTop = alwaysOnTop;
        text.fontSize = fontSize;
        var e = new Entry { text = text };
        _entries.Add(e);
        return e;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        foreach (var e in _entries)
        {
            if (!e.alive) continue;
            e.age += dt;
            float t = e.age / lifetime;
            if (t >= 1f)
            {
                e.alive = false;
                e.text.gameObject.SetActive(false);
                continue;
            }

            // ลอยขึ้นแบบ ease-out + ไหลออกข้างเล็กน้อย
            float rise = 1f - (1f - t) * (1f - t);
            e.text.transform.position = e.start + Vector3.up * (riseHeight * rise) + e.drift * t;

            var label = e.text.Label;
            if (label == null) continue;
            // เด้งใหญ่ตอนเกิดแล้วหดกลับ / จางช่วง 40% สุดท้าย
            float pop = popTime > 0f && t < popTime ? 1f + 0.5f * (1f - t / popTime) : 1f;
            label.fontSize = e.size * pop;
            // จางด้วย color.a (ไม่ใช้ label.alpha: ตั้งแล้วหน้าตัวอักษรของ material นี้โปร่งจนเห็นแต่ขอบ)
            Color c = e.color;
            c.a = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
            label.color = c;
        }
    }
}

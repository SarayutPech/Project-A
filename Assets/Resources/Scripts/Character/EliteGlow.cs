using UnityEngine;

// เรืองแสงของศัตรู elite (presentation อย่างเดียว server headless ถอดออกได้) วางบน Visual
// อ่าน EnemyRank จาก parent: เป็น elite เมื่อไหร่ -> ต่อ material เรืองแสง (fresnel additive) เป็นชั้นเพิ่มบนทุก renderer
// + ไฟ point light เล็กๆ รอบตัว (เลือกปิดได้) และกะพริบเบาๆ ผ่าน MaterialPropertyBlock (ไม่สร้าง material ใหม่ต่อตัว)
// material ที่เกินจำนวน submesh Unity วาดซ้ำบน submesh สุดท้าย -> ได้ชั้นเรืองทับโมเดลเดิมโดยไม่ต้องแก้ shader หลัก
public class EliteGlow : MonoBehaviour
{
    [Tooltip("material เรืองแสง (เช่น EliteGlow.mat ที่ใช้ shader ProjectA/Afterimage แบบ additive)")]
    public Material glowMaterial;
    [ColorUsage(true, true)] public Color glowColor = new Color(2.5f, 1.6f, 0.3f, 1f);
    [Tooltip("ชื่อ property สีใน glow material")]
    public string colorProperty = "_Color";
    [Tooltip("ความถี่กะพริบ (ครั้ง/วินาที) 0 = ไม่กะพริบ")]
    [Min(0f)] public float pulseSpeed = 1.2f;
    [Range(0f, 1f)] public float pulseAmount = 0.35f;

    [Header("Light")]
    public bool addLight = true;
    [Min(0f)] public float lightRange = 4f;
    [Min(0f)] public float lightIntensity = 2f;

    private EnemyRank _rank;
    private Renderer[] _renderers;
    private Light _light;
    private MaterialPropertyBlock _block;
    private int _colorId;
    private bool _applied;

    private void Awake()
    {
        _rank = GetComponentInParent<EnemyRank>();
        _block = new MaterialPropertyBlock();
        _colorId = Shader.PropertyToID(colorProperty);
    }

    private void OnEnable()
    {
        if (_rank != null) _rank.RankChanged += OnRankChanged;
        Refresh();
    }

    private void OnDisable()
    {
        if (_rank != null) _rank.RankChanged -= OnRankChanged;
    }

    private void OnRankChanged(EnemyRank rank) => Refresh();

    private void Refresh()
    {
        bool elite = _rank != null && _rank.IsElite;
        if (elite == _applied) return;
        _applied = elite;
        if (elite) Apply();
        else Remove();
    }

    private void Apply()
    {
        _renderers = GetComponentsInChildren<Renderer>(true);
        if (glowMaterial != null)
        {
            foreach (var r in _renderers)
            {
                if (r == null || r is ParticleSystemRenderer) continue;
                var mats = r.sharedMaterials;
                if (System.Array.IndexOf(mats, glowMaterial) >= 0) continue;
                System.Array.Resize(ref mats, mats.Length + 1);
                mats[mats.Length - 1] = glowMaterial;
                r.sharedMaterials = mats;
            }
        }

        if (addLight && _light == null)
        {
            var go = new GameObject("Elite Light");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.up * 1f / Mathf.Max(0.01f, transform.lossyScale.y);
            _light = go.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.shadows = LightShadows.None;
            _light.range = lightRange;
            _light.intensity = lightIntensity;
            float max = Mathf.Max(glowColor.maxColorComponent, 0.0001f);
            _light.color = new Color(glowColor.r / max, glowColor.g / max, glowColor.b / max);
        }
        UpdatePulse();
    }

    private void Remove()
    {
        if (_renderers != null && glowMaterial != null)
        {
            foreach (var r in _renderers)
            {
                if (r == null) continue;
                var mats = new System.Collections.Generic.List<Material>(r.sharedMaterials);
                if (mats.Remove(glowMaterial)) r.sharedMaterials = mats.ToArray();
            }
        }
        if (_light != null) Destroy(_light.gameObject);
        _light = null;
    }

    private void LateUpdate()
    {
        if (_applied && pulseSpeed > 0f) UpdatePulse();
    }

    private void UpdatePulse()
    {
        float pulse = pulseSpeed > 0f ? 1f - pulseAmount * (0.5f + 0.5f * Mathf.Sin(Time.time * pulseSpeed * Mathf.PI * 2f)) : 1f;
        Color c = glowColor * pulse;
        c.a = glowColor.a;

        if (_renderers != null)
        {
            foreach (var r in _renderers)
            {
                if (r == null || r is ParticleSystemRenderer) continue;
                r.GetPropertyBlock(_block);
                _block.SetColor(_colorId, c);
                r.SetPropertyBlock(_block);
            }
        }
        if (_light != null) _light.intensity = lightIntensity * pulse;
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// object ที่บังระหว่างกล้องกับ player จะค่อยๆ fade โปร่งแสงลง (ปรับความโปร่งได้) + มีเส้นขอบ
// พ้นแล้วค่อยๆ fade กลับทึบแล้วคืน material เดิม เช็คด้วย bounds ของ renderer (ไม่ต้องมี collider)
//
// การ fade ต้องใช้ material ที่รองรับ transparent (มี property _Surface):
//   - URP Lit / Simple Lit / Unlit
//   - Shader Graph ที่เปิด Graph Settings > Allow Material Override และต่อ Alpha block
//     (ถ้ามี property reference "_FadeAlpha" ใน graph จะถูกตั้งค่าความโปร่งให้)
// material ที่ไม่รองรับจะใช้ material เส้นขอบล้วน (Custom/OcclusionOutline) แทน ข้างในโปร่งใสทั้งหมด
//
// ผู้สมัครเป็นตัวบัง: object ที่ generator scatter ไว้ (แต่ละ child ของ Scattered Objects = 1 ชิ้น)
// + รายการ Extra Occluders ที่ลากใส่เอง
// หมายเหตุ: ถ้าเปิด Combine Scatter Meshes ไว้ จะ fade ทั้ง batch ก้อนใหญ่
[RequireComponent(typeof(Camera))]
public class OcclusionOutlineController : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("ถ้าเว้นว่างจะใช้ PlayerMovement.Instance")]
    public Transform target;
    [Tooltip("ความหนาของเส้นเช็คระหว่างกล้องกับ player (world unit) กันขอบ object เฉียดบังแล้วไม่ติด")]
    [Min(0f)] public float checkRadius = 0.3f;

    [Header("Occluders")]
    [Tooltip("ถ้าเว้นว่างจะใช้ ProceduralMapGenerator.Instance")]
    public ProceduralMapGenerator generator;
    [Tooltip("ใช้ object ที่ generator scatter ไว้ (ต้นไม้ ก้อนหิน ฯลฯ) เป็นตัวบัง")]
    public bool includeScatterObjects = true;
    [Tooltip("object เพิ่มเติมที่ให้ fade ได้ตอนบัง (ทั้ง hierarchy ของแต่ละตัวนับเป็น 1 ชิ้น)")]
    public List<Transform> extraOccluders = new List<Transform>();
    [Tooltip("สูงไม่ถึงนี้ไม่นับเป็นตัวบัง (world unit) ของเตี้ยๆ อย่างก้อนหินเล็กจะไม่ถูก fade")]
    [Min(0f)] public float minOccluderHeight = 1.5f;

    [Header("Fade")]
    [Tooltip("ความทึบตอนบัง 0 = หายเหลือแต่เส้นขอบ, 1 = ทึบเหมือนเดิม")]
    [Range(0f, 1f)] public float occludedOpacity = 0.3f;
    [Tooltip("เวลาที่ใช้ fade จางลงตอนเริ่มบัง (วินาที)")]
    [Min(0f)] public float fadeOutDuration = 0.25f;
    [Tooltip("เวลาที่ใช้ fade กลับทึบตอนพ้น (วินาที)")]
    [Min(0f)] public float fadeInDuration = 0.4f;

    [Header("Outline")]
    public bool showOutline = true;
    [Tooltip("ถ้าเว้นว่างจะสร้างจาก shader Custom/OcclusionOutline ให้อัตโนมัติ")]
    public Material outlineMaterial;
    [Tooltip("alpha ของสีนี้ = ความทึบเส้นขอบตอน fade เต็มที่")]
    public Color outlineColor = new Color(1f, 1f, 1f, 0.9f);
    [Range(0f, 10f)] public float outlineWidth = 2f;

    private class Occluder
    {
        public Transform root;
        public Renderer[] renderers;
        public Material[][] originalMaterials; // null = ยังใช้ material เดิมอยู่
        public bool[][] fadeSlots;             // slot ไหนเป็น material transparent ที่ปรับ alpha ได้
        public float fade;                     // 0 = ปกติ, 1 = จางเต็มที่
        public bool occluded;
    }

    private const string OutlineShaderName = "Custom/OcclusionOutline";
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int FadeAlphaId = Shader.PropertyToID("_FadeAlpha");
    private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
    private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
    private static readonly int SurfaceId = Shader.PropertyToID("_Surface");

    private Camera _camera;
    private Material _outline;
    private readonly List<Occluder> _occluders = new List<Occluder>();
    private readonly List<Occluder> _animating = new List<Occluder>(); // fade > 0 หรือกำลังบัง
    private readonly Dictionary<Material, Material> _fadeClones = new Dictionary<Material, Material>(); // null = ไม่รองรับ transparent
    private readonly Vector3[] _samplePoints = new Vector3[3];
    private MaterialPropertyBlock _block;
    private ProceduralMapGenerator _subscribedGenerator;

    private ProceduralMapGenerator Generator => generator != null ? generator : ProceduralMapGenerator.Instance;

    private void Awake()
    {
        _camera = GetComponent<Camera>();
        _block = new MaterialPropertyBlock();
    }

    private void OnEnable()
    {
        _subscribedGenerator = Generator;
        if (_subscribedGenerator != null) _subscribedGenerator.MapGenerated += RefreshOccluders;
        RefreshOccluders();
    }

    private void OnDisable()
    {
        if (_subscribedGenerator != null) _subscribedGenerator.MapGenerated -= RefreshOccluders;
        _subscribedGenerator = null;
        RestoreAll();
    }

    private void OnDestroy()
    {
        // material ที่สร้างเองตอน runtime ต้องลบเอง
        foreach (var clone in _fadeClones.Values)
            if (clone != null) Destroy(clone);
        _fadeClones.Clear();
        if (_outline != null && _outline != outlineMaterial) Destroy(_outline);
    }

    // เก็บรายชื่อตัวบังใหม่ (เรียกเองได้ถ้าเพิ่ม/ลบ object ตอน runtime)
    [ContextMenu("Refresh Occluders")]
    public void RefreshOccluders()
    {
        RestoreAll();
        _occluders.Clear();

        var gen = Generator;
        if (includeScatterObjects && gen != null && gen.ScatterContainer != null)
            foreach (Transform child in gen.ScatterContainer) AddOccluder(child);

        foreach (var extra in extraOccluders)
            if (extra != null) AddOccluder(extra);
    }

    private void AddOccluder(Transform root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return;
        _occluders.Add(new Occluder { root = root, renderers = renderers });
    }

    private void LateUpdate()
    {
        Transform t = target != null ? target : (PlayerMovement.Instance != null ? PlayerMovement.Instance.transform : null);
        bool hasTarget = t != null && t.gameObject.activeInHierarchy;
        if (hasTarget) FillSamplePoints(t);

        // หาว่าชิ้นไหนบังอยู่ตอนนี้
        foreach (var occ in _occluders)
        {
            bool occluded = false;
            if (hasTarget && occ.root != null && !t.IsChildOf(occ.root) &&
                TryGetBounds(occ, out Bounds bounds) && bounds.size.y >= minOccluderHeight)
            {
                bounds.Expand(checkRadius * 2f);
                occluded = BlocksAnySample(bounds);
            }

            occ.occluded = occluded;
            if (occluded && occ.fade <= 0f && !_animating.Contains(occ)) _animating.Add(occ);
        }

        UpdateOutlineLook();

        // เดิน fade ของชิ้นที่กำลังบัง/กำลังคืนสภาพ
        for (int i = _animating.Count - 1; i >= 0; i--)
        {
            Occluder occ = _animating[i];
            if (occ.root == null)
            {
                _animating.RemoveAt(i);
                continue;
            }

            float duration = occ.occluded ? fadeOutDuration : fadeInDuration;
            float step = duration > 0f ? Time.deltaTime / duration : 1f;
            occ.fade = Mathf.MoveTowards(occ.fade, occ.occluded ? 1f : 0f, step);

            if (occ.fade > 0f)
            {
                if (occ.originalMaterials == null) ApplyFadeMaterials(occ);
                ApplyFadeAmount(occ);
            }
            else
            {
                Restore(occ);
                _animating.RemoveAt(i);
            }
        }
    }

    // ---------- Occlusion Check ----------

    // จุดเช็คบนตัว player: เท้า กลางตัว หัว (บังแค่ส่วนไหนก็นับว่าบัง)
    private void FillSamplePoints(Transform t)
    {
        var col = t.GetComponent<Collider>();
        Bounds b = col != null ? col.bounds : new Bounds(t.position + Vector3.up, new Vector3(0.5f, 2f, 0.5f));

        _samplePoints[0] = new Vector3(b.center.x, b.min.y + b.extents.y * 0.25f, b.center.z);
        _samplePoints[1] = b.center;
        _samplePoints[2] = new Vector3(b.center.x, b.max.y - b.extents.y * 0.1f, b.center.z);
    }

    private bool BlocksAnySample(Bounds bounds)
    {
        Vector3 camPos = transform.position;
        Vector3 forward = transform.forward;

        foreach (var p in _samplePoints)
        {
            // ortho: เส้นขนานกับทิศกล้อง เริ่มที่ระนาบกล้อง / perspective: เริ่มที่ตัวกล้อง
            Vector3 origin = _camera.orthographic ? p - forward * Vector3.Dot(p - camPos, forward) : camPos;
            Vector3 toPoint = p - origin;
            float distance = toPoint.magnitude;
            if (distance < 0.001f) continue;

            var ray = new Ray(origin, toPoint / distance);
            if (bounds.IntersectRay(ray, out float hitDistance) && hitDistance < distance) return true;
        }
        return false;
    }

    private static bool TryGetBounds(Occluder occ, out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        foreach (var r in occ.renderers)
        {
            // renderer ที่ถูกปิด (เช่นต้นฉบับหลัง combine mesh / ยังไม่ถึงคิว animation) ไม่นับ
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (!any) { bounds = r.bounds; any = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return any;
    }

    // ---------- Material Swap ----------

    private void ApplyFadeMaterials(Occluder occ)
    {
        Material outline = GetOutlineMaterial();

        occ.originalMaterials = new Material[occ.renderers.Length][];
        occ.fadeSlots = new bool[occ.renderers.Length][];

        for (int i = 0; i < occ.renderers.Length; i++)
        {
            Renderer r = occ.renderers[i];
            if (r == null) continue;

            Material[] original = r.sharedMaterials;
            occ.originalMaterials[i] = original;

            var replaced = new List<Material>(original.Length + 1);
            var fadeSlots = new bool[original.Length];
            bool anyFade = false;

            for (int m = 0; m < original.Length; m++)
            {
                Material clone = GetFadeClone(original[m]);
                fadeSlots[m] = clone != null;
                anyFade |= clone != null;
                // ทำ transparent ไม่ได้ -> เหลือแต่เส้นขอบ (ข้างในโปร่งใส)
                replaced.Add(clone != null ? clone : outline);
            }

            // material เกินจำนวน submesh จะถูกวาดซ้ำบน submesh สุดท้าย ใช้เป็นเส้นขอบเพิ่มได้
            if (showOutline && anyFade && outline != null)
            {
                replaced.Add(outline);
            }

            occ.fadeSlots[i] = fadeSlots;
            r.sharedMaterials = replaced.ToArray();
        }
    }

    // ตั้ง alpha ต่อชิ้นผ่าน MaterialPropertyBlock (material clone ใช้ร่วมกันหลายชิ้นได้ แต่ fade ไม่เท่ากัน)
    private void ApplyFadeAmount(Occluder occ)
    {
        float t = Mathf.SmoothStep(0f, 1f, occ.fade);
        float opacity = Mathf.Lerp(1f, occludedOpacity, t);
        Color outlineTint = outlineColor;
        outlineTint.a *= showOutline ? t : 0f;

        for (int i = 0; i < occ.renderers.Length; i++)
        {
            Renderer r = occ.renderers[i];
            if (r == null || occ.fadeSlots[i] == null) continue;

            Material[] mats = r.sharedMaterials;
            for (int m = 0; m < mats.Length; m++)
            {
                Material mat = mats[m];
                if (mat == null) continue;

                _block.Clear();
                bool isFadeSlot = m < occ.fadeSlots[i].Length && occ.fadeSlots[i][m];
                if (isFadeSlot)
                {
                    if (mat.HasProperty(BaseColorId))
                    {
                        Color c = mat.GetColor(BaseColorId);
                        c.a *= opacity;
                        _block.SetColor(BaseColorId, c);
                    }
                    if (mat.HasProperty(FadeAlphaId)) _block.SetFloat(FadeAlphaId, opacity);
                }
                else
                {
                    _block.SetColor(OutlineColorId, outlineTint); // slot เส้นขอบ (ทั้งแบบเสริมและแบบแทนที่)
                }
                r.SetPropertyBlock(_block, m);
            }
        }
    }

    private void Restore(Occluder occ)
    {
        if (occ.originalMaterials == null) return;

        _block.Clear();
        for (int i = 0; i < occ.renderers.Length; i++)
        {
            Renderer r = occ.renderers[i];
            if (r == null) continue;

            int count = r.sharedMaterials.Length;
            for (int m = 0; m < count; m++) r.SetPropertyBlock(_block, m); // block ว่าง = ล้างค่าที่ตั้งไว้
            if (occ.originalMaterials[i] != null) r.sharedMaterials = occ.originalMaterials[i];
        }

        occ.originalMaterials = null;
        occ.fadeSlots = null;
        occ.fade = 0f;
    }

    private void RestoreAll()
    {
        foreach (var occ in _animating) Restore(occ);
        _animating.Clear();
    }

    // clone แบบ transparent ของ material เดิม (cache ไว้ใช้ร่วมกัน) คืน null ถ้า shader ไม่รองรับ
    private Material GetFadeClone(Material source)
    {
        if (source == null) return null;
        if (_fadeClones.TryGetValue(source, out var cached)) return cached;

        Material clone = null;
        if (source.HasProperty(SurfaceId))
        {
            clone = new Material(source) { name = source.name + " (Fade)" };
            MakeTransparent(clone);
        }

        _fadeClones[source] = clone;
        return clone;
    }

    // ตั้งค่าเหมือนเลือก Surface Type = Transparent, Blending = Alpha ใน Inspector ของ URP
    private static void MakeTransparent(Material mat)
    {
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        if (mat.HasProperty("_SrcBlendAlpha")) mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        if (mat.HasProperty("_DstBlendAlpha")) mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.DisableKeyword("_ALPHAMODULATE_ON");
        mat.renderQueue = (int)RenderQueue.Transparent;
    }

    private Material GetOutlineMaterial()
    {
        if (_outline != null) return _outline;

        if (outlineMaterial != null)
        {
            _outline = outlineMaterial;
            return _outline;
        }

        // shader อยู่ใน Assets/Resources/Shaders จึงถูกรวมตอน Build และ Shader.Find หาเจอ
        Shader shader = Shader.Find(OutlineShaderName);
        if (shader == null)
        {
            Debug.LogWarning($"[{nameof(OcclusionOutlineController)}] หา shader '{OutlineShaderName}' ไม่เจอ", this);
            return null;
        }

        _outline = new Material(shader) { name = "Occlusion Outline (auto)" };
        return _outline;
    }

    // ความกว้างเส้นปรับใน Inspector ได้ระหว่าง Play (สีต่อชิ้นตั้งผ่าน property block ตาม fade)
    private void UpdateOutlineLook()
    {
        Material mat = GetOutlineMaterial();
        if (mat == null || mat == outlineMaterial) return;
        mat.SetFloat(OutlineWidthId, outlineWidth);
    }
}

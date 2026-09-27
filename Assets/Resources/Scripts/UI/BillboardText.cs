using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

// ข้อความลอยในโลกแบบ billboard (หันหน้าเข้ากล้องตลอด) เช่นชื่อ NPC / ป้าย / ตัวเลขดาเมจ
// ใส่ที่ object ไหนก็ได้ จะสร้างลูก TextMeshPro ให้เอง (ปรับฟอนต์/สไตล์เพิ่มที่ลูกได้)
// presentation อย่างเดียว: ไม่มีผลกับ gameplay / server headless ปิดตัวเอง
// เปลี่ยนข้อความจากโค้ด: GetComponent<BillboardText>().Text = "..."
[ExecuteAlways]
[DisallowMultipleComponent]
public class BillboardText : MonoBehaviour
{
    public enum FacingMode
    {
        [Tooltip("หันขนานกับจอเสมอ (เหมาะกับกล้อง isometric)")] FaceCamera,
        [Tooltip("หมุนตามกล้องแค่แกน Y ข้อความตั้งตรงเสมอ")] YAxisOnly,
    }

    [TextArea(2, 6)]
    [Tooltip("รองรับ rich text เช่น <b>ตัวหนา</b> <color=#ff0>สี</color> <size=150%>ขนาด</size>")]
    [SerializeField] private string text = "Text";

    [Header("Style")]
    [Tooltip("ฟอนต์ (เว้นว่าง = ฟอนต์ตั้งต้นของ TMP) ภาษาไทยต้องใช้ฟอนต์ที่มีอักษรไทย")]
    public TMP_FontAsset font;
    [Tooltip("material ของฟอนต์ ต้องเปิด Outline (keyword OUTLINE_ON) ถึงจะมีขอบ / เว้นว่าง = BillboardText SDF (ใช้กับฟอนต์ตั้งต้น)")]
    public Material textMaterial;
    [Tooltip("ขนาดตัวอักษร")]
    [Min(0.01f)] public float fontSize = 3f;
    public bool bold = false;
    [Tooltip("ความหนาเส้นตัวอักษร (0 = ปกติ, บวก = หนาขึ้น, ลบ = บางลง)")]
    [Range(-1f, 1f)] public float thickness = 0f;
    public Color color = Color.white;
    [Range(0f, 1f)] public float outlineWidth = 0.2f;
    public Color outlineColor = Color.black;
    public TextAlignmentOptions alignment = TextAlignmentOptions.Center;

    [Header("Animation")]
    [Tooltip("ลอยขึ้นลง")]
    public bool bob = true;
    [Tooltip("ลอยขึ้นลงได้สูงสุดกี่หน่วย")]
    [Min(0f)] public float bobHeight = 0.15f;
    [Tooltip("รอบต่อวินาที")]
    [Min(0f)] public float bobSpeed = 0.8f;
    [Tooltip("ขยาย/ย่อเป็นจังหวะ")]
    public bool pulse = true;
    [Tooltip("ขยาย/ย่อกี่ส่วน (0.08 = ±8%)")]
    [Range(0f, 0.5f)] public float pulseAmount = 0.08f;
    [Tooltip("รอบต่อวินาที")]
    [Min(0f)] public float pulseSpeed = 1.2f;
    [Tooltip("เริ่มจังหวะไม่พร้อมกัน (ป้ายหลายอันจะไม่ขยับตรงกัน)")]
    public bool randomPhase = true;

    [Header("Placement")]
    [Tooltip("ระยะจากจุดของ object (world) เช่นยกขึ้นเหนือหัว")]
    public Vector3 offset = new Vector3(0f, 2.2f, 0f);
    public FacingMode facing = FacingMode.FaceCamera;
    [Tooltip("ขนาดบนจอคงที่ตอนกล้องซูมเข้า/ออก")]
    public bool keepScreenSize = false;
    [Tooltip("ขนาดกล้องที่ข้อความมีขนาดตาม Font Size พอดี (ortho = orthographicSize / perspective = ระยะห่าง)")]
    [Min(0.01f)] public float referenceCameraSize = 10f;

    [Header("Visibility")]
    [Tooltip("แสดงเป็นลำดับแรก: วาดทับทุกอย่าง ไม่โดนกำแพง/ต้นไม้บัง (ใช้ material BillboardText SDF Overlay)\nถ้าใส่ Text Material เอง material นั้นต้องตั้ง ZTest = Always เอง")]
    public bool alwaysOnTop = false;
    [Tooltip("ซ่อนเมื่อกล้องไกลเกินนี้ (0 = แสดงตลอด)")]
    [Min(0f)] public float maxDistance = 0f;
    [Tooltip("ถ้าเว้นว่างจะใช้ Camera.main")]
    public Camera targetCamera;

    [SerializeField, HideInInspector] private TextMeshPro label;
    private float _phase;
    private MaterialPropertyBlock _block;

    public string Text
    {
        get => text;
        set
        {
            text = value;
            if (label != null) label.text = value;
        }
    }

    public TextMeshPro Label => label;

    private void OnEnable()
    {
        if (!Application.isEditor && SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
        {
            enabled = false;
            return;
        }
        // สุ่มจังหวะเริ่มจากตำแหน่ง (ไม่ใช้ UnityEngine.Random จะได้ไม่กระทบ random ของ gameplay)
        Vector3 p = transform.position;
        _phase = randomPhase ? Mathf.Abs(Mathf.Sin(p.x * 12.9898f + p.z * 78.233f) * 43758.5453f) % 1f : 0f;

        EnsureLabel();
        ApplyStyle();
        if (label != null) label.gameObject.SetActive(true);
    }

    private void OnDisable()
    {
        if (label != null) label.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        // label เป็นลูกที่ component นี้สร้าง -> ลบไปพร้อมกัน
        if (label == null) return;
        if (Application.isPlaying) Destroy(label.gameObject);
        else DestroyImmediate(label.gameObject);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // OnValidate แก้ material/สร้าง object ไม่ได้ -> เลื่อนไปทำหลัง inspector แก้ค่าเสร็จ
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null && label != null) ApplyStyle();
        };
    }
#endif

    private void EnsureLabel()
    {
        if (label != null) return;
        label = GetComponentInChildren<TextMeshPro>(true);
        if (label != null) return;

        var obj = new GameObject("Billboard Label");
        obj.transform.SetParent(transform, false);
        obj.layer = gameObject.layer;
        label = obj.AddComponent<TextMeshPro>();
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.rectTransform.sizeDelta = new Vector2(20f, 5f);
    }

    private const string DefaultMaterialPath = "BillboardText/BillboardText SDF";
    private const string OverlayMaterialPath = "BillboardText/BillboardText SDF Overlay";

    public void ApplyStyle()
    {
        if (label == null) return;
        if (font != null && label.font != font) label.font = font;

        // material ตั้งต้นของฟอนต์ (Mobile SDF) ไม่เปิด outline -> ใช้ preset ที่เปิดไว้แล้ว ถ้าเป็น atlas เดียวกัน
        Material mat = textMaterial;
        if (mat == null)
        {
            var preset = Resources.Load<Material>(alwaysOnTop ? OverlayMaterialPath : DefaultMaterialPath);
            if (preset != null && label.font != null && preset.GetTexture(ShaderUtilities.ID_MainTex) == label.font.atlasTexture) mat = preset;
        }
        if (mat != null && label.fontSharedMaterial != mat) label.fontSharedMaterial = mat;

        label.text = text;
        label.fontSize = fontSize;
        label.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        label.color = color;
        label.alignment = alignment;
        // outline / ความหนา อยู่ที่ material -> TMP สร้าง material instance ของ label นี้ให้เอง (ลบให้เองตอน label ถูกทำลาย)
        // outline / ความหนา ตั้งผ่าน MaterialPropertyBlock -> ใช้ material ของฟอนต์ร่วมกัน ไม่สร้าง material instance ต่อป้าย
        // extraPadding กันขอบตัวอักษรที่หนาขึ้น/มี outline โดนตัด (TMP คำนวณ padding จาก material กลางซึ่งไม่รู้ค่าใน block)
        label.extraPadding = true;
        var renderer = label.renderer;
        if (renderer == null) return;
        _block ??= new MaterialPropertyBlock();
        renderer.GetPropertyBlock(_block);
        _block.SetFloat(ShaderUtilities.ID_FaceDilate, thickness);
        _block.SetFloat(ShaderUtilities.ID_OutlineWidth, outlineWidth);
        _block.SetColor(ShaderUtilities.ID_OutlineColor, outlineColor);
        renderer.SetPropertyBlock(_block);
    }

    private void LateUpdate()
    {
        if (label == null) return;

        Camera cam = targetCamera != null ? targetCamera : Camera.main;
        Transform lt = label.transform;

        // ลอยขึ้นลง / ขยายย่อ ตามเวลา (sin ไป-กลับนุ่มๆ)
        float t = Application.isPlaying ? Time.time : 0f;
        float bobOffset = bob ? Mathf.Sin((t * bobSpeed + _phase) * Mathf.PI * 2f) * bobHeight : 0f;
        float pulseScale = pulse ? 1f + Mathf.Sin((t * pulseSpeed + _phase * 0.37f) * Mathf.PI * 2f) * pulseAmount : 1f;

        lt.position = transform.position + offset + Vector3.up * bobOffset;
        if (cam == null) return;

        Transform ct = cam.transform;
        float distance = Vector3.Distance(ct.position, lt.position);
        bool visible = maxDistance <= 0f || distance <= maxDistance;
        if (label.enabled != visible) label.enabled = visible;
        if (!visible) return;

        lt.rotation = facing == FacingMode.FaceCamera
            ? ct.rotation
            : Quaternion.Euler(0f, ct.eulerAngles.y, 0f);

        float scale = pulseScale;
        if (keepScreenSize)
            scale *= (cam.orthographic ? cam.orthographicSize : distance) / referenceCameraSize;
        // ชดเชย scale ของ parent -> ขนาดข้อความไม่ขึ้นกับขนาด object
        Vector3 parentScale = transform.lossyScale;
        lt.localScale = new Vector3(
            scale / Mathf.Max(Mathf.Abs(parentScale.x), 1e-4f),
            scale / Mathf.Max(Mathf.Abs(parentScale.y), 1e-4f),
            scale / Mathf.Max(Mathf.Abs(parentScale.z), 1e-4f));
    }
}

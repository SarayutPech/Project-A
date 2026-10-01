using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// minimap มุมขวาบน + แผนที่เต็มจอแบบ overlay (กด Tab) (presentation ล้วน ฝั่ง client เท่านั้น)
// ภาพแผนที่ = ความสูงของพื้นแบ่งเป็นชั้น ขาว (สูง) -> ดำ (ต่ำ) มีเส้นขอบตรงรอยต่อชั้น / ไม่มีพื้น = โปร่งใส
//   สร้างครั้งเดียวหลังโหลด scene / map สร้างเสร็จ (รอ MapBuildAnimator เล่นจบ): ยิง raycast ลงจากฟ้าทีละจุดใน PhysicsScene ของแผนที่
//   (ทยอยทำหลายเฟรมไม่ให้กระตุก) ไม่อ้าง renderer -> ได้ทั้ง map สุ่มและ Hideout / ของที่ map วาง (ต้นไม้ หิน) ขึ้นเป็นจุดสูง
//   ไม่นับ collider ของตัวละคร (Health) และ trigger
// ภาพเก็บตามแกนโลก (x,z) แล้วหมุนทั้งแผ่นตามมุมกล้องเกม (บน = ทิศที่กล้องหัน) กล้องหมุนก็ไม่ต้องสร้างใหม่
// ไอคอนที่ขยับทุกเฟรม: ผู้เล่น (ลูกศร) / มอนสเตอร์ (elite ใหญ่กว่า) / object ที่คลิกได้ (portal, กล่องของ)
// เลื่อนล้อเมาส์บน minimap = ซูม / slider เล็กด้านขวาของ minimap = ความทึบของ overlay (จำค่าไว้ในเครื่อง)
public class MinimapView : UISingleton<MinimapView>, IPlayerUI, IScrollHandler
{
    // จอแสดงแผนที่ 1 จอ (minimap หรือ overlay)
    [System.Serializable]
    public class Display
    {
        [Tooltip("กรอบที่ตัดภาพ (Mask / RectMask2D) และใช้วัดขนาด")]
        public RectTransform viewport;
        [Tooltip("ตัวหมุนตามมุมกล้อง (ลูกของ viewport, anchor กลาง)")]
        public RectTransform rotator;
        [Tooltip("ภาพแผนที่ (ลูกของ rotator) ไอคอนเป็นลูกของมัน")]
        public RawImage mapImage;
        [Tooltip("ลูกศรผู้เล่น (ชี้ขึ้น) follow = อยู่กลาง viewport / fit = ถูกย้ายไปเป็นลูกของ mapImage")]
        public RectTransform playerIcon;
        [Tooltip("true = เลื่อนตามผู้เล่น (minimap) / false = แสดงทั้งแผนที่พอดีกรอบ (overlay)")]
        public bool followPlayer = true;
        [Tooltip("ความทึบ/การแสดงผลทั้งจอ (overlay) เว้นว่างได้")]
        public CanvasGroup group;

        [System.NonSerialized] public readonly List<Image> enemyIcons = new List<Image>();
        [System.NonSerialized] public readonly List<Image> eliteIcons = new List<Image>();
        [System.NonSerialized] public readonly List<Image> interactIcons = new List<Image>();
    }

    [Header("Displays")]
    public Display minimap = new Display { followPlayer = true };
    public Display overlay = new Display { followPlayer = false };
    [Tooltip("ซ่อน minimap มุมจอตอนเปิด overlay")]
    public bool hideMinimapWhileOverlay = true;

    [Header("Icons (ต้นแบบ ปิดไว้)")]
    public Image enemyIconTemplate;
    public Image eliteIconTemplate;
    public Image interactableIconTemplate;
    [Tooltip("ชื่อพื้นที่ (ชื่อ scene) เว้นว่างได้")]
    public TextMeshProUGUI areaLabel;

    [Header("Overlay Opacity")]
    [Tooltip("slider ปรับความทึบ overlay (ข้าง minimap)")]
    public Slider opacitySlider;
    [Range(0.05f, 1f)] public float overlayOpacity = 0.6f;

    [Header("View")]
    [Tooltip("ครึ่งความกว้างของพื้นที่ที่เห็นใน minimap (world unit)")]
    [Min(5f)] public float viewRadius = 30f;
    public Vector2 zoomRange = new Vector2(12f, 120f);
    [Min(0.1f)] public float zoomStep = 4f;
    [Tooltip("overlay ใช้กี่ส่วนของกรอบ")]
    [Range(0.3f, 1f)] public float overlayFill = 0.92f;

    [Header("Height Layers")]
    [Tooltip("ความสูงต่อ 1 ชั้นสี (map สุ่มที่เปิด terraces ใช้ความสูงชั้นของ config แทน)")]
    [Min(0.1f)] public float layerHeight = 0.75f;
    public Color lowColor = new Color(0.1f, 0.1f, 0.1f, 1f);
    public Color highColor = new Color(0.95f, 0.95f, 0.95f, 1f);
    [Tooltip("ตัวคูณสีตรงขอบที่ชั้นเปลี่ยน (เส้นหน้าผา)")]
    [Range(0f, 1f)] public float edgeShade = 0.55f;
    [Tooltip("ขนาดจุดเล็กสุด (world unit ต่อ pixel)")]
    [Min(0.1f)] public float minCellSize = 0.5f;
    [Tooltip("ด้านยาวสุดของภาพ (px)")]
    [Min(64)] public int maxTextureSize = 512;
    [Tooltip("layer ที่นับเป็นพื้น/สิ่งของ")]
    public LayerMask groundLayers = ~(1 << 5); // ทุก layer ยกเว้น UI
    [Tooltip("ยิง raycast ได้กี่ครั้งต่อเฟรม (ทยอยสร้างไม่ให้กระตุก)")]
    [Min(500)] public int raysPerFrame = 20000;
    [Tooltip("แผนที่ที่ไม่ใช่ map สุ่ม (Hideout): จำกัดขนาดพื้นที่ (world unit)")]
    [Min(10f)] public float maxFallbackSize = 300f;
    [Tooltip("Hideout: collider ที่กว้างเกินนี้ถือเป็นพื้นผืนใหญ่ ไม่นับตอนหาขอบเขตแผนที่")]
    [Min(10f)] public float fallbackFloorSize = 60f;
    [Tooltip("Hideout: ขอบเพิ่มรอบพื้นที่ที่มีของวาง (world unit)")]
    [Min(0f)] public float fallbackMargin = 15f;

    private const string OpacityPrefKey = "MapOverlayOpacity";

    private Transform _player;
    private Texture2D _texture;
    private Coroutine _buildRoutine;
    private ProceduralMapGenerator _generator;
    private bool _overlayOpen;

    // กรอบของภาพ (แกนโลก): uv = (x - minX) / width, (z - minZ) / height
    private bool _hasMap;
    private float _minX, _minZ, _width, _height;

    private readonly RaycastHit[] _hits = new RaycastHit[8];
    private readonly Dictionary<Collider, bool> _ignoreCache = new Dictionary<Collider, bool>();

    public bool IsOverlayOpen => _overlayOpen;

    protected override void Awake()
    {
        base.Awake();
        if (!IsInstance) return;
        foreach (var t in new[] { enemyIconTemplate, eliteIconTemplate, interactableIconTemplate })
            if (t != null) t.gameObject.SetActive(false);
        foreach (var d in new[] { minimap, overlay })
            if (d.mapImage != null) d.mapImage.enabled = false;

        // overlay ไม่รับคลิก (คลิกทะลุไปเล่นเกมได้)
        if (overlay.group != null)
        {
            overlay.group.blocksRaycasts = false;
            overlay.group.interactable = false;
        }
        // ลูกศรผู้เล่นของ overlay ขยับตามตำแหน่งบนแผนที่
        if (overlay.playerIcon != null && overlay.mapImage != null) overlay.playerIcon.SetParent(overlay.mapImage.rectTransform, false);

        try { overlayOpacity = PlayerPrefs.GetFloat(OpacityPrefKey, overlayOpacity); } catch { }
        if (opacitySlider != null)
        {
            opacitySlider.minValue = 0.05f;
            opacitySlider.maxValue = 1f;
            opacitySlider.SetValueWithoutNotify(overlayOpacity);
            opacitySlider.onValueChanged.AddListener(SetOverlayOpacity);
        }
        SetOverlay(false);
    }

    private void OnEnable() => GameUI.SceneChanged += RequestRebuild;

    private void OnDisable()
    {
        GameUI.SceneChanged -= RequestRebuild;
        SetGenerator(null);
    }

    protected override void OnDestroy()
    {
        if (opacitySlider != null) opacitySlider.onValueChanged.RemoveListener(SetOverlayOpacity);
        ReleaseTexture();
        base.OnDestroy();
    }

    public void Bind(GameObject player)
    {
        _player = player != null ? player.transform : null;
        if (_player != null) RequestRebuild();
    }

    // ---------- Overlay ----------

    public void ToggleOverlay() => SetOverlay(!_overlayOpen);

    public void SetOverlay(bool open)
    {
        _overlayOpen = open;
        if (overlay.group != null) overlay.group.alpha = open ? overlayOpacity : 0f;
        else if (overlay.viewport != null) overlay.viewport.gameObject.SetActive(open);
        if (minimap.group != null) minimap.group.alpha = open && hideMinimapWhileOverlay ? 0f : 1f;
    }

    public void SetOverlayOpacity(float value)
    {
        overlayOpacity = Mathf.Clamp(value, 0.05f, 1f);
        if (_overlayOpen && overlay.group != null) overlay.group.alpha = overlayOpacity;
        try { PlayerPrefs.SetFloat(OpacityPrefKey, overlayOpacity); } catch { }
    }

    public void OnScroll(PointerEventData eventData)
    {
        viewRadius = Mathf.Clamp(viewRadius - eventData.scrollDelta.y * zoomStep, zoomRange.x, zoomRange.y);
    }

    // ---------- สร้างภาพแผนที่ ----------

    public void RequestRebuild()
    {
        if (!isActiveAndEnabled) return;
        if (_buildRoutine != null) StopCoroutine(_buildRoutine);
        _buildRoutine = StartCoroutine(BuildWhenReady());
    }

    private IEnumerator BuildWhenReady()
    {
        // รอ scene ใหม่ Awake/Start ครบ แล้วรอแอนิเมชันสร้าง map เล่นจบ (กันค้างสุด 30 วินาที)
        yield return null;
        yield return null;
        SetGenerator(FindInActiveScene<ProceduralMapGenerator>());
        float timeout = Time.realtimeSinceStartup + 30f;
        while (AnyBuildPlaying() && Time.realtimeSinceStartup < timeout) yield return null;
        yield return null;
        yield return BuildHeightMap();
        _buildRoutine = null;
    }

    private void SetGenerator(ProceduralMapGenerator gen)
    {
        if (_generator == gen) return;
        if (_generator != null) _generator.MapGenerated -= RequestRebuild;
        _generator = gen;
        if (_generator != null) _generator.MapGenerated += RequestRebuild;
    }

    private static bool AnyBuildPlaying()
    {
        var scene = SceneManager.GetActiveScene();
        foreach (var a in FindObjectsByType<MapBuildAnimator>())
            if (a.gameObject.scene == scene && a.IsPlaying) return true;
        return false;
    }

    private static T FindInActiveScene<T>() where T : Component
    {
        var scene = SceneManager.GetActiveScene();
        foreach (var c in FindObjectsByType<T>())
            if (c.gameObject.scene == scene) return c;
        return null;
    }

    private IEnumerator BuildHeightMap()
    {
        if (!TryGetMapBounds(out Bounds bounds)) yield break;

        float cell = Mathf.Max(minCellSize, Mathf.Max(bounds.size.x, bounds.size.z) / maxTextureSize);
        int texW = Mathf.Clamp(Mathf.CeilToInt(bounds.size.x / cell), 8, maxTextureSize);
        int texH = Mathf.Clamp(Mathf.CeilToInt(bounds.size.z / cell), 8, maxTextureSize);
        float minX = bounds.min.x, minZ = bounds.min.z;
        float top = bounds.max.y + 50f;
        float distance = top - bounds.min.y + 100f;

        var scene = SceneManager.GetActiveScene();
        PhysicsScene physics = scene.GetPhysicsScene(); // กฎข้อ 6: ใช้ physics ของฉากแผนที่นั้น
        Physics.SyncTransforms();
        _ignoreCache.Clear();

        var heights = new float[texW * texH];
        int rays = 0;
        for (int y = 0; y < texH; y++)
        {
            for (int x = 0; x < texW; x++)
            {
                var origin = new Vector3(minX + (x + 0.5f) * cell, top, minZ + (y + 0.5f) * cell);
                heights[y * texW + x] = SampleHeight(physics, origin, distance);
            }
            rays += texW;
            if (rays >= raysPerFrame)
            {
                rays = 0;
                yield return null;
                if (scene != SceneManager.GetActiveScene()) yield break; // เปลี่ยน scene ระหว่างสร้าง
            }
        }

        // แบ่งชั้นจากความสูง
        float minH = float.MaxValue, maxH = float.MinValue;
        foreach (float h in heights)
        {
            if (float.IsNaN(h)) continue;
            minH = Mathf.Min(minH, h);
            maxH = Mathf.Max(maxH, h);
        }
        if (minH > maxH) yield break; // ไม่เจอพื้นเลย

        float layer = _generator != null && _generator.Config != null && _generator.Config.terraces
            ? Mathf.Max(0.1f, _generator.Config.terraceHeight) : layerHeight;
        int bandCount = Mathf.Max(1, Mathf.FloorToInt((maxH - minH) / layer + 0.001f) + 1);
        var bands = new int[heights.Length];
        for (int i = 0; i < heights.Length; i++)
            bands[i] = float.IsNaN(heights[i]) ? -1 : Mathf.FloorToInt((heights[i] - minH) / layer + 0.001f);

        var pixels = new Color32[heights.Length];
        for (int y = 0; y < texH; y++)
        {
            for (int x = 0; x < texW; x++)
            {
                int i = y * texW + x;
                int b = bands[i];
                if (b < 0) { pixels[i] = new Color32(0, 0, 0, 0); continue; }
                // ชั้นเดียว (พื้นเรียบ) = เทากลาง ไม่ขาวล้วน
                Color c = Color.Lerp(lowColor, highColor, bandCount > 1 ? (float)b / (bandCount - 1) : 0.6f);
                // ขอบชั้น: เพื่อนบ้านคนละชั้น / ไม่มีพื้น -> เข้มลง
                if (IsEdge(bands, texW, texH, x, y, b)) c = new Color(c.r * edgeShade, c.g * edgeShade, c.b * edgeShade, c.a);
                pixels[i] = c;
            }
        }

        ReleaseTexture();
        _texture = new Texture2D(texW, texH, TextureFormat.RGBA32, false)
        {
            name = "MinimapHeight",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };
        _texture.SetPixels32(pixels);
        _texture.Apply(false, true); // ไม่ต้องเก็บสำเนาไว้ฝั่ง CPU

        _minX = minX;
        _minZ = minZ;
        _width = texW * cell;
        _height = texH * cell;
        _hasMap = true;
        foreach (var d in new[] { minimap, overlay })
        {
            if (d.mapImage == null) continue;
            d.mapImage.texture = _texture;
            d.mapImage.enabled = true;
        }
        if (areaLabel != null) areaLabel.text = scene.name;
    }

    private static bool IsEdge(int[] bands, int w, int h, int x, int y, int b)
    {
        return Differs(x - 1, y) || Differs(x + 1, y) || Differs(x, y - 1) || Differs(x, y + 1);

        bool Differs(int nx, int ny)
        {
            if (nx < 0 || ny < 0 || nx >= w || ny >= h) return false;
            return bands[ny * w + nx] != b;
        }
    }

    // ความสูงของสิ่งแรกที่โดน (ข้ามตัวละคร / trigger) ไม่โดนอะไร = NaN
    private float SampleHeight(PhysicsScene physics, Vector3 origin, float distance)
    {
        int count = physics.Raycast(origin, Vector3.down, _hits, distance, groundLayers, QueryTriggerInteraction.Ignore);
        float best = float.NaN;
        float bestDist = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            var hit = _hits[i];
            if (hit.distance >= bestDist || IsCharacter(hit.collider)) continue;
            bestDist = hit.distance;
            best = hit.point.y;
        }
        return best;
    }

    private bool IsCharacter(Collider c)
    {
        if (!_ignoreCache.TryGetValue(c, out bool ignore))
        {
            ignore = c.GetComponentInParent<Health>() != null;
            _ignoreCache[c] = ignore;
        }
        return ignore;
    }

    // map สุ่ม = MapBounds ของ generator / ไม่งั้น (Hideout) = รวม bounds ของ collider ใน scene (ไม่รวมตัวละคร)
    private bool TryGetMapBounds(out Bounds bounds)
    {
        bounds = default;
        if (_generator != null && _generator.MapBounds.size.x > 0f)
        {
            bounds = _generator.MapBounds;
            bounds.Expand(new Vector3(8f, 0f, 8f));
            // ความสูงจริงจาก collider ของแผนที่ (MapBounds เป็นระนาบ y = 0)
            bounds.SetMinMax(new Vector3(bounds.min.x, bounds.min.y - 50f, bounds.min.z), new Vector3(bounds.max.x, bounds.max.y + 100f, bounds.max.z));
            return true;
        }

        // พื้นผืนใหญ่ (กว้างเกิน fallbackFloorSize) ไม่นับตอนหาขอบเขต -> โฟกัสพื้นที่ที่มีของวางอยู่ (raycast ยังโดนพื้นนั้นตามปกติ)
        bool any = false, anyFloor = false;
        Bounds floor = default;
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (var c in root.GetComponentsInChildren<Collider>())
            {
                if (!c.enabled || c.isTrigger || ((1 << c.gameObject.layer) & groundLayers) == 0) continue;
                if (c.GetComponentInParent<Health>() != null) continue;
                Bounds b = c.bounds;
                if (Mathf.Max(b.size.x, b.size.z) > fallbackFloorSize)
                {
                    if (anyFloor) floor.Encapsulate(b);
                    else { floor = b; anyFloor = true; }
                    continue;
                }
                if (any) bounds.Encapsulate(b);
                else { bounds = b; any = true; }
            }
        }
        if (any) bounds.Expand(new Vector3(fallbackMargin * 2f, 0f, fallbackMargin * 2f));
        else if (anyFloor) bounds = floor;
        else return false;
        if (anyFloor) bounds.Encapsulate(new Vector3(bounds.center.x, floor.min.y, bounds.center.z)); // ให้ raycast ลงไปถึงพื้น

        Vector3 size = bounds.size;
        size.x = Mathf.Min(size.x, maxFallbackSize);
        size.z = Mathf.Min(size.z, maxFallbackSize);
        bounds.size = size;
        return true;
    }

    private void ReleaseTexture()
    {
        foreach (var d in new[] { minimap, overlay })
            if (d.mapImage != null) d.mapImage.texture = null;
        _hasMap = false;
        if (_texture == null) return;
        Destroy(_texture); // กฎข้อ 10: asset ที่สร้างตอนเล่นต้องทำลายเอง
        _texture = null;
    }

    // ---------- แสดงผล ----------

    private Vector2 ToUV(Vector3 world) => new Vector2((world.x - _minX) / _width, (world.z - _minZ) / _height);

    private void LateUpdate()
    {
        if (_player == null || !_hasMap) return;
        Camera gameCam = Camera.main;
        float yaw = gameCam != null ? gameCam.transform.eulerAngles.y : 0f;

        Draw(minimap, yaw);
        if (_overlayOpen) Draw(overlay, yaw);
    }

    private void Draw(Display d, float yaw)
    {
        if (d.viewport == null || d.mapImage == null) return;
        Rect view = d.viewport.rect;

        // ขนาดภาพบนจอ: minimap = ตาม viewRadius / overlay = ทั้งแผนที่ (หลังหมุน) พอดีกรอบ
        float pxPerUnit;
        if (d.followPlayer) pxPerUnit = view.width / (viewRadius * 2f);
        else
        {
            float rad = yaw * Mathf.Deg2Rad;
            float cos = Mathf.Abs(Mathf.Cos(rad)), sin = Mathf.Abs(Mathf.Sin(rad));
            float rotW = cos * _width + sin * _height;
            float rotH = sin * _width + cos * _height;
            pxPerUnit = Mathf.Min(view.width / rotW, view.height / rotH) * overlayFill;
        }
        Vector2 size = new Vector2(_width, _height) * pxPerUnit;
        var mapRect = d.mapImage.rectTransform;
        mapRect.sizeDelta = size;
        Vector2 playerUV = ToUV(_player.position);
        mapRect.anchoredPosition = d.followPlayer ? -(playerUV - new Vector2(0.5f, 0.5f)) * size : Vector2.zero;
        if (d.rotator != null) d.rotator.localRotation = Quaternion.Euler(0f, 0f, yaw);

        // ลูกศร: มุมของผู้เล่นบนแผนที่ (ตามเข็มจากทิศ +z) หักมุมกล้อง
        if (d.playerIcon != null)
        {
            Vector3 f = _player.forward;
            float heading = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            if (d.followPlayer)
            {
                d.playerIcon.anchoredPosition = Vector2.zero;
                d.playerIcon.localRotation = Quaternion.Euler(0f, 0f, -(heading - yaw));
            }
            else
            {
                Place(d.playerIcon, _player.position, size);
                d.playerIcon.localRotation = Quaternion.Euler(0f, 0f, -heading); // อยู่ในแผ่นที่หมุน yaw แล้ว
                d.playerIcon.SetAsLastSibling();
            }
        }

        // มอนสเตอร์
        int normal = 0, elite = 0;
        foreach (var h in Health.All)
        {
            if (h.team != Team.Enemy || h.IsDead) continue;
            var rank = h.GetComponent<EnemyRank>();
            bool isElite = rank != null && rank.IsElite;
            var icon = isElite ? Icon(d, d.eliteIcons, eliteIconTemplate, elite++) : Icon(d, d.enemyIcons, enemyIconTemplate, normal++);
            if (icon != null) Place(icon.rectTransform, h.transform.position, size);
        }
        HideFrom(d.enemyIcons, normal);
        HideFrom(d.eliteIcons, elite);

        // object ที่คลิกได้ (portal / กล่องของดรอป)
        int interact = 0;
        var scene = SceneManager.GetActiveScene();
        foreach (var it in ClickInteractable.All)
        {
            if (it == null || it.gameObject.scene != scene) continue;
            var icon = Icon(d, d.interactIcons, interactableIconTemplate, interact++);
            if (icon != null) Place(icon.rectTransform, it.transform.position, size);
        }
        HideFrom(d.interactIcons, interact);
    }

    private Image Icon(Display d, List<Image> pool, Image template, int index)
    {
        if (template == null) return null;
        while (pool.Count <= index)
        {
            var icon = Instantiate(template, d.mapImage.rectTransform);
            icon.raycastTarget = false;
            pool.Add(icon);
        }
        var result = pool[index];
        if (!result.gameObject.activeSelf) result.gameObject.SetActive(true);
        return result;
    }

    private void Place(RectTransform rt, Vector3 world, Vector2 mapSize)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = (ToUV(world) - new Vector2(0.5f, 0.5f)) * mapSize;
    }

    private static void HideFrom(List<Image> pool, int used)
    {
        for (int i = used; i < pool.Count; i++)
            if (pool[i].gameObject.activeSelf) pool[i].gameObject.SetActive(false);
    }
}

using System.Collections.Generic;
using UnityEngine;

// ค่าตั้งของแผนที่ 1 แบบ (variant) แยกออกจาก ProceduralMapGenerator เพื่อสร้างหลาย variant ได้ง่าย
// สร้าง asset: Create > Procedural Map > Map Config แล้วลากใส่ช่อง Config ของ ProceduralMapGenerator
// แก้ค่าใน asset ระหว่างอยู่ใน Editor แล้ว generator ที่ใช้ asset นี้อยู่จะ generate ใหม่ให้ทันที
[CreateAssetMenu(fileName = "MapConfig", menuName = "Procedural Map/Map Config")]
public class ProceduralMapConfig : ScriptableObject
{
    [Header("Grid / Step")]
    [Min(0.1f)] public float stepSize = 3f;
    [Range(0, 4)] public int fillRadius = 1;
    [Range(0f, 1f)] public float edgeFillChance = 0.6f;

    [Header("Ground Appearance")]
    public Material groundMaterial;
    [Min(0.01f)] public float textureWorldSize = 1f;
    public bool addCollider = true;

    [Header("Map Generation")]
    [Min(1)] public int walkSteps = 40;
    public bool moveTransformToEnd = true;

    [Header("Start / End Markers")]
    public bool showStartEndMarkers = true;
    [Tooltip("ถ้าไม่ตั้งค่า จะสร้าง sphere สีให้อัตโนมัติแทน")]
    public GameObject startMarkerPrefab;
    public GameObject endMarkerPrefab;
    public float markerSize = 1.5f;

    [Header("Warp Portal")]
    [Tooltip("object ที่วางไว้ที่จุด Start ทุกครั้งที่ generate (เช่น portal กลับ hideout) เว้นว่าง = ไม่วาง")]
    public GameObject warpPortalPrefab;
    //[Tooltip("เลื่อนตำแหน่ง portal จากจุด Start (player เกิดที่จุด Start ด้วย ถ้า collider ของ portal ไม่ใช่ trigger ควรเลื่อนออก)")]
    [Tooltip("offset จากพื้น ณ จุดที่ตั้ง (Y = ระยะจาก pivot ของ prefab ถึงฐาน) ระบบหาความสูงพื้นตรงนั้นให้เอง")]
    public Vector3 warpPortalOffset = Vector3.zero;

    [Header("Object Scatter Preview")]
    [Tooltip("สุ่มวาง cube สีต่างๆ กระจายทั่วพื้นที่ walkable เพื่อจำลองตำแหน่งวาง object จริงในอนาคต")]
    public bool scatterObjects = false;
    [Tooltip("สุ่มขยับตำแหน่งภายใน cell เล็กน้อย กันดูเป็นตารางเป๊ะ (0 = อยู่กึ่งกลาง cell พอดี)")]
    [Range(0f, 1f)] public float scatterPositionJitter = 0.3f;
    public List<ScatterCategory> scatterCategories = new List<ScatterCategory>
    {
        new ScatterCategory { categoryName = "Obstacle", color = Color.red, count = 8 },
        new ScatterCategory { categoryName = "Nature", color = Color.green, count = 12 },
        new ScatterCategory { categoryName = "Item", color = Color.yellow, count = 5 },
    };

    [Header("Post-Process: Smoothing")]
    public bool smoothEdges = true;
    [Range(1, 20)] public int smoothIterations = 1;
    [Range(1, 8)] public int smoothThreshold = 5;

    [Header("Post-Process: Connectivity")]
    public bool connectIslands = true;
    public bool fillEnclosedHoles = true;

    [Header("Post-Process: Noise Holes")]
    [Tooltip("เจาะรูขนาดเล็กลงในพื้น ไม่ผูกกับขนาด Step Size แล้ว")]
    public bool addNoiseHoles = false;
    [Tooltip("ความละเอียด mesh ต่อ 1 step สำหรับตัดรู ยิ่งสูงขอบรูยิ่งเนียน/กลมขึ้น แต่ mesh หนักขึ้น (sub-quad = ค่านี้ยกกำลังสอง ต่อ 1 cell)")]
    [Range(2, 12)] public int holeMeshResolution = 6;
    [Tooltip("โอกาสที่แต่ละ cell จะกลายเป็นจุดเริ่มรู (0 = ไม่มีรู, 1 = รูเยอะมาก)")]
    [Range(0f, 1f)] public float holeDensity = 0.12f;
    [Tooltip("รัศมีของแต่ละรู (world unit) สุ่มในช่วงนี้ ไม่เกี่ยวกับ Step Size")]
    public Vector2 holeRadiusRange = new Vector2(0.3f, 1.2f);
    [Tooltip("ความเบี้ยวของขอบรู (0 = กลมเป๊ะ, 1 = ขอบบิดเบี้ยวเยอะ ดูเป็นธรรมชาติไม่เหลี่ยม)")]
    [Range(0f, 1f)] public float holeEdgeJitter = 0.35f;

    [Header("Noise Holes: Pond (ต้องเปิด Add Noise Holes)")]
    [Tooltip("เปลี่ยนรูให้เป็นหลุม: มีผนัง + พื้นก้นหลุม + ผิวน้ำ")]
    public bool holesAsPonds = true;
    [Tooltip("ความลึกของหลุมจากผิวพื้น (world unit)")]
    [Min(0f)] public float pitDepth = 1f;
    [Tooltip("ผิวน้ำอยู่ต่ำกว่าผิวพื้นเท่าไหร่ (ถูกจำกัดไม่ให้ลึกเกินก้นหลุม)")]
    [Min(0f)] public float waterSurfaceDepth = 0.25f;
    [Tooltip("ถ้าไม่ตั้งค่า จะสร้าง material น้ำสีฟ้าโปร่งใสให้อัตโนมัติ")]
    public Material waterMaterial;
    [Tooltip("ชื่อ Unity Layer ที่ใช้กับน้ำ (Water เป็น layer มาตรฐานของ Unity) ถ้าไม่มี layer นี้จะใช้ Default")]
    public string waterLayerName = "Water";
    [Tooltip("เดินลุยน้ำได้: ไม่มีกำแพงรอบบ่อ ไม่ respawn มีพื้นล่องหนลาดลงจากขอบบ่อ ตัวจมลงน้ำตอนเดินเข้า เดินขึ้นฝั่งเองได้ (เปิดแล้ว Block Walking Into Holes จะไม่มีผล)")]
    public bool walkableWater = true;
    [Tooltip("ความลึกพื้นลุยน้ำจากผิวพื้น (ลึกกว่า Water Surface Depth = เท้าจมน้ำ) ถูกจำกัดไม่ให้ลึกเกินก้นหลุม")]
    [Min(0f)] public float waterWalkDepth = 0.5f;
    [Tooltip("ระยะทางลาดจากขอบบ่อจนถึงความลึกเต็ม (world unit) ยิ่งมากยิ่งลาดเบา กันลื่นไถลบนทางลาด")]
    [Min(0.05f)] public float waterWalkRampWidth = 1f;
    [Tooltip("ห้ามมีบ่อ/รูในรัศมีนี้ (world unit) รอบจุด Start/End กัน player เกิดในน้ำ และ Warp Portal ตั้งบนน้ำ")]
    [Min(0f)] public float holeClearanceAroundStartEnd = 2f;
    [Tooltip("สร้างกำแพงล่องหนรอบขอบบ่อ/รู กันเดินลงน้ำ (PlayerMovement จะทะลุกำแพงนี้ได้ตอนกระโดด/dash)")]
    public bool blockWalkingIntoHoles = true;
    [Tooltip("ความสูงกำแพงรอบบ่อเหนือผิวพื้น")]
    [Min(0.1f)] public float holeBlockerHeight = 2f;
    [Tooltip("ความหนากำแพงรอบบ่อ (กินเข้ามาฝั่งพื้น) บางไว้จะได้ยืนชิดขอบบ่อได้")]
    [Min(0.02f)] public float holeBlockerThickness = 0.15f;

    [Header("Island Underside (ใต้พื้นให้ดูเป็นเกาะลอย)")]
    public bool buildUnderside = true;
    [Tooltip("Island = ก้อนหินเรียวลง / Cube = ฐานทรงกล่อง (ผนังตั้งฉาก ก้นเรียบ) Cube ไม่ใช้ Edge Lip / Taper / Noise")]
    public UndersideType undersideType = UndersideType.Island;
    [Tooltip("ถ้าไม่ตั้งค่า จะสร้าง material สีน้ำตาลให้อัตโนมัติ / vertex color.r = ความลึก 0..1 ไว้ blend ใน Shader Graph")]
    public Material undersideMaterial;
    [Tooltip("ความลึกสูงสุดใต้ผิวพื้น (Cube = ความสูงของฐาน)")]
    [Min(0.5f)] public float undersideMaxDepth = 8f;
    [Tooltip("ความหนาผนังตรงขอบ (ถ้าเปิดบ่อน้ำ จะถูกดันให้ไม่ตื้นกว่าก้นหลุม)")]
    [Min(0f)] public float undersideEdgeLip = 0.6f;
    [Tooltip("ยิ่งน้อยยิ่งเรียวชัน (ระยะจากขอบที่ลึกถึง ~63% ของความลึกสูงสุด)")]
    [Min(0.1f)] public float undersideTaperDistance = 5f;
    [Tooltip("ความขรุขระ/หินยื่นลงล่าง (world unit)")]
    [Range(0f, 4f)] public float undersideNoise = 1.5f;
    [Min(0.01f)] public float undersideNoiseScale = 0.25f;
    [Tooltip("จำนวน quad ต่อ 1 cell ยิ่งมากยิ่งละเอียด")]
    [Range(1, 4)] public int undersideResolution = 2;
    public bool undersideCollider = false;

    [Header("Map Boundary (กำแพงล่องหนกันตกขอบ)")]
    [Tooltip("สร้าง BoxCollider ล่องหนรอบขอบแผนที่ กัน player เดินตก map")]
    public bool buildBoundaryWalls = true;
    [Min(0.1f)] public float boundaryWallHeight = 3f;
    [Min(0.05f)] public float boundaryWallThickness = 0.5f;

    [Header("Scatter Optimization")]
    [Tooltip("ตั้ง Static Flag ให้ Ground/Underside/Water/Scattered Objects ช่วย Occlusion Culling เสมอ และช่วย Static Batching ตอน Build ถ้า generate ทิ้งไว้ใน Editor โดยไม่ regenerate ซ้ำตอน runtime (ถ้า regenerate ทุกครั้งที่ Awake ตอน Play จะไม่ได้ static batching เพราะ object เพิ่งถูกสร้างหลัง Build ไปแล้ว ต้องพึ่ง Combine Scatter Meshes แทน)")]
    public bool markGeneratedStatic = true;
    [Tooltip("รวม mesh ของ object ที่ scatter ที่ใช้ mesh+material ชุดเดียวกันให้เหลือไม่กี่ draw call ลดภาระ render มหาศาลเมื่อมี object เยอะ (ทำงานได้ทั้ง Editor และ runtime ไม่พึ่ง static batching ของ Unity) Renderer ต้นฉบับจะถูกปิดไว้ (ไม่ลบ) Collider/Script เดิมยังทำงานปกติ ข้อเสีย: เลือก/ขยับ object แต่ละตัวใน Editor ไม่ได้หลัง combine และ mesh ต้นฉบับต้องเปิด Read/Write Enabled ใน Import Settings")]
    public bool combineScatterMeshes = false;
    [Tooltip("หลัง combine ให้ปิด Collider ของ object ต้นฉบับไปด้วย (ปิดถ้า object เหล่านั้นไม่ต้องชนกับอะไรเลย ลด physics overhead เพิ่มอีกชั้น)")]
    public bool disableCollidersAfterCombine = false;

#if UNITY_EDITOR
    // ให้ generator ที่ใช้ asset นี้ generate ใหม่ตอนแก้ค่าใน Inspector
    public static event System.Action<ProceduralMapConfig> Changed;
    private void OnValidate() => Changed?.Invoke(this);
#endif
}

[System.Serializable]
public class ScatterCategory
{
    public string categoryName = "Category";
    public Color color = Color.white;
    [Min(0)] public int count = 5;
    [Tooltip("รายการ prefab ให้สุ่มเลือกตอน scatter แต่ละตัว (ใส่ได้หลายแบบเพื่อเพิ่มความหลากหลาย) ถ้าเว้นว่างจะสร้าง cube สีตามที่กำหนดให้อัตโนมัติแทน")]
    public GameObject[] prefabVariants;
    public float cubeScale = 1f;

    [Tooltip("สุ่มขนาด object แต่ละตัว (คูณกับ Cube Scale หรือขนาดเดิมของ prefab) มีผลกับแกน X/Z เสมอ และแกน Y ด้วยถ้าไม่ได้เปิด Randomize Height แยก")]
    public bool randomizeSize = false;
    [Tooltip("ช่วงตัวคูณขนาดแกน X/Z (min, max)")]
    public Vector2 sizeRange = new Vector2(0.8f, 1.2f);

    [Tooltip("สุ่มความสูง (แกน Y) แยกจากขนาด X/Z เช่นต้นไม้ต้นเตี้ยแคระ ต้นสูงชะลูด แต่ความกว้างพุ่มใกล้เคียงกัน ถ้าปิดไว้ แกน Y จะใช้ตัวคูณเดียวกับ Size Range")]
    public bool randomizeHeight = false;
    [Tooltip("ช่วงตัวคูณความสูงแกน Y (min, max)")]
    public Vector2 heightRange = new Vector2(0.8f, 1.2f);

    [Tooltip("สุ่มหมุนรอบแกน Y")]
    public bool randomizeRotation = false;
    [Tooltip("ช่วงมุมหมุนรอบแกน Y หน่วยองศา (min, max)")]
    public Vector2 rotationRange = new Vector2(0f, 360f);
}

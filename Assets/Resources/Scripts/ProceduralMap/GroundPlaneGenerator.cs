using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
[ExecuteAlways]
public class GroundPlaneGenerator : MonoBehaviour
{
    [Header("Size")]
    [Min(0.01f)] public float width = 10f;
    [Min(0.01f)] public float length = 10f;

    [Header("Subdivisions (optional, 1x1 = single quad)")]
    [Min(1)] public int widthSegments = 1;
    [Min(1)] public int lengthSegments = 1;

    [Header("Texture Tiling")]
    [Min(0.01f)] public float textureWorldSize = 1f;

    [Header("Material")]
    public Material groundMaterial;

    [Header("Randomization")]
    [Tooltip("ถ้าเปิด: ตอนกด Play จะสุ่มขนาดใหม่ทุกครั้งก่อน generate")]
    public bool randomizeOnPlay = false;
    [Tooltip("ถ้าเปิด: ใช้ seed คงที่ (ได้แผนที่ซ้ำแบบทำนายได้) / ถ้าปิด: สุ่มไม่ซ้ำทุกรอบ")]
    public bool useFixedSeed = false;
    public int seed = 12345;
    public Vector2 widthRange = new Vector2(5f, 20f);
    public Vector2 lengthRange = new Vector2(5f, 20f);

    private MeshFilter _meshFilter;
    private MeshRenderer _meshRenderer;

    private void OnEnable()
    {
        if (Application.isPlaying && randomizeOnPlay)
        {
            if (useFixedSeed) Random.InitState(seed);
            RandomizeSize();
        }
        GenerateMesh();
    }

    private void OnValidate() => GenerateMesh();

    public void RandomizeSize()
    {
        width = Random.Range(widthRange.x, widthRange.y);
        length = Random.Range(lengthRange.x, lengthRange.y);
    }

    [ContextMenu("Randomize Size & Regenerate")]
    private void RandomizeAndGenerateEditor()
    {
        RandomizeSize();
        GenerateMesh();
    }

    // ให้สคริปต์อื่น (เช่น agent) รู้ขอบเขตพื้นที่จริงในโลก
    public Bounds GetWorldBounds()
    {
        Vector3 center = transform.position;
        Vector3 size = new Vector3(width, 0.01f, length);
        return new Bounds(center, size);
    }

    public void GenerateMesh()
    {
        if (_meshFilter == null) _meshFilter = GetComponent<MeshFilter>();
        if (_meshRenderer == null) _meshRenderer = GetComponent<MeshRenderer>();

        int xSegs = Mathf.Max(1, widthSegments);
        int zSegs = Mathf.Max(1, lengthSegments);
        int vertsX = xSegs + 1;
        int vertsZ = zSegs + 1;

        Vector3[] vertices = new Vector3[vertsX * vertsZ];
        Vector2[] uvs = new Vector2[vertsX * vertsZ];
        int[] triangles = new int[xSegs * zSegs * 6];

        float halfW = width * 0.5f;
        float halfL = length * 0.5f;

        for (int z = 0; z < vertsZ; z++)
        {
            float tz = (float)z / zSegs;
            float posZ = Mathf.Lerp(-halfL, halfL, tz);

            for (int x = 0; x < vertsX; x++)
            {
                float tx = (float)x / xSegs;
                float posX = Mathf.Lerp(-halfW, halfW, tx);

                int i = z * vertsX + x;
                vertices[i] = new Vector3(posX, 0f, posZ);
                uvs[i] = new Vector2(posX / textureWorldSize, posZ / textureWorldSize);
            }
        }

        int triIndex = 0;
        for (int z = 0; z < zSegs; z++)
        {
            for (int x = 0; x < xSegs; x++)
            {
                int i = z * vertsX + x;
                triangles[triIndex++] = i;
                triangles[triIndex++] = i + vertsX;
                triangles[triIndex++] = i + 1;
                triangles[triIndex++] = i + 1;
                triangles[triIndex++] = i + vertsX;
                triangles[triIndex++] = i + vertsX + 1;
            }
        }

        Mesh mesh = new Mesh { name = "Ground Plane" };
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();

        _meshFilter.sharedMesh = mesh;

        var collider = GetComponent<MeshCollider>();
        if (collider != null) collider.sharedMesh = mesh;

        if (groundMaterial != null) _meshRenderer.sharedMaterial = groundMaterial;
    }
}
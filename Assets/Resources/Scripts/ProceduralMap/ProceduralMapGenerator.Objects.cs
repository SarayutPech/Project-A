using UnityEngine;
using System.Collections.Generic;

// จุด Start/End, warp portal, โปรยต้นไม้/หิน (partial ของ ProceduralMapGenerator ไฟล์หลัก = ProceduralMapGenerator.cs)
public partial class ProceduralMapGenerator
{
    // ---------- Start / End Markers ----------

    private void PlaceMarkers(Transform root, Vector2Int startCell, Vector2Int endCell)
    {
        Vector3 startPos = CellToWorld(startCell);
        Vector3 endPos = CellToWorld(endCell);
        startPos.y = GetSurfaceHeight(startPos);
        endPos.y = GetSurfaceHeight(endPos);

        StartMarker = CreateMarker(root, "Start Marker", startMarkerPrefab, startPos, new Color(0.2f, 0.9f, 0.3f));
        EndMarker = CreateMarker(root, "End Marker", endMarkerPrefab, endPos, new Color(0.9f, 0.25f, 0.25f));
    }

    // วาง portal ไว้ใต้ GeneratedRoot จึงถูกลบพร้อมแผนที่เก่าทุกครั้งที่ generate ใหม่
    private void PlaceWarpPortal(Transform root, Vector2Int startCell)
    {
        if (warpPortalPrefab == null) return;

        // Y = ผิวพื้นจริง ณ จุดตั้ง (ไม่ใช่ความสูง transform ของ generator) offset จึงเป็นแค่ระยะ pivot->ฐานของ prefab
        Vector3 pos = CellToWorld(startCell);
        pos.y = GetSurfaceHeight(pos);
        WarpPortal = Instantiate(warpPortalPrefab, pos + warpPortalOffset, warpPortalPrefab.transform.rotation, root);
        WarpPortal.name = "Warp Portal";
    }

    private GameObject CreateMarker(Transform root, string markerName, GameObject prefab, Vector3 position, Color fallbackColor)
    {
        GameObject marker;
        if (prefab != null)
        {
            marker = Instantiate(prefab, position, Quaternion.identity, root);
        }
        else
        {
            marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.transform.SetParent(root, false);
            marker.transform.position = position;
            marker.transform.localScale = Vector3.one * markerSize;

            var col = marker.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }

            var rend = marker.GetComponent<Renderer>();
            if (rend != null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                if (shader != null)
                {
                    var mat = new Material(shader) { color = fallbackColor };
                    rend.sharedMaterial = mat;
                }
            }
        }

        marker.name = markerName;
        return marker;
    }

    // ---------- Object Scatter Preview ----------

    private void ScatterObjects(Transform root, Vector2Int startCell, Vector2Int endCell)
    {
        if (!scatterObjects || scatterCategories == null || scatterCategories.Count == 0) return;

        var scatterContainer = new GameObject("Scattered Objects");
        scatterContainer.transform.SetParent(root, false);
        ScatterContainer = scatterContainer.transform;

        // เก็บ cell ที่ใช้ไม่ได้ไว้ก่อน (จุด start/end กับ cell ที่โดน noise hole)
        HashSet<Vector2Int> excluded = new HashSet<Vector2Int> { startCell, endCell };

        List<Vector2Int> availableCells = new List<Vector2Int>();
        foreach (var c in _cells)
        {
            if (excluded.Contains(c)) continue;
            if (Terrace.IsRampAccess(c)) continue; // ห้ามของขวางทางลาด/ปากทางลาด ไม่งั้นอาจขึ้นชั้นบนไม่ได้
            if (addNoiseHoles && IsInsideHole(CellToWorld(c))) continue;
            availableCells.Add(c);
        }
        ShuffleCells(availableCells);

        // material 1 ตัวต่อ 1 category (ไม่ใช่ 1 ต่อ object) กัน object เยอะแล้ว alloc material ซ้ำเป็นพันตัว
        // และจำเป็นสำหรับให้ Unity batch (SRP Batcher / GPU Instancing) ก้อน cube fallback เหล่านี้ได้
        var fallbackMaterials = new Dictionary<ScatterCategory, Material>();

        int cursor = 0;
        foreach (var category in scatterCategories)
        {
            if (category == null) continue;

            // กรอง null ออกจาก prefabVariants ไว้ล่วงหน้าครั้งเดียวต่อ category กันสุ่มเจอ slot ว่างซ้ำๆ ระหว่าง loop
            GameObject[] validPrefabs = category.prefabVariants != null
                ? System.Array.FindAll(category.prefabVariants, p => p != null)
                : null;

            int placed = 0;
            while (placed < category.count && cursor < availableCells.Count)
            {
                Vector2Int cell = availableCells[cursor];
                cursor++;

                Vector3 pos = CellToWorld(cell);
                pos.x += Random.Range(-stepSize, stepSize) * 0.5f * scatterPositionJitter;
                pos.z += Random.Range(-stepSize, stepSize) * 0.5f * scatterPositionJitter;
                pos.y = transform.position.y + Terrace.HeightAtWorld(pos);

                CreateScatterObject(scatterContainer.transform, category, pos, fallbackMaterials, validPrefabs);
                placed++;
            }
        }

        if (combineScatterMeshes) ScatterMeshCombiner.Combine(scatterContainer.transform, !disableCollidersAfterCombine);
    }

    private void CreateScatterObject(Transform container, ScatterCategory category, Vector3 position,
        Dictionary<ScatterCategory, Material> fallbackMaterials, GameObject[] validPrefabs)
    {
        Quaternion rotation = category.randomizeRotation
            ? Quaternion.Euler(0f, Random.Range(category.rotationRange.x, category.rotationRange.y), 0f)
            : Quaternion.identity;
        float scaleMul = category.randomizeSize
            ? Random.Range(category.sizeRange.x, category.sizeRange.y)
            : 1f;
        // ไม่ได้เปิด Randomize Height แยก -> แกน Y ใช้ตัวคูณเดียวกับ X/Z (พฤติกรรมเดิม)
        float heightMul = category.randomizeHeight
            ? Random.Range(category.heightRange.x, category.heightRange.y)
            : scaleMul;
        Vector3 scaleVector = new Vector3(scaleMul, heightMul, scaleMul);

        GameObject prefab = (validPrefabs != null && validPrefabs.Length > 0)
            ? validPrefabs[Random.Range(0, validPrefabs.Length)] // สุ่มเลือก 1 แบบจากหลาย prefab ต่อ category
            : null;

        GameObject obj;
        if (prefab != null)
        {
            obj = Instantiate(prefab, position, rotation, container);
            if (scaleVector != Vector3.one) obj.transform.localScale = Vector3.Scale(obj.transform.localScale, scaleVector);
        }
        else
        {
            obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obj.transform.SetParent(container, false);
            obj.transform.SetPositionAndRotation(position, rotation);
            obj.transform.localScale = category.cubeScale * scaleVector;

            var rend = obj.GetComponent<Renderer>();
            if (rend != null)
            {
                if (!fallbackMaterials.TryGetValue(category, out var mat))
                {
                    Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                    if (shader == null) shader = Shader.Find("Standard");
                    if (shader != null)
                    {
                        mat = new Material(shader) { color = category.color };
                        mat.enableInstancing = true; // material ของเราเอง เปิด GPU Instancing ได้เต็มที่
                    }
                    fallbackMaterials[category] = mat;
                }
                if (mat != null) rend.sharedMaterial = mat;
            }
        }

        obj.name = category.categoryName;
        if (markGeneratedStatic) MarkStaticRecursive(obj);
    }

    // ตั้ง static ทั้งลูกหลาน เพราะ prefab ต้นไม้/ก้อนหินมักมี mesh หลายชิ้นแยกเป็น child object (canopy, LOD proxy ฯลฯ)
    private static void MarkStaticRecursive(GameObject go)
    {
        go.isStatic = true;
        foreach (Transform child in go.transform) MarkStaticRecursive(child.gameObject);
    }

    private void ShuffleCells(List<Vector2Int> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}

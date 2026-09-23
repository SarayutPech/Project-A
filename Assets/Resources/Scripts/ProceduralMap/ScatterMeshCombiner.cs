using System.Collections.Generic;
using UnityEngine;

// รวม mesh ของ object ที่ scatter (เช่นต้นไม้ที่กระจายเต็มแผนที่) ที่ใช้ mesh+material ชุดเดียวกัน
// ให้เหลือไม่กี่ draw call แทนที่จะ render object ทีละตัว
//
// Renderer ของ object ต้นฉบับถูก "ปิด" (ไม่ลบ) เพื่อให้ Transform/Collider/Script เดิมยังอยู่และทำงานได้ปกติ
// (เช่นถ้ามี wind-sway script หรือ collider ยิงปะทะที่ต้นไม้อยู่ ยังใช้งานได้ ตำแหน่งอ้างอิงจาก object เดิม)
//
// ข้อจำกัด: mesh ต้นฉบับต้องเปิด "Read/Write Enabled" ใน Import Settings ไม่งั้น CombineMeshes อ่าน vertex ไม่ได้
public static class ScatterMeshCombiner
{
    // เผื่อ margin จาก limit จริงของ mesh (~4 พันล้าน vertex ตอนใช้ UInt32 index) กันแค่ mesh ใหญ่เกินจนหน่วง
    private const int MaxVerticesPerBatch = 200_000;

    // marker กันไม่ให้ batch object (หรือ object ที่ถูกปิด renderer ไปแล้ว) ถูกดึงมารวมซ้ำรอบถัดไป
    private sealed class CombinedBatchMarker : MonoBehaviour { }
    private sealed class CombinedSourceMarker : MonoBehaviour { }

    public static void Combine(Transform root, bool keepOriginalColliders)
    {
        if (root == null) return;

        var groups = new Dictionary<(Mesh mesh, Material mat), List<MeshFilter>>();

        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            if (mf.GetComponent<CombinedBatchMarker>() != null) continue;
            if (mf.GetComponent<CombinedSourceMarker>() != null) continue;

            var rend = mf.GetComponent<MeshRenderer>();
            if (rend == null || rend.sharedMaterial == null || !rend.enabled) continue;

            if (!mf.sharedMesh.isReadable)
            {
                Debug.LogWarning($"[{nameof(ScatterMeshCombiner)}] ข้าม '{mf.name}' เพราะ mesh '{mf.sharedMesh.name}' ปิด Read/Write Enabled ไว้ (เปิดใน Import Settings ถ้าต้องการ combine)", mf);
                continue;
            }

            var key = (mf.sharedMesh, rend.sharedMaterial);
            if (!groups.TryGetValue(key, out var list))
            {
                list = new List<MeshFilter>();
                groups[key] = list;
            }
            list.Add(mf);
        }

        int batchIndex = 0;
        foreach (var kv in groups)
        {
            var (mesh, material) = kv.Key;
            var filters = kv.Value;
            if (filters.Count < 2) continue; // ตัวเดียวไม่คุ้มรวม

            int verticesPerInstance = Mathf.Max(1, mesh.vertexCount);
            int instancesPerBatch = Mathf.Max(1, MaxVerticesPerBatch / verticesPerInstance);

            for (int start = 0; start < filters.Count; start += instancesPerBatch)
            {
                int count = Mathf.Min(instancesPerBatch, filters.Count - start);
                CombineBatch(root, mesh, material, filters, start, count, batchIndex++);
            }

            foreach (var mf in filters)
            {
                var rend = mf.GetComponent<MeshRenderer>();
                if (rend != null) rend.enabled = false;
                mf.gameObject.AddComponent<CombinedSourceMarker>();

                if (!keepOriginalColliders)
                {
                    foreach (var col in mf.GetComponents<Collider>())
                    {
                        if (Application.isPlaying) Object.Destroy(col);
                        else Object.DestroyImmediate(col);
                    }
                }
            }
        }
    }

    private static void CombineBatch(Transform root, Mesh mesh, Material material, List<MeshFilter> filters, int start, int count, int batchIndex)
    {
        var combine = new CombineInstance[count];
        for (int i = 0; i < count; i++)
        {
            var mf = filters[start + i];
            combine[i].mesh = mesh;
            combine[i].transform = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
        }

        var combinedMesh = new Mesh
        {
            name = $"Scatter Batch ({material.name}) {batchIndex}",
            indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,
        };
        combinedMesh.CombineMeshes(combine, true, true);
        combinedMesh.RecalculateBounds();

        var batchObj = new GameObject($"Scatter Batch ({material.name}) {batchIndex}");
        batchObj.transform.SetParent(root, false);
        batchObj.isStatic = true;

        batchObj.AddComponent<MeshFilter>().sharedMesh = combinedMesh;
        batchObj.AddComponent<MeshRenderer>().sharedMaterial = material;
        batchObj.AddComponent<CombinedBatchMarker>();
    }
}

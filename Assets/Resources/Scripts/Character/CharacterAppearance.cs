using UnityEngine;

// เปลี่ยนสี base / material ของโมเดลตัวละคร (presentation อย่างเดียว) ใช้กับศัตรูที่ใช้โมเดลเดียวกับ player
// - Material Override: แทน material ทุกช่องของทุก renderer ลูก (ทำตอนเล่นเท่านั้น ไม่ทับค่าที่เซฟใน prefab)
// - Base Color: ทับสีผ่าน MaterialPropertyBlock (เห็นผลทั้งใน Editor และตอนเล่น ไม่สร้าง material ใหม่ต่อตัว)
//   หมายเหตุ: renderer ที่ใช้ property block จะไม่เข้า SRP Batcher ถ้าศัตรูเยอะมากค่อยเปลี่ยนเป็น material แยกต่อสี
[ExecuteAlways]
public class CharacterAppearance : MonoBehaviour
{
    [Tooltip("เว้นว่าง = ใช้ material เดิมของโมเดล")]
    public Material materialOverride;
    public bool overrideBaseColor = true;
    public Color baseColor = new Color(0.8f, 0.25f, 0.2f);
    [Tooltip("ชื่อ property สีหลักใน shader (Pencil_BaseColor / URP Lit = _BaseColor)")]
    public string colorProperty = "_BaseColor";

    private Renderer[] _renderers;
    private MaterialPropertyBlock _block;

    private void OnEnable() => Apply();

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Unity ไม่ให้แก้ renderer ระหว่าง OnValidate -> เลื่อนไปทำเฟรมถัดไป
        UnityEditor.EditorApplication.delayCall += () => { if (this != null) Apply(); };
    }
#endif

    public void SetBaseColor(Color color)
    {
        baseColor = color;
        overrideBaseColor = true;
        Apply();
    }

    [ContextMenu("Apply")]
    public void Apply()
    {
        _renderers = GetComponentsInChildren<Renderer>(true);
        if (_block == null) _block = new MaterialPropertyBlock();
        int colorId = Shader.PropertyToID(colorProperty);

        foreach (var r in _renderers)
        {
            if (r == null || r is ParticleSystemRenderer) continue;

            if (materialOverride != null && Application.isPlaying)
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = materialOverride;
                r.sharedMaterials = mats;
            }

            r.GetPropertyBlock(_block);
            if (overrideBaseColor) _block.SetColor(colorId, baseColor);
            else _block.Clear();
            r.SetPropertyBlock(_block.isEmpty ? null : _block);
        }
    }
}

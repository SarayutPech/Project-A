using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

// ขอให้ URP วาด depth + normals ของฉากไว้ก่อนวาด opaque (DepthNormals prepass)
// material ที่หาขอบจาก depth/normal ของกล้องตอนวาดตัวเอง (PencilEdge.hlsl) ต้องใช้ ไม่งั้น texture ยังว่าง
// pass นี้ไม่วาดอะไรเอง แค่ประกาศ input ที่ต้องการ (แบบเดียวกับ SSAO)
public class DepthNormalsPrepassFeature : ScriptableRendererFeature
{
    private class RequestPass : ScriptableRenderPass
    {
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) { }
    }

    private RequestPass _pass;

    public override void Create()
    {
        // ก่อน opaque -> URP ต้องทำ prepass ให้ (ถ้าขอหลัง opaque จะใช้การ copy depth ซึ่งช้าไปสำหรับ material ใน opaque)
        _pass = new RequestPass { renderPassEvent = RenderPassEvent.BeforeRenderingOpaques };
        _pass.ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(_pass);
    }
}

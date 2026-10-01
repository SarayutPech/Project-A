// เจาะวงกลมรอบ player บน object ที่บัง (ใช้ใน Shader Graph ผ่าน Custom Function node ชื่อ OcclusionCutout แบบ File)
// material ยังเป็น opaque + alpha clip (ไม่ต้องสลับเป็น transparent) -> ไม่มีปัญหาลำดับวาด / ผิวใสซ้อนกันเอง
// ขอบวงเป็น dither (Bayer 4x4) ให้ดูนุ่ม ข้างในเจาะทะลุ depth texture ด้วย เส้นดินสอ (PencilEdge) เลยวาดขอบรูให้เอง
//
// ค่าต่อชิ้น: Fade (ต่อ _FadeAlpha) 1 = ปกติ, 0 = เจาะเต็มรัศมี (OcclusionOutlineController ตั้งผ่าน MaterialPropertyBlock)
// ค่า global (OcclusionOutlineController ตั้งทุกเฟรม):
//   _OcclusionCutCenter   xyz = จุดกลางตัว player (world), w = 1 เปิดใช้
//   _OcclusionCutRadius   รัศมีเป็นสัดส่วนความสูงจอ
//   _OcclusionCutSoftness ความกว้างขอบ dither เป็นสัดส่วนของรัศมี
// เจาะเฉพาะ pixel ที่อยู่ใกล้กล้องกว่า player (ของที่อยู่หลังตัวไม่โดนเจาะ) และไม่เจาะใน shadow pass (เงายังอยู่ครบ)
#ifndef OCCLUSION_CUTOUT_INCLUDED
#define OCCLUSION_CUTOUT_INCLUDED

float4 _OcclusionCutCenter;
float _OcclusionCutRadius;
float _OcclusionCutSoftness;

float OcclusionBayer4(uint2 p)
{
    static const float m[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
    return (m[(p.y & 3) * 4 + (p.x & 3)] + 0.5) / 16.0;
}

// Alpha ต่อเข้า Alpha block / ClipThreshold ต่อเข้า Alpha Clip Threshold block (material ต้องเปิด Alpha Clipping)
void OcclusionCutout_float(float Fade, float3 PositionWS, out float Alpha, out float ClipThreshold)
{
    Alpha = 1.0;
    ClipThreshold = 0.5;

#if !defined(SHADERGRAPH_PREVIEW) && !(defined(SHADERPASS_SHADOWCASTER) && SHADERPASS == SHADERPASS_SHADOWCASTER)
    float amount = saturate(1.0 - Fade);
    if (amount <= 0.0 || _OcclusionCutCenter.w < 0.5) return;

    // ระยะบนจอ (หน่วย = ความสูงจอ) คิดจาก clip space ของทั้งสองจุดด้วย matrix เดียวกัน ไม่ต้องสนทิศแกน y ของแต่ละ platform
    float4 pCS = mul(UNITY_MATRIX_VP, float4(PositionWS, 1.0));
    float4 cCS = mul(UNITY_MATRIX_VP, float4(_OcclusionCutCenter.xyz, 1.0));
    float2 pNdc = pCS.xy / pCS.w;
    float2 d = pNdc - cCS.xy / cCS.w;
    d.x *= _ScreenParams.x / _ScreenParams.y;
    float dist = length(d) * 0.5;

    // วงขยายออกตาม amount (fade in/out = วงโต/หด)
    float radius = _OcclusionCutRadius * amount;
    float soft = max(radius * _OcclusionCutSoftness, 1e-4);
    float mask = 1.0 - smoothstep(radius - soft, radius, dist);

    // เฉพาะส่วนที่อยู่หน้า player (view space มองไปทาง -z)
    float pz = -mul(UNITY_MATRIX_V, float4(PositionWS, 1.0)).z;
    float cz = -mul(UNITY_MATRIX_V, float4(_OcclusionCutCenter.xyz, 1.0)).z;
    mask *= step(pz, cz);

    Alpha = 1.0 - mask;
    uint2 pixel = (uint2)((pNdc * 0.5 + 0.5) * _ScreenParams.xy);
    ClipThreshold = OcclusionBayer4(pixel);
#endif
}

void OcclusionCutout_half(half Fade, half3 PositionWS, out half Alpha, out half ClipThreshold)
{
    float a, c;
    OcclusionCutout_float(Fade, PositionWS, a, c);
    Alpha = a;
    ClipThreshold = c;
}

#endif

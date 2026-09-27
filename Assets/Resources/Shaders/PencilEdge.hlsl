// เส้นดินสอตามขอบ (ใช้ใน Shader Graph ผ่าน Custom Function node ชื่อ PencilEdge แบบ File)
// หาขอบจาก depth + normal ของกล้องรอบ pixel นี้ (Roberts cross) -> ได้ทั้งเส้นรอบรูป (silhouette) และเส้นมุมหัก (crease)
// ทำเป็นลายดินสอ: ตำแหน่งเส้นสั่นด้วย noise ที่เปลี่ยนเป็นจังหวะ (boil แบบงานวาดมือ) + เนื้อ graphite เป็นเสี้ยน
//
// ต้องมี renderer feature "Depth Normals Prepass" ใน URP renderer (DepthNormalsPrepassFeature)
// ไม่งั้น _CameraDepthTexture / _CameraNormalsTexture ยังไม่พร้อมตอนวาด opaque -> ไม่มีเส้น
#ifndef PENCIL_EDGE_INCLUDED
#define PENCIL_EDGE_INCLUDED

#if !defined(SHADERGRAPH_PREVIEW)
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
#endif

float PencilHash(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float PencilNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    float a = PencilHash(i);
    float b = PencilHash(i + float2(1, 0));
    float c = PencilHash(i + float2(0, 1));
    float d = PencilHash(i + float2(1, 1));
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

#if !defined(SHADERGRAPH_PREVIEW)
// ระยะจากกล้อง (world unit) รองรับทั้ง perspective และ orthographic (กล้อง isometric ของเกมเป็น ortho: depth เป็นเส้นตรงอยู่แล้ว)
float PencilEyeDepth(float2 uv)
{
    float raw = SampleSceneDepth(uv);
    if (unity_OrthoParams.w > 0.5)
    {
#if UNITY_REVERSED_Z
        raw = 1.0 - raw;
#endif
        return lerp(_ProjectionParams.y, _ProjectionParams.z, raw);
    }
    return LinearEyeDepth(raw, _ZBufferParams);
}

// 0..1 ความเป็นขอบที่ uv (offset = ครึ่งความหนาเส้นเป็น uv)
float PencilEdgeAt(float2 uv, float2 offset, float depthThreshold, float normalThreshold)
{
    float2 uvA = uv + float2(-offset.x, -offset.y);
    float2 uvB = uv + float2( offset.x,  offset.y);
    float2 uvC = uv + float2( offset.x, -offset.y);
    float2 uvD = uv + float2(-offset.x,  offset.y);

    // depth: ระยะต่างกันกี่ world unit (ขั้น/ขอบที่สูงต่างกันเกิน DepthThreshold = มีเส้น)
    float dA = PencilEyeDepth(uvA);
    float dB = PencilEyeDepth(uvB);
    float dC = PencilEyeDepth(uvC);
    float dD = PencilEyeDepth(uvD);
    float depthDiff = sqrt((dA - dB) * (dA - dB) + (dC - dD) * (dC - dD));
    float depthEdge = smoothstep(depthThreshold, depthThreshold * 1.5 + 1e-4, depthDiff);

    // normal: มุมหักของผิว (ขอบขั้นบันได มุมกล่อง)
    float3 nA = SampleSceneNormals(uvA);
    float3 nB = SampleSceneNormals(uvB);
    float3 nC = SampleSceneNormals(uvC);
    float3 nD = SampleSceneNormals(uvD);
    float normalDiff = sqrt(dot(nA - nB, nA - nB) + dot(nC - nD, nC - nD));
    float normalEdge = smoothstep(normalThreshold, normalThreshold * 1.5 + 1e-4, normalDiff);

    return max(depthEdge, normalEdge);
}
#endif

// ScreenPos     = Screen Position (Default) / BaseColor = สีเดิมก่อนมีเส้น
// PencilColor.a = ความทึบเส้น / Strength = คูณความเข้มเส้นรวม
// Thickness     = ความหนาเส้น (pixel)
// DepthThreshold = ระยะต่างกี่ world unit ถึงเป็นขอบ / NormalThreshold = มุมผิวต่างแค่ไหนถึงเป็นขอบ (ต่ำ = เส้นเยอะ)
// Jitter        = เส้นส่ายได้กี่ pixel / BoilFPS = เส้นเปลี่ยนรูปกี่ครั้งต่อวินาที (0 = นิ่ง)
// Grain         = เนื้อดินสอเป็นเสี้ยน/ขาดช่วง (0 = เส้นเรียบ)
// boil: สุ่มใหม่เป็นจังหวะ ไม่ใช่ทุกเฟรม (ดูเหมือนวาดใหม่ทีละภาพ)
float2 PencilBoilSeed(float boilFPS)
{
    float frame = boilFPS > 0.0 ? floor(_Time.y * boilFPS) : 0.0;
    return float2(frame * 17.13, frame * 7.71);
}

// เนื้อ graphite ตาม pixel บนจอ: เสี้ยนแนวเฉียง (เข้ม/อ่อนสลับ) + ขาดช่วงสั้นๆ บางจุด / 1 = เข้มเต็ม
float PencilGrainMask(float2 px, float2 seed)
{
    float2 streakUV = float2(px.x * 0.7 + px.y * 0.45, px.y * 0.06 - px.x * 0.04);
    float streak = PencilNoise(streakUV + seed * 0.37);
    float breakup = PencilNoise(px / 11.0 + seed * 1.3);
    return (0.6 + 0.4 * streak) * lerp(0.25, 1.0, smoothstep(0.1, 0.28, breakup));
}

void PencilEdge_float(float4 ScreenPos, float3 BaseColor, float4 PencilColor, float Strength, float Thickness,
    float DepthThreshold, float NormalThreshold, float Jitter, float BoilFPS, float Grain, out float3 Out)
{
#if defined(SHADERGRAPH_PREVIEW)
    Out = BaseColor;
#else
    float2 uv = ScreenPos.xy;
    float2 texel = 1.0 / _ScreenParams.xy;
    float2 px = uv * _ScreenParams.xy;
    float2 seed = PencilBoilSeed(BoilFPS);

    // เส้นส่าย: เลื่อนจุดที่ใช้หาขอบด้วย noise ความถี่ต่ำ -> เส้นคดเคี้ยวเล็กน้อยแทนตรงเป๊ะ
    // วาด 2 เส้นซ้อนที่ส่ายคนละแบบ (เส้นที่ 2 จางกว่า) -> ดูเป็นลายเส้นร่างด้วยมือ
    float2 offset = texel * max(Thickness, 0.5) * 0.5;
    float2 wobbleA = float2(PencilNoise(px / 24.0 + seed), PencilNoise(px / 24.0 + seed + 31.7)) - 0.5;
    float2 wobbleB = float2(PencilNoise(px / 17.0 + seed + 71.3), PencilNoise(px / 17.0 + seed + 13.9)) - 0.5;
    float edgeA = PencilEdgeAt(uv + wobbleA * 2.0 * Jitter * texel, offset, DepthThreshold, NormalThreshold);
    float edgeB = PencilEdgeAt(uv + wobbleB * 3.0 * Jitter * texel, offset * 0.7, DepthThreshold, NormalThreshold);
    float edge = max(edgeA, edgeB * 0.7);

    // เร่ง contrast ให้แกนเส้นเข้มเต็ม ขอบเส้นยังไล่อ่อนเหมือนดินสอ
    edge = saturate(edge * lerp(1.0, PencilGrainMask(px, seed), saturate(Grain)) * 1.8);

    Out = lerp(BaseColor, PencilColor.rgb, saturate(edge * Strength * PencilColor.a));
#endif
}

void PencilEdge_half(float4 ScreenPos, float3 BaseColor, float4 PencilColor, float Strength, float Thickness,
    float DepthThreshold, float NormalThreshold, float Jitter, float BoilFPS, float Grain, out float3 Out)
{
    PencilEdge_float(ScreenPos, BaseColor, PencilColor, Strength, Thickness, DepthThreshold, NormalThreshold, Jitter, BoilFPS, Grain, Out);
}

// แบบมีเส้นตาราง (GrassGrid_Pencil): วาดเส้นตาราง (LineMask 0..1, LineColor) ด้วยเนื้อดินสอก่อน แล้วค่อยเติมเส้นขอบ
void PencilEdgeGrid_float(float4 ScreenPos, float3 BaseColor, float4 PencilColor, float Strength, float Thickness,
    float DepthThreshold, float NormalThreshold, float Jitter, float BoilFPS, float Grain,
    float LineMask, float4 LineColor, out float3 Out)
{
    float lineAmount = saturate(LineMask);
#if !defined(SHADERGRAPH_PREVIEW)
    // เนื้อ graphite ชุดเดียวกับเส้นขอบ (สุ่มคนละตำแหน่ง) -> เส้นตารางเป็นเสี้ยน/ขาดช่วงเหมือนลากดินสอ
    float2 px = ScreenPos.xy * _ScreenParams.xy;
    lineAmount *= lerp(1.0, PencilGrainMask(px + 57.3, PencilBoilSeed(BoilFPS)), saturate(Grain));
#endif
    float3 gridColor = lerp(BaseColor, LineColor.rgb, lineAmount);
    PencilEdge_float(ScreenPos, gridColor, PencilColor, Strength, Thickness, DepthThreshold, NormalThreshold, Jitter, BoilFPS, Grain, Out);
}

void PencilEdgeGrid_half(float4 ScreenPos, float3 BaseColor, float4 PencilColor, float Strength, float Thickness,
    float DepthThreshold, float NormalThreshold, float Jitter, float BoilFPS, float Grain,
    float LineMask, float4 LineColor, out float3 Out)
{
    PencilEdgeGrid_float(ScreenPos, BaseColor, PencilColor, Strength, Thickness, DepthThreshold, NormalThreshold, Jitter, BoilFPS, Grain, LineMask, LineColor, Out);
}

// jitter เส้นตารางแบบเดียวกับเส้นขอบ: เลื่อนพิกัดตารางไปเท่ากับการเลื่อนบนจอ ±Jitter pixel (noise ชุดเดียวกับ wobbleA ของเส้นขอบ)
// -> เส้นตารางกับเส้นขอบสั่น/boil ไปพร้อมกัน / ใช้ ddx/ddy แปลงระยะบนจอเป็นระยะในพิกัดตาราง
void PencilGridJitter_float(float2 GridUV, float4 ScreenPos, float Jitter, float BoilFPS, out float2 Out)
{
#if defined(SHADERGRAPH_PREVIEW)
    Out = GridUV;
#else
    float2 px = ScreenPos.xy * _ScreenParams.xy;
    float2 seed = PencilBoilSeed(BoilFPS);
    float2 j = (float2(PencilNoise(px / 24.0 + seed), PencilNoise(px / 24.0 + seed + 31.7)) - 0.5) * 2.0 * Jitter;
    Out = GridUV + ddx(GridUV) * j.x + ddy(GridUV) * j.y;
#endif
}

void PencilGridJitter_half(float2 GridUV, float4 ScreenPos, float Jitter, float BoilFPS, out float2 Out)
{
    PencilGridJitter_float(GridUV, ScreenPos, Jitter, BoilFPS, Out);
}

#endif

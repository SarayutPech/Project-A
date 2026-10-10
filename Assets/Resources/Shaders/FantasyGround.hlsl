#ifndef PROJECTA_FANTASY_GROUND_INCLUDED
#define PROJECTA_FANTASY_GROUND_INCLUDED

// พื้นหญ้าแฟนตาซี "ดินแดนสายฟ้า" — คำนวณทุกอย่างจาก World Position (XZ) จึงไม่ต้องพึ่ง UV
// และ tile ต่อเนื่องข้าม mesh/chunk ของแมพ (ไม่มีรอยต่อ)  ใช้ผ่าน Custom Function ใน FantasyGround_Lightning.shadergraph

float FG_Hash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float2 FG_Hash22(float2 p)
{
    float n = FG_Hash21(p);
    return float2(n, FG_Hash21(p + n + 19.19));
}

float FG_ValueNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    float a = FG_Hash21(i);
    float b = FG_Hash21(i + float2(1, 0));
    float c = FG_Hash21(i + float2(0, 1));
    float d = FG_Hash21(i + float2(1, 1));
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

float FG_Fbm(float2 p)
{
    float v = 0.0;
    float a = 0.5;
    [unroll] for (int i = 0; i < 4; i++)
    {
        v += a * FG_ValueNoise(p);
        p = p * 2.03 + 11.7;
        a *= 0.5;
    }
    return v;
}

// Voronoi: x = ระยะถึงจุดที่ใกล้สุด (F1), y = ระยะถึงจุดที่ใกล้เป็นอันดับสอง (F2), z = hash ของเซลล์ที่ใกล้สุด
float3 FG_Voronoi(float2 p)
{
    float2 ip = floor(p);
    float2 fp = frac(p);
    float f1 = 8.0, f2 = 8.0, id = 0.0;
    [unroll] for (int y = -1; y <= 1; y++)
    {
        [unroll] for (int x = -1; x <= 1; x++)
        {
            float2 g = float2(x, y);
            float2 o = FG_Hash22(ip + g);
            float d = length(g + o - fp);
            if (d < f1) { f2 = f1; f1 = d; id = FG_Hash21(ip + g + 7.7); }
            else if (d < f2) { f2 = d; }
        }
    }
    return float3(f1, f2, id);
}

void FantasyGround_float(
    float3 WorldPos,
    float3 DeepColor, float3 GrassColor, float3 TipColor, float3 StormColor, float3 LightningColor,
    float Scale, float BladeDensity, float PatchStrength,
    float CrackScale, float CrackWidth, float CrackCoverage,
    float GlowIntensity, float PulseSpeed, float SparkAmount, float BaseSmoothness,
    out float3 BaseColor, out float3 Emission, out float Smoothness, out float Occlusion)
{
    float2 p = WorldPos.xz * Scale;
    float t = _TimeParameters.x;

    // --- ปื้นสีใหญ่: ไล่จากเขียวเข้ม -> เขียวสด และมีปื้น "พายุ" สีม่วงน้ำเงินแทรก ---
    float patches = FG_Fbm(p * 0.35 + 3.1);
    float storm = smoothstep(0.52, 0.78, FG_Fbm(p * 0.22 + 17.3)) * PatchStrength;

    // --- ใบหญ้า: เส้นยาวสองทิศทางซ้อนกัน + กอหญ้า (voronoi) ให้ปลายใบสว่างกว่าโคน ---
    float2 bp = p * BladeDensity;
    float bladeA = FG_ValueNoise(float2(bp.x * 4.5, bp.y * 0.9));
    float2 r = float2(bp.x * 0.8 - bp.y * 0.6, bp.x * 0.6 + bp.y * 0.8);
    float bladeB = FG_ValueNoise(float2(r.x * 4.0, r.y * 1.0) + 5.3);
    float blades = smoothstep(0.30, 0.72, (bladeA + bladeB) * 0.5);

    float3 tuftV = FG_Voronoi(bp * 0.9);
    float tuft = pow(saturate(1.0 - tuftV.x * 1.4), 3.0);

    float3 col = lerp(DeepColor, GrassColor, saturate(patches * 1.3 + blades * 0.35));
    col = lerp(col, TipColor, saturate(tuft * blades * 1.4) * (0.35 + 0.65 * patches));
    col = lerp(col, StormColor, storm);
    col *= lerp(0.72, 1.12, blades);

    // --- รอยแตกสายฟ้า: ขอบของ voronoi ที่ถูกบิดด้วย noise เพื่อให้เป็นเส้นหัก ๆ ---
    float2 cp = p * CrackScale;
    cp += (float2(FG_Fbm(cp * 3.0), FG_Fbm(cp * 3.0 + 9.1)) - 0.5) * 0.35;
    float3 cr = FG_Voronoi(cp);
    float edge = cr.y - cr.x;
    float zone = smoothstep(1.0 - CrackCoverage, 1.0 - CrackCoverage + 0.2, FG_Fbm(p * 0.07 + 5.0));
    float core = (1.0 - smoothstep(0.0, CrackWidth, edge)) * zone;
    float halo = (1.0 - smoothstep(0.0, CrackWidth * 3.0, edge)) * zone;

    // พัลส์วิ่งตามแต่ละเซลล์ + วูบแบบสายฟ้าฟาด
    float phase = cr.z * 6.2831 + dot(p, float2(0.35, 0.2));
    float pulse = 0.55 + 0.45 * sin(t * PulseSpeed + phase);
    float flash = pow(saturate(sin(t * PulseSpeed * 0.37 + cr.z * 40.0)), 12.0);
    float energy = pulse + flash * 1.5;

    // ประกายไฟฟ้าสถิตกระพริบเล็ก ๆ ในโซนเดียวกัน
    float2 sp = floor(p * 28.0);
    float spark = step(0.997, FG_Hash21(sp + floor(t * 3.0))) * zone * SparkAmount;

    col = lerp(col, col * 0.25, core * 0.8);                       // ร่องแตกเข้มลง
    col = lerp(col, StormColor * 0.6, halo * 0.35);                // รอบ ๆ ร่องอมม่วง
    BaseColor = col;

    Emission = LightningColor * (core * energy + halo * 0.06 * pulse + spark * 3.0) * GlowIntensity;
    Smoothness = saturate(BaseSmoothness + core * 0.45 + storm * 0.1);
    Occlusion = lerp(0.65, 1.0, blades) * (1.0 - core * 0.3);
}

void FantasyGround_half(
    half3 WorldPos,
    half3 DeepColor, half3 GrassColor, half3 TipColor, half3 StormColor, half3 LightningColor,
    half Scale, half BladeDensity, half PatchStrength,
    half CrackScale, half CrackWidth, half CrackCoverage,
    half GlowIntensity, half PulseSpeed, half SparkAmount, half BaseSmoothness,
    out half3 BaseColor, out half3 Emission, out half Smoothness, out half Occlusion)
{
    float3 b, e; float s, o;
    FantasyGround_float(WorldPos, DeepColor, GrassColor, TipColor, StormColor, LightningColor,
        Scale, BladeDensity, PatchStrength, CrackScale, CrackWidth, CrackCoverage,
        GlowIntensity, PulseSpeed, SparkAmount, BaseSmoothness, b, e, s, o);
    BaseColor = b; Emission = e; Smoothness = s; Occlusion = o;
}


// ผนังหน้าผา: หินม่วงน้ำเงินเป็นชั้น ๆ + ตะไคร่เขียว + เส้นสายฟ้าเรือง ๆ ตามรอยแตก
void FantasyCliff_float(
    float3 WorldPos,
    float3 RockDark, float3 RockLight, float3 MossColor, float3 LightningColor,
    float Scale, float StrataStrength, float MossAmount,
    float GlowIntensity, float PulseSpeed, float BaseSmoothness,
    out float3 BaseColor, out float3 Emission, out float Smoothness, out float Occlusion)
{
    // ฉายลงระนาบแนวตั้ง: แกนนอน = x+z (ผนังวางตามแกนใดแกนหนึ่ง), แกนตั้ง = y
    float2 p = float2(WorldPos.x + WorldPos.z, WorldPos.y) * Scale;
    float t = _TimeParameters.x;

    float strata = FG_ValueNoise(float2(p.x * 0.5, p.y * 3.0));
    float bands = smoothstep(0.35, 0.65, frac(p.y * 1.7 + strata * 0.8));
    float grain = FG_Fbm(p * 5.0);
    float rock = saturate(grain * 0.8 + bands * StrataStrength * 0.5);
    float3 col = lerp(RockDark, RockLight, rock);

    float moss = smoothstep(0.55, 0.8, FG_Fbm(p * 1.3 + 4.0)) * MossAmount;
    col = lerp(col, MossColor, moss);

    float2 cp = p * 0.9;
    cp += (float2(FG_Fbm(cp * 2.5), FG_Fbm(cp * 2.5 + 9.1)) - 0.5) * 0.4;
    float3 cr = FG_Voronoi(cp);
    float edge = cr.y - cr.x;
    float zone = smoothstep(0.55, 0.75, FG_Fbm(p * 0.35 + 8.0));
    float core = (1.0 - smoothstep(0.0, 0.05, edge)) * zone;
    float pulse = 0.55 + 0.45 * sin(t * PulseSpeed + cr.z * 6.2831);

    col = lerp(col, col * 0.3, core * 0.7);
    BaseColor = col;
    Emission = LightningColor * core * pulse * GlowIntensity;
    Smoothness = saturate(BaseSmoothness + core * 0.3);
    Occlusion = lerp(0.6, 1.0, rock);
}

void FantasyCliff_half(
    half3 WorldPos,
    half3 RockDark, half3 RockLight, half3 MossColor, half3 LightningColor,
    half Scale, half StrataStrength, half MossAmount,
    half GlowIntensity, half PulseSpeed, half BaseSmoothness,
    out half3 BaseColor, out half3 Emission, out half Smoothness, out half Occlusion)
{
    float3 b, e; float s, o;
    FantasyCliff_float(WorldPos, RockDark, RockLight, MossColor, LightningColor,
        Scale, StrataStrength, MossAmount, GlowIntensity, PulseSpeed, BaseSmoothness, b, e, s, o);
    BaseColor = b; Emission = e; Smoothness = s; Occlusion = o;
}


// fade ผนังขอบเกาะแบบ dither: ต่อท้าย OcclusionCutout (รับ Alpha/ClipThreshold ของมัน) แล้วคูณความทึบจาก vertex color.r
// ความจางไล่จาก 1 (ขอบบน) ไป 0 (ก้นผนัง) ยกกำลัง FadePower: ยิ่งมากยิ่งจางเร็วจากด้านบน
// ใช้ Bayer 4x4 เทียบกับความทึบ (material ยังเป็น opaque + alpha clip ไม่ต้องสลับเป็น transparent -> ไม่มีปัญหาลำดับวาด)
// ถ้ากำลังเจาะวงตอนบัง (Alpha < 1) ใช้ threshold ของการเจาะแทน
float FG_Bayer4(uint2 p)
{
    static const float m[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
    return (m[(p.y & 3) * 4 + (p.x & 3)] + 0.5) / 16.0;
}

void FantasyRimFade_float(float Alpha, float Clip, float3 PositionWS, float VertexFade, float FadePower,
    out float AlphaOut, out float ClipOut)
{
    float fade = pow(saturate(VertexFade), max(FadePower, 0.01));
    AlphaOut = Alpha * fade;
    ClipOut = Clip;
#if !defined(SHADERGRAPH_PREVIEW)
    if (fade < 0.999 && Alpha >= 1.0)
    {
        float4 pCS = mul(UNITY_MATRIX_VP, float4(PositionWS, 1.0));
        float2 ndc = pCS.xy / pCS.w;
        uint2 pixel = (uint2)((ndc * 0.5 + 0.5) * _ScreenParams.xy);
        ClipOut = FG_Bayer4(pixel);
    }
#endif
}

void FantasyRimFade_half(half Alpha, half Clip, half3 PositionWS, half VertexFade, half FadePower,
    out half AlphaOut, out half ClipOut)
{
    float a, c;
    FantasyRimFade_float(Alpha, Clip, PositionWS, VertexFade, FadePower, a, c);
    AlphaOut = a; ClipOut = c;
}

#endif

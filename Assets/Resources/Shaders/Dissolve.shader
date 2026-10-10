// ตัวละครสลายเป็นเม็ด (ใช้คู่กับ DissolveEffect): ตัดพิกเซลตาม noise + ความสูง (ล่างขึ้นบน) ขอบรอยสลายเรืองแสง
// ก๊อป _BaseMap/_BaseColor จาก material เดิมให้โดย DissolveEffect -> หน้าตาใกล้เดิม (lambert + ambient ง่ายๆ)
// _Dissolve 0 = ครบตัว, 1 = หายหมด / _YMin,_YMax = ช่วงความสูงตัว (world) ที่ใช้ไล่จากเท้าขึ้นหัว
Shader "ProjectA/Dissolve"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _Dissolve ("Dissolve", Range(0, 1)) = 0
        _YMin ("Y Min (world)", Float) = 0
        _YMax ("Y Max (world)", Float) = 2
        _NoiseScale ("Noise Scale (cells per unit)", Float) = 3
        _NoiseWeight ("Noise Weight", Range(0, 1)) = 0.45
        _EdgeWidth ("Edge Width", Range(0, 0.3)) = 0.06
        [HDR] _EdgeColor ("Edge Color", Color) = (0.3, 2.2, 3, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _Dissolve;
            float _YMin;
            float _YMax;
            float _NoiseScale;
            half _NoiseWeight;
            half _EdgeWidth;
            half4 _EdgeColor;
        CBUFFER_END

        float Hash13(float3 p)
        {
            p = frac(p * 0.1031);
            p += dot(p, p.zyx + 31.32);
            return frac((p.x + p.y) * p.z);
        }

        // value noise 3 มิติ (0..1)
        float Noise3(float3 p)
        {
            float3 i = floor(p);
            float3 f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            float n000 = Hash13(i);
            float n100 = Hash13(i + float3(1, 0, 0));
            float n010 = Hash13(i + float3(0, 1, 0));
            float n110 = Hash13(i + float3(1, 1, 0));
            float n001 = Hash13(i + float3(0, 0, 1));
            float n101 = Hash13(i + float3(1, 0, 1));
            float n011 = Hash13(i + float3(0, 1, 1));
            float n111 = Hash13(i + float3(1, 1, 1));
            return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y),
                        lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
        }

        // ค่าเกณฑ์ของจุดนี้: เล็กที่เท้า -> สลายก่อน (ผสม noise ให้ขอบขรุขระ)
        float DissolveValue(float3 positionWS)
        {
            float h = saturate((positionWS.y - _YMin) / max(_YMax - _YMin, 0.0001));
            float n = Noise3(positionWS * _NoiseScale);
            return lerp(h, n, _NoiseWeight);
        }

        float DissolveThreshold()
        {
            return _Dissolve * (1.0 + _EdgeWidth);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float s = DissolveValue(i.positionWS);
                float t = DissolveThreshold();
                clip(s - t);

                half3 n = normalize(i.normalWS);
                Light l = GetMainLight();
                half3 lighting = l.color * saturate(dot(n, l.direction)) + SampleSH(n);
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb;
                half3 col = albedo * lighting;

                // ขอบรอยสลายเรืองแสง (จางเข้าตอน _Dissolve เพิ่งเริ่ม ไม่ให้เท้าเรืองตั้งแต่ 0)
                half edge = 1.0h - saturate((s - t) / max(_EdgeWidth, 0.0001));
                edge *= saturate(_Dissolve * 30.0h);
                col += _EdgeColor.rgb * edge;
                return half4(col, 1);
            }
            ENDHLSL
        }

        // เงาตัดตามรอยสลายด้วย (ไม่งั้นเงาค้างเต็มตัวทั้งที่ตัวหายแล้ว)
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings vertShadow(Attributes input)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                o.positionCS = positionCS;
                o.positionWS = positionWS;
                return o;
            }

            half4 fragShadow(Varyings i) : SV_Target
            {
                clip(DissolveValue(i.positionWS) - DissolveThreshold());
                return 0;
            }
            ENDHLSL
        }
    }
}

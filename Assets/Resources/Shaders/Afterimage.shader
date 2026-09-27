// เงาตามหลังตอน dash (แบบ Rockman): สีเดียวโปร่งแสง ขอบเรืองกว่าตรงกลาง (fresnel) เลือก additive / alpha blend ได้
// alpha ของ _Color ใช้ fade (ตั้งผ่าน MaterialPropertyBlock ต่อเงา)
Shader "ProjectA/Afterimage"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.35, 0.75, 1)
        _CoreOpacity ("Core Opacity", Range(0, 1)) = 0.35
        _RimPower ("Rim Power", Range(0.5, 8)) = 2
        // One = บวกแสง เรืองแบบ Rockman (ฉากสว่างจะจาง) / OneMinusSrcAlpha = โปร่งแสงปกติ เห็นสีชัดบนฉากสว่าง
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Blend (Dst)", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Afterimage"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha [_DstBlend]
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _CoreOpacity;
                half _RimPower;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewDirWS : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.viewDirWS = GetWorldSpaceViewDir(positionWS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half ndv = saturate(dot(normalize(i.normalWS), normalize(i.viewDirWS)));
                half rim = pow(1.0h - ndv, _RimPower);
                half a = lerp(_CoreOpacity, 1.0h, rim) * _Color.a;
                return half4(_Color.rgb, a);
            }
            ENDHLSL
        }
    }
}

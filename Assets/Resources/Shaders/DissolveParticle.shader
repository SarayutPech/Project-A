// เม็ดจุดเรืองแสงของ DissolveEffect: quad กลมๆ ขอบนุ่ม additive สีจาก vertex color (startColor ของ particle) x _Color
Shader "ProjectA/DissolveParticle"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (0.3, 2.2, 3, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Dot"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.color = input.color;
                o.uv = input.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half d = length(i.uv - 0.5) * 2.0;
                half a = smoothstep(1.0h, 0.35h, d);
                return half4(i.color.rgb * _Color.rgb, a * i.color.a);
            }
            ENDHLSL
        }
    }
}

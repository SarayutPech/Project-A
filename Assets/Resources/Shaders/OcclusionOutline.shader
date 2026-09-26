// แสดงเฉพาะเส้นขอบของ object (ข้างในโปร่งใส) ใช้กับ object ที่บัง player
// Pass 1 (OutlineMask): เขียน stencil เป็นรูปทรงของ mesh จริง ไม่วาดสี
// Pass 2 (OutlineHull): วาด mesh ที่ขยายออกตาม normal (ความกว้างคงที่เป็น pixel) เฉพาะจุดที่ stencil ไม่ได้ถูก mark
//                      จึงเหลือแค่วงแหวนรอบขอบ
// URP รัน pass SRPDefaultUnlit ก่อน UniversalForward ของ object เดียวกัน stencil จึงพร้อมก่อนวาดขอบ
Shader "Custom/OcclusionOutline"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (1, 1, 1, 1)
        _OutlineWidth ("Outline Width (px)", Range(0, 10)) = 2
        [IntRange] _StencilRef ("Stencil Ref", Range(1, 255)) = 64
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent+10"
            "IgnoreProjector" = "True"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _OutlineColor;
            float _OutlineWidth;
            float _StencilRef;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            UNITY_VERTEX_OUTPUT_STEREO
        };
        ENDHLSL

        Pass
        {
            Name "OutlineMask"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            ColorMask 0
            ZWrite Off
            ZTest Always // mark ทั้งรูปทรงแม้บางส่วนโดนอย่างอื่นบัง ข้างในจะได้ไม่มีเส้นขอบโผล่
            Cull Off

            Stencil
            {
                Ref [_StencilRef]
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "OutlineHull"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off // ใบไม้/mesh แผ่นบางๆ ก็มีขอบ

            Stencil
            {
                Ref [_StencilRef]
                Comp NotEqual
                Pass Keep
            }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float4 positionCS = TransformObjectToHClip(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float2 normalCS = mul((float3x3)UNITY_MATRIX_VP, normalWS).xy;

                // ขยายใน clip space: คูณ w ให้ความกว้างคงที่เป็น pixel ไม่ว่าใกล้หรือไกล (ortho w = 1)
                float len = length(normalCS);
                if (len > 1e-5)
                {
                    float2 offset = (normalCS / len) * (_OutlineWidth * 2.0) / _ScreenParams.xy;
                    positionCS.xy += offset * positionCS.w;
                }

                output.positionCS = positionCS;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }
    }
}

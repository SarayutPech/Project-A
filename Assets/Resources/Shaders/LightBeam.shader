// ลำแสงพุ่งฟ้า/ลงดิน ของ DissolveEffect: ท่อทรงกระบอก (mesh สูง 1 หน่วย แกน Y ขึ้น ไม่มีฝา) additive
// แสดงเฉพาะช่วง _Tail.._Head ของความสูงตามสัดส่วน (0..1) ปลายนุ่ม -> ทำเป็น "หัวลำแสงที่วิ่ง" ได้ด้วยการเลื่อน _Tail/_Head
// กลางลำสว่างกว่าขอบ (มองด้านข้าง = fresnel) มีริ้วเลื่อนขึ้นช้าๆ _Intensity ใช้ fade ทั้งลำ
Shader "ProjectA/LightBeam"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (0.4, 1.6, 3, 1)
        _Tail ("Tail (0..1)", Range(0, 1)) = 0
        _Head ("Head (0..1)", Range(0, 1)) = 1
        _Soft ("End Softness", Range(0.001, 0.5)) = 0.06
        _Intensity ("Intensity", Range(0, 2)) = 1
        _CorePower ("Core Power", Range(0.2, 6)) = 1.5
        _StripeSpeed ("Stripe Speed", Float) = 6
        _StripeTime ("Stripe Time (script)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Beam"
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
                half _Tail;
                half _Head;
                half _Soft;
                half _Intensity;
                half _CorePower;
                half _StripeSpeed;
                float _StripeTime;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewDirWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.viewDirWS = GetWorldSpaceViewDir(positionWS);
                o.uv = input.uv; // x = รอบเส้นรอบวง 0..1, y = ความสูง 0..1
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half y = i.uv.y;
                half seg = smoothstep(_Tail, _Tail + _Soft, y) * (1.0h - smoothstep(_Head - _Soft, _Head, y));
                // กลางลำสว่าง ขอบนุ่ม
                half ndv = abs(dot(normalize(i.normalWS), normalize(i.viewDirWS)));
                half core = pow(saturate(ndv), _CorePower);
                // ริ้วแสงวิ่งขึ้น
                half stripe = 0.78h + 0.22h * sin(y * 90.0h - _StripeTime * _StripeSpeed + i.uv.x * 12.566h);
                // ปลายฟ้าจางลง ฐานสว่าง
                half sky = 1.0h - smoothstep(0.55h, 1.0h, y) * 0.7h;
                half a = saturate(seg * core * stripe * sky * _Intensity);
                return half4(_Color.rgb, a);
            }
            ENDHLSL
        }
    }
}

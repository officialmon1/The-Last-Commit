// 单片花瓣（URP 粒子版）：在一个 Quad 上用形状函数抠出带缺口的樱花花瓣。
// 翻滚/抖动由 ParticleSystem 的 Rotation over Lifetime 负责；颜色来自粒子顶点色。
Shader "FlowerDeath/Petal"
{
    Properties
    {
        _Curl("Curl", Float) = 0.35
        _NotchDepth("Notch Depth", Range(0, 0.4)) = 0.14
        _EmissionStrength("Emission", Float) = 0.35
        _BacklightStrength("Backlight", Float) = 0.6
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "AlphaTest" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float _Curl;
            float _NotchDepth;
            float _EmissionStrength;
            float _BacklightStrength;
        CBUFFER_END

        // uv -> 花瓣形状。返回 >0 表示在花瓣内。x: -1..1 横向，t: 0 = 根部，1 = 顶端
        float PetalMask(float2 uv, out float t, out float ax)
        {
            float x = uv.x * 2.0 - 1.0;
            t = 1.0 - uv.y;
            ax = abs(x);
            float halfWidth = 0.95 * sin(3.14159 * pow(saturate(t), 0.72));
            halfWidth *= lerp(0.55, 1.0, smoothstep(0.0, 0.35, t)); // 根部更窄
            float inside = halfWidth - ax;
            // 顶端的 V 形缺口
            float notchW = 0.32;
            float notch = t - (1.0 - _NotchDepth * (1.0 - ax / notchW));
            if (ax < notchW && notch > 0.0) inside = -1.0;
            return inside;
        }

        float3 CurlVertex(float3 positionOS)
        {
            // 卷曲：两侧向 +Z 翘起，像真花瓣一样不是平板（Quad 的 x 范围 -0.5..0.5）
            positionOS.z += _Curl * positionOS.x * positionOS.x * 2.0;
            return positionOS;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                half4  color      : COLOR;
                float  fog        : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(CurlVertex(input.positionOS.xyz));
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.uv = input.uv;
                o.color = input.color;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings input, bool isFront : SV_IsFrontFace) : SV_Target
            {
                float t, ax;
                clip(PetalMask(input.uv, t, ax));

                half3 c = input.color.rgb;
                // 根部稍深、顶端稍浅，中间一条淡淡的脉络
                float shade = lerp(0.78, 1.06, smoothstep(0.0, 0.8, t));
                float vein = 1.0 - 0.12 * (1.0 - smoothstep(0.0, 0.06, ax)) * (1.0 - t);
                half3 albedo = c * shade * vein;

                half3 n = normalize(input.normalWS);
                if (!isFront) n = -n;
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half ndl = dot(n, mainLight.direction);
                // 正面受光 + 背面透光
                half3 lit = mainLight.color * mainLight.shadowAttenuation * (saturate(ndl) + _BacklightStrength * saturate(-ndl));
                half3 ambient = SampleSHPixel(half3(0, 0, 0), n);
                half3 col = albedo * (lit + ambient) + albedo * _EmissionStrength;
                col = MixFog(col, input.fog);
                return half4(col, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings ShadowVert(Attributes input)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(CurlVertex(input.positionOS.xyz));
                float3 n = TransformObjectToWorldNormal(input.normalOS);
                o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(ws, n, _LightDirection)));
                o.uv = input.uv;
                return o;
            }

            half4 ShadowFrag(Varyings input) : SV_Target
            {
                float t, ax;
                clip(PetalMask(input.uv, t, ax));
                return 0;
            }
            ENDHLSL
        }
    }
    FallBack Off
}

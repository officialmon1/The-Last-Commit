// Body dissolve shader (URP): erodes by "height + 3D noise", with a petal-colored glowing edge.
// _Progress is advanced every frame by FlowerDissolve.cs. The noise function is identical to the C# side,
// so the CPU can predict when each surface point disappears and petals spawn exactly at the dissolve edge.
Shader "FlowerDeath/Dissolve"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (0.85, 0.82, 0.78, 1)
        _BaseMap("Base Map", 2D) = "white" {}
        _Smoothness("Smoothness", Range(0, 1)) = 0.3
        _Metallic("Metallic", Range(0, 1)) = 0.0

        _Progress("Progress", Float) = -1.0
        _MinY("Min Y (world)", Float) = 0.0
        _MaxY("Max Y (world)", Float) = 2.0
        _TopDown("Top Down", Float) = 1.0
        _HeightWeight("Height Weight", Range(0, 1)) = 0.65
        _NoiseScale("Noise Scale", Float) = 5.0
        _EdgeWidth("Edge Width", Range(0, 0.3)) = 0.05
        [HDR] _EdgeColor("Edge Color", Color) = (1.0, 0.58, 0.78, 1)
        _EdgeEmission("Edge Emission", Float) = 5.0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "AlphaTest" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _BaseMap_ST;
            float _Smoothness;
            float _Metallic;
            float _Progress;
            float _MinY;
            float _MaxY;
            float _TopDown;
            float _HeightWeight;
            float _NoiseScale;
            float _EdgeWidth;
            float4 _EdgeColor;
            float _EdgeEmission;
            // World -> target local. The dissolve is computed in target local space so the pattern does not drift while the character/hands move.
            float4x4 _WorldToTarget;
            // Spread mode: distance to _Origin (target local) replaces height, e.g. spreading from the hands over the body
            float4 _Origin;
            float _MaxDist;
            float _UseOrigin;
        CBUFFER_END

        // 32-bit integer hash (lowbias32 variant). Must stay identical to Hash3 in FlowerDissolve.cs.
        float Hash3(int3 c)
        {
            uint3 u = uint3(c + int3(4096, 4096, 4096));
            uint h = (u.x * 73856093u) ^ (u.y * 19349663u) ^ (u.z * 83492791u);
            h ^= h >> 16u;
            h *= 2146121005u;
            h ^= h >> 15u;
            h *= 2221713035u;
            h ^= h >> 16u;
            return float(h) / 4294967295.0;
        }

        float ValueNoise(float3 p)
        {
            float3 i = floor(p);
            float3 f = p - i;
            float3 u = f * f * (3.0 - 2.0 * f);
            int3 c = int3(i);
            float n000 = Hash3(c);
            float n100 = Hash3(c + int3(1, 0, 0));
            float n010 = Hash3(c + int3(0, 1, 0));
            float n110 = Hash3(c + int3(1, 1, 0));
            float n001 = Hash3(c + int3(0, 0, 1));
            float n101 = Hash3(c + int3(1, 0, 1));
            float n011 = Hash3(c + int3(0, 1, 1));
            float n111 = Hash3(c + int3(1, 1, 1));
            float x00 = lerp(n000, n100, u.x);
            float x10 = lerp(n010, n110, u.x);
            float x01 = lerp(n001, n101, u.x);
            float x11 = lerp(n011, n111, u.x);
            return lerp(lerp(x00, x10, u.y), lerp(x01, x11, u.y), u.z);
        }

        float Fbm(float3 p)
        {
            float n = 0.5 * ValueNoise(p);
            n += 0.25 * ValueNoise(p * 2.03 + 17.1);
            n += 0.125 * ValueNoise(p * 4.01 + 31.7);
            return n / 0.875;
        }

        float DissolveValue(float3 positionWS)
        {
            float3 p = mul(_WorldToTarget, float4(positionWS, 1.0)).xyz;
            float h;
            if (_UseOrigin > 0.5)
            {
                h = saturate(distance(p, _Origin.xyz) / max(_MaxDist, 0.0001));
            }
            else
            {
                h = saturate((p.y - _MinY) / max(_MaxY - _MinY, 0.0001));
                if (_TopDown > 0.5) h = 1.0 - h;
            }
            return lerp(Fbm(p * _NoiseScale), h, _HeightWeight);
        }

        // Returns the distance to the dissolve edge; pixels below 0 are discarded.
        float DissolveClip(float3 positionWS)
        {
            float d = DissolveValue(positionWS) - _Progress;
            clip(d);
            return d;
        }

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS   : NORMAL;
            float2 uv         : TEXCOORD0;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float3 normalWS   : TEXCOORD1;
            float2 uv         : TEXCOORD2;
        };

        Varyings CommonVertex(Attributes input)
        {
            Varyings o;
            o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
            o.positionCS = TransformWorldToHClip(o.positionWS);
            o.normalWS = TransformObjectToWorldNormal(input.normalOS);
            o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
            return o;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex CommonVertex
            #pragma fragment LitFragment

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            half4 LitFragment(Varyings input) : SV_Target
            {
                float d = DissolveClip(input.positionWS);

                half3 baseCol = _BaseColor.rgb * SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb;
                half edge = 1.0 - smoothstep(0.0, _EdgeWidth, d);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = NormalizeNormalPerPixel(input.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord = ComputeFogFactor(input.positionCS.z);
                inputData.bakedGI = SampleSHPixel(half3(0, 0, 0), inputData.normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = lerp(baseCol, _EdgeColor.rgb, edge);
                surfaceData.metallic = _Metallic;
                surfaceData.smoothness = _Smoothness;
                surfaceData.normalTS = half3(0, 0, 1);
                surfaceData.emission = _EdgeColor.rgb * edge * _EdgeEmission;
                surfaceData.occlusion = 1.0;
                surfaceData.alpha = 1.0;

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVertex
            #pragma fragment ShadowFragment
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            Varyings ShadowVertex(Attributes input)
            {
                Varyings o = CommonVertex(input);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDir = normalize(_LightPosition - o.positionWS);
            #else
                float3 lightDir = _LightDirection;
            #endif
                o.positionCS = TransformWorldToHClip(ApplyShadowBias(o.positionWS, o.normalWS, lightDir));
                o.positionCS = ApplyShadowClamping(o.positionCS);
                return o;
            }

            half4 ShadowFragment(Varyings input) : SV_Target
            {
                DissolveClip(input.positionWS);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex CommonVertex
            #pragma fragment DepthFragment

            half DepthFragment(Varyings input) : SV_Target
            {
                DissolveClip(input.positionWS);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex CommonVertex
            #pragma fragment DepthNormalsFragment

            half4 DepthNormalsFragment(Varyings input) : SV_Target
            {
                DissolveClip(input.positionWS);
                return half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}

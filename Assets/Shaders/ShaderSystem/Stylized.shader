Shader "Stylized"
{
    Properties
    {
        // =====================================================
        // Transition
        // =====================================================

        [Header(Transition)]

        _TransitionNoise(
            "Transition Noise",
            2D
        ) = "white" {}

        _TransitionProgress(
            "Transition Progress",
            Range(0, 1)
        ) = 1

        _TransitionSoftness(
            "Transition Softness",
            Range(0.001, 0.25)
        ) = 0.05

        _TransitionWorldSize(
            "Transition World Size",
            Float
        ) = 50

        _TransitionOffset(
            "Transition Offset",
            Vector
        ) = (0, 0, 0, 0)

        // =====================================================
        // Posterize
        // =====================================================

        [Header(Posterize)]

        _PosterizeSteps(
            "Posterize Steps",
            Range(8, 64)
        ) = 32

        _LightWeight(
            "Posterize Light Weight",
            Range(0, 1)
        ) = 0.5

        _ColorWeight(
            "Posterize Color Weight",
            Range(0, 1)
        ) = 0.2

        // =====================================================
        // Sharpen
        // =====================================================

        [Header(Sharpen)]

        _SharpenRadius(
            "Sharpen Radius",
            Range(0.5, 2.5)
        ) = 1

        _SharpenStrength(
            "Sharpen Strength",
            Range(0, 2)
        ) = 0.2
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
        }

        HLSLINCLUDE

        #pragma target 3.5

        // Core.hlsl must be included before Transition.hlsl,
        // because Transition.hlsl uses TEXTURE2D and SAMPLER.
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // Blitter input texture.
        TEXTURE2D_X(_BlitTexture);
        SAMPLER(sampler_BlitTexture);

        // Existing common libraries.
        #include "../HLSL/ColorSpace.hlsl"
        #include "../HLSL/Transition.hlsl"

        // =====================================================
        // Effect parameters
        // =====================================================

        float _PosterizeSteps;
        float _LightWeight;
        float _ColorWeight;

        float _SharpenRadius;
        float _SharpenStrength;

        // =====================================================
        // Fullscreen triangle
        // =====================================================

        struct Attributes
        {
            uint vertexID : SV_VertexID;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
        };

        Varyings Vert(Attributes input)
        {
            Varyings output;

            float2 uv = float2(
                (input.vertexID << 1) & 2,
                input.vertexID & 2
            );

            output.positionCS = float4(
                uv * 2.0 - 1.0,
                0.0,
                1.0
            );

            output.uv = uv;

            #if UNITY_UV_STARTS_AT_TOP
                output.uv.y = 1.0 - output.uv.y;
            #endif

            return output;
        }

        // =====================================================
        // Common source sampling
        // =====================================================

        float4 SampleSource(float2 uv)
        {
            return SAMPLE_TEXTURE2D_X(
                _BlitTexture,
                sampler_BlitTexture,
                uv
            );
        }

        float3 ApplyScreenTransition(
            float2 uv,
            float3 originalColor,
            float3 effectColor
        )
        {
            float noiseValue =
                SampleScreenTransition(uv);

            return ApplyTransition(
                originalColor,
                effectColor,
                noiseValue
            );
        }

        // =====================================================
        // Posterize
        // =====================================================

        float3 ApplyPosterize(float3 color)
        {
            float steps = max(
                _PosterizeSteps,
                2.0
            );

            // Perceptual lightness posterization.
            float3 originalLab =
                RGBToOKLab(color);

            float3 posterizedLab =
                originalLab;

            float quantizedLightness =
                floor(
                    originalLab.x *
                    (steps - 1.0)
                ) /
                (steps - 1.0);

            posterizedLab.x = lerp(
                originalLab.x,
                quantizedLightness,
                _LightWeight
            );

            float3 colorLab = saturate(
                OKLabToRGB(posterizedLab)
            );

            // Linear RGB posterization.
            float3 quantizedRGB =
                floor(
                    color * (steps - 1.0)
                ) /
                (steps - 1.0);

            float3 colorRGB = saturate(
                lerp(
                    color,
                    quantizedRGB,
                    _LightWeight
                )
            );

            float3 result = lerp(
                colorLab,
                colorRGB,
                _ColorWeight
            );

            return saturate(result);
        }

        half4 FragPosterize(
            Varyings input
        ) : SV_Target
        {
            float4 source =
                SampleSource(input.uv);

            float3 effectColor =
                ApplyPosterize(source.rgb);

            float3 result =
                ApplyScreenTransition(
                    input.uv,
                    source.rgb,
                    effectColor
                );

            return half4(
                result,
                source.a
            );
        }

        // =====================================================
        // Sharpen
        // =====================================================

        float3 ApplySharpen(float2 uv)
        {
            float2 texelSize =
                (1.0 / _ScreenParams.xy) *
                _SharpenRadius;

            float3 center = SampleSource(
                uv
            ).rgb;

            float3 up = SampleSource(
                uv + float2(
                    0.0,
                    texelSize.y
                )
            ).rgb;

            float3 down = SampleSource(
                uv + float2(
                    0.0,
                    -texelSize.y
                )
            ).rgb;

            float3 left = SampleSource(
                uv + float2(
                    -texelSize.x,
                    0.0
                )
            ).rgb;

            float3 right = SampleSource(
                uv + float2(
                    texelSize.x,
                    0.0
                )
            ).rgb;

            float strength =
                _SharpenStrength;

            float3 result =
                center *
                (1.0 + 4.0 * strength) -
                (
                    up +
                    down +
                    left +
                    right
                ) *
                strength;

            return saturate(result);
        }

        half4 FragSharpen(
            Varyings input
        ) : SV_Target
        {
            float4 source =
                SampleSource(input.uv);

            float3 effectColor =
                ApplySharpen(input.uv);

            float3 result =
                ApplyScreenTransition(
                    input.uv,
                    source.rgb,
                    effectColor
                );

            return half4(
                result,
                source.a
            );
        }

        ENDHLSL

        // =====================================================
        // Pass 0: Posterize
        // =====================================================

        Pass
        {
            Name "Posterize"

            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM

            #pragma vertex Vert
            #pragma fragment FragPosterize

            ENDHLSL
        }

        // =====================================================
        // Pass 1: Sharpen
        // =====================================================

        Pass
        {
            Name "Sharpen"

            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM

            #pragma vertex Vert
            #pragma fragment FragSharpen

            ENDHLSL
        }
    }

    Fallback Off
}
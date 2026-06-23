Shader "Hidden/Custom/URP/StyleTransitionComposite"
{
    Properties
    {
        [Header(Transition)]

        _TransitionNoise(
            "Transition Noise",
            2D
        ) = "white" {}

        _TransitionProgress(
            "Transition Progress",
            Range(0, 1)
        ) = 0

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
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
        }

        Pass
        {
            Name "StyleTransition"

            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM

            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // Replace this path with the actual
            // project path of Transition.hlsl.
            #include "../HLSL/Transition.hlsl"

            // Style A is automatically assigned by
            // Blitter.BlitCameraTexture.
            TEXTURE2D_X(_BlitTexture);
            SAMPLER(sampler_BlitTexture);

            // Style B is assigned by
            // PostProcessRenderFeature.
            TEXTURE2D_X(_StyleBTexture);
            SAMPLER(sampler_StyleBTexture);

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
                    output.uv.y =
                        1.0 - output.uv.y;
                #endif

                return output;
            }

            half4 Frag(
                Varyings input
            ) : SV_Target
            {
                half4 styleA =
                    SAMPLE_TEXTURE2D_X(
                        _BlitTexture,
                        sampler_BlitTexture,
                        input.uv
                    );

                half4 styleB =
                    SAMPLE_TEXTURE2D_X(
                        _StyleBTexture,
                        sampler_StyleBTexture,
                        input.uv
                    );

                float noiseValue =
                    SampleScreenTransition(
                        input.uv
                    );

                float transitionMask =
                    EvaluateTransitionMask(
                        noiseValue,
                        _TransitionProgress,
                        _TransitionSoftness
                    );

                return lerp(
                    styleA,
                    styleB,
                    transitionMask
                );
            }

            ENDHLSL
        }
    }

    Fallback Off
}
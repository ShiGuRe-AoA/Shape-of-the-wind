Shader "Custom/URP/PostProcess/GaussianBilateralBlur"
{
    Properties
    {
        _BlurRadius ("Blur Radius", Range(0, 20)) = 2
        _BlurStrength ("Blur Strength", Range(0, 1)) = 1
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
            Name "GaussianBilateralBlur"

            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM

            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D_X(_BlitTexture);
            SAMPLER(sampler_BlitTexture);

            TEXTURE2D_X_FLOAT(_CameraDepthTexture);
            SAMPLER(sampler_CameraDepthTexture);

            float _BlurRadius;
            float _BlurStrength;

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
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

            float SampleLinearDepth01(float2 uv)
            {
                float rawDepth = SAMPLE_TEXTURE2D_X(
                    _CameraDepthTexture,
                    sampler_CameraDepthTexture,
                    uv
                ).r;

                return Linear01Depth(rawDepth, _ZBufferParams);
            }

            float GaussianWeight(float x, float sigma)
            {
                return exp(-(x * x) / (2.0 * sigma * sigma));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                float2 texelSize = 1.0 / _ScreenParams.xy;

                half4 originalCol = SAMPLE_TEXTURE2D_X(
                    _BlitTexture,
                    sampler_BlitTexture,
                    uv
                );

                half3 colorSum = 0.0;
                float weightSum = 0.0;

                float sigma = max(_BlurRadius * 0.5, 0.001);

                [unroll]
                for (int y = -4; y <= 4; y++)
                {
                    [unroll]
                    for (int x = -4; x <= 4; x++)
                    {
                        float2 offset = float2(x, y);

                        float dist = length(offset);

                        if (dist > _BlurRadius)
                            continue;

                        float2 sampleUV = uv + offset * texelSize;

                        sampleUV = clamp(sampleUV, 0.001, 0.999);

                        half3 sampleCol = SAMPLE_TEXTURE2D_X(
                            _BlitTexture,
                            sampler_BlitTexture,
                            sampleUV
                        ).rgb;

                        float spatialWeight = GaussianWeight(dist, sigma);

                        float bilateralWeight = 1.0;

                        float weight = spatialWeight * bilateralWeight;

                        colorSum += sampleCol * weight;
                        weightSum += weight;
                    }
                }

                half3 blurCol = colorSum / max(weightSum, 1e-5);

                half3 finalCol = lerp(
                    originalCol.rgb,
                    blurCol,
                    _BlurStrength
                );

                return half4(finalCol, originalCol.a);
            }

            ENDHLSL
        }
    }
}
Shader "Custom/URP/Sharpen"
{
    Properties
    {
        _SharpenRadius ("Sharpen Radius", Range(0.5, 2.5)) = 1
        _SharpenStrength("Sharpen Strength", Range(0, 2)) = 0.2
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }

        Pass
        {
            Name "SharpenPosterize"
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D_X(_BlitTexture);
            SAMPLER(sampler_BlitTexture);

            float _SharpenRadius;
            float _SharpenStrength;

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

                float2 uv = float2((input.vertexID << 1) & 2, input.vertexID & 2);
                output.positionCS = float4(uv * 2.0 - 1.0, 0.0, 1.0);
                output.uv = uv;

                #if UNITY_UV_STARTS_AT_TOP
                output.uv.y = 1.0 - output.uv.y;
                #endif

                return output;
            }

            float3 ApplySharpen(float2 uv)
            {
                float2 texelSize =
                    (1.0 / _ScreenParams.xy) *
                    _SharpenRadius;

                float3 center = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_BlitTexture, uv).rgb;
                float3 up     = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_BlitTexture, uv + float2(0,  texelSize.y)).rgb;
                float3 down   = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_BlitTexture, uv + float2(0, -texelSize.y)).rgb;
                float3 left   = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_BlitTexture, uv + float2(-texelSize.x, 0)).rgb;
                float3 right  = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_BlitTexture, uv + float2( texelSize.x, 0)).rgb;

                float k = _SharpenStrength;
                float3 result = center * (1.0 + 4.0 * k) - (up + down + left + right) * k;
                return saturate(result);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_BlitTexture, input.uv).rgb;
                color = ApplySharpen(input.uv);

                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
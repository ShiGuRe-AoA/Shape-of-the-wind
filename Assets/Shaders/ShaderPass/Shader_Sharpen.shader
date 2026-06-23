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
            Name "Sharpen"
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "../HLSL/PostProcessCommon.hlsl"

            float _SharpenRadius;
            float _SharpenStrength;
            
            float3 ApplySharpen(float2 uv)
            {
                float2 texelSize =
                    GetSourceTexelSize() *
                    _SharpenRadius;

                float3 center = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
                float3 up     = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(0,  texelSize.y)).rgb;
                float3 down   = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(0, -texelSize.y)).rgb;
                float3 left   = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(-texelSize.x, 0)).rgb;
                float3 right  = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2( texelSize.x, 0)).rgb;

                float k = _SharpenStrength;
                float3 result = center * (1.0 + 4.0 * k) - (up + down + left + right) * k;
                return saturate(result);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord).rgb;
                color = ApplySharpen(input.texcoord);

                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
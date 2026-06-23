Shader "Custom/URP/Posterize"
{
    Properties
    {
        _PosterizeSteps("Posterize Steps", Range(8, 64)) = 32
        _LightWeight("Posterize Light Weight", Range(0, 1)) = 0.5
        _ColorWeight("Posterize Color Weight", Range(0, 1)) = 0.2
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }

        Pass
        {
            Name "Posterize"
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "../HLSL/PostProcessCommon.hlsl"
            #include "../HLSL/ColorSpace.hlsl"

            float _PosterizeSteps;
            float _LightWeight;
            float _ColorWeight;
            
            float3 ApplyPosterize(float3 color)
            {
                float steps = max(_PosterizeSteps, 2.0);

                //============ OKLAB 2 RGB
                float3 originLab = RGBToOKLab(color);
                float3 lab = originLab;
                lab.x = lerp(originLab.x, floor(lab.x * (steps - 1)) / (steps - 1), _LightWeight);
                float3 colorLAB = saturate(OKLabToRGB(lab));
                //============

                //============ Linear RGB
                float3 rgb = color;
                rgb = lerp(rgb, floor(rgb * (steps - 1)) / (steps - 1), _LightWeight);
                float3 colorRGB = saturate(rgb);
                //============

                float3 resultColor = lerp(colorLAB, colorRGB, _ColorWeight);
                return saturate(resultColor);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord).rgb;

                color = ApplyPosterize(color);

                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
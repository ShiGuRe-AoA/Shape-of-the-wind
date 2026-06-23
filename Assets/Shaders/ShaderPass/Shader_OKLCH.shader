Shader "Custom/PostProcess/OKLCH"
{
    Properties
    {

        _LightnessOffset("Lightness Offset", Range(-1, 1)) = 0
        _ChromaScale("Chroma Scale", Range(0, 3)) = 1
        _HueShift("Hue Shift", Range(-3.14159, 3.14159)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Opaque"
        }

        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "OKLCH"

            HLSLPROGRAM

            #pragma vertex Vert
            #pragma fragment Frag

            #include "../HLSL/PostProcessCommon.hlsl"
            #include "../HLSL/ColorSpace.hlsl"

            float _LightnessOffset;
            float _ChromaScale;
            float _HueShift;
            
            half4 Frag(Varyings input) : SV_Target
            {
                float3 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord).rgb;

                float3 lab = RGBToOKLab(color);
                float3 lch = OKLabToOKLCH(lab);

                lch.x += _LightnessOffset;
                lch.y *= _ChromaScale;
                lch.z += _HueShift;

                lab = OKLCHToOKLab(lch);
                color = OKLabToRGB(lab);

                return half4(saturate(color), 1.0);
            }

            ENDHLSL
        }
    }
}
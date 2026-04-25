Shader "Custom/PostProcess/Constructivism"
{
    Properties
    {
        [Header(Base Tone)]
        _EffectIntensity("Effect Intensity", Range(0,1)) = 1

        _PaperColor("Paper Color", Color) = (0.88, 0.80, 0.63, 1)
        _CreamColor("Cream Color", Color) = (0.72, 0.63, 0.45, 1)
        _WarmGrayColor("Warm Gray Color", Color) = (0.36, 0.34, 0.28, 1)
        _InkBlackColor("Ink Black Color", Color) = (0.06, 0.055, 0.045, 1)

        _BlackThreshold("Black Threshold", Range(0,1)) = 0.20
        _GrayThreshold("Gray Threshold", Range(0,1)) = 0.45
        _CreamThreshold("Cream Threshold", Range(0,1)) = 0.72

        [Header(Print Noise)]
        _Noise("Noise Texture", 2D) = "white" {}
        _NoiseScale("Noise Scale", Range(0.1,20)) = 2.5
        _NoiseThresholdJitter("Noise Threshold Jitter", Range(0,0.2)) = 0.08
        _NoiseStrength("Noise Strength", Range(0,1)) = 0.16

        [Header(Hatch)]
        _HatchDensity("Hatch Density", Range(1,1000)) = 180
        _HatchStrength("Hatch Strength", Range(0,1)) = 0.28
        _HatchWidth("Hatch Width", Range(0.01,0.49)) = 0.12
        _HatchShadowStart("Hatch Shadow Start", Range(0,1)) = 0.48
        _HatchShadowSoftness("Hatch Shadow Softness", Range(0.001,0.5)) = 0.16

        [Header(Edge Roughness)]
        _EdgeRoughness("Edge Roughness", Range(0,1)) = 0.35

        [Header(Optional Geometry)]
        _GeoStrength("Geometry Strength", Range(0,1)) = 0
        _RedColor("Red Color", Color) = (0.65, 0.06, 0.035, 1)
        _RedBlockOffset("Red Block Offset", Range(-1,1)) = -0.22
        _RedBlockSoftness("Red Block Softness", Range(0.001,0.2)) = 0.015
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Opaque"
        }

        Pass
        {
            Name "Constructivism"

            ZWrite Off
            ZTest Always
            Cull Off
            Blend One Zero

            HLSLPROGRAM

            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D_X(_Noise);
            SAMPLER(sampler_Noise);

            float _EffectIntensity;

            float4 _BlitTexture_TexelSize;

            float4 _PaperColor;
            float4 _CreamColor;
            float4 _WarmGrayColor;
            float4 _InkBlackColor;

            float _BlackThreshold;
            float _GrayThreshold;
            float _CreamThreshold;

            float _NoiseScale;
            float _NoiseThresholdJitter;
            float _NoiseStrength;

            float _HatchDensity;
            float _HatchStrength;
            float _HatchWidth;
            float _HatchShadowStart;
            float _HatchShadowSoftness;

            float _EdgeRoughness;

            float _GeoStrength;
            float4 _RedColor;
            float _RedBlockOffset;
            float _RedBlockSoftness;

            float cbrt_safe(float x)
            {
                return sign(x) * pow(abs(x), 1.0 / 3.0);
            }

            float3 RGB2OKLAB(float3 c)
            {
                float l = 0.41222147*c.r + 0.53633254*c.g + 0.05144599*c.b;
                float m = 0.21190350*c.r + 0.68069955*c.g + 0.10739696*c.b;
                float s = 0.08830246*c.r + 0.28171884*c.g + 0.62997870*c.b;

                float3 lms = float3(
                    cbrt_safe(l),
                    cbrt_safe(m),
                    cbrt_safe(s)
                );

                return float3(
                    0.21045426*lms.x + 0.79361778*lms.y - 0.00407205*lms.z,
                    1.97799850*lms.x - 2.42859221*lms.y + 0.45059371*lms.z,
                    0.02590404*lms.x + 0.78277177*lms.y - 0.80867577*lms.z
                );
            }

            float3 OKLAB2RGB(float3 lab)
            {
                float L = lab.x;
                float a = lab.y;
                float b = lab.z;

                float l_ = L + 0.3963377774*a + 0.2158037573*b;
                float m_ = L - 0.1055613458*a - 0.0638541728*b;
                float s_ = L - 0.0894841775*a - 1.2914855480*b;

                float l = l_*l_*l_;
                float m = m_*m_*m_;
                float s = s_*s_*s_;

                float3 rgb;
                rgb.r =  4.0767416621*l - 3.3077115913*m + 0.2309699292*s;
                rgb.g = -1.2684380046*l + 2.6097574011*m - 0.3413193965*s;
                rgb.b = -0.0041960863*l - 0.7034186147*m + 1.7076147010*s;
                return rgb;
            }


            float CalcLuminance(float3 color)
            {
                // Rec.601 老电视
                //return dot(color, float3(0.299, 0.587, 0.114));
                // Rec.709 HDTV
                //return dot(color, float3(0.2126, 0.7152, 0.0722));
                // Rec.2020 HDR广色域
                return dot(color, float3(0.2627, 0.6780, 0.0593));
                // OKLAB
                //return RGB2OKLAB(color).x;
            }

            float3 ConstructivismPosterize(float luminance, float noiseRate)
            {
                float jitter = (noiseRate - 0.5) * _NoiseThresholdJitter;
                float l = saturate(luminance + jitter);

                if (l < _BlackThreshold)
                    return _InkBlackColor.rgb;

                if (l < _GrayThreshold)
                    return _WarmGrayColor.rgb;

                if (l < _CreamThreshold)
                    return _CreamColor.rgb;

                return _PaperColor.rgb;
            }

            float DiagonalMask(float2 uv, float2 dir, float offset, float softness)
            {
                float d = dot(uv - 0.5, normalize(dir)) + offset;
                return smoothstep(-softness, softness, d);
            }

            float HatchLinesAngle(float2 uv, float angle)
            {
                float s = sin(angle);
                float c = cos(angle);

                float2 p;
                p.x = uv.x * c - uv.y * s;
                p.y = uv.x * s + uv.y * c;

                float v = frac(p.y);
                return 1.0 - smoothstep(_HatchWidth, _HatchWidth + 0.03, v);
            }

            float HatchPattern(float2 uv, float luminance)
            {
                float2 hatchUV = uv * _ScreenParams.xy;

                float hatch1 = HatchLinesAngle(hatchUV / _HatchDensity, 0.52);
                float hatch2 = HatchLinesAngle(hatchUV / (_HatchDensity / 1.25), -0.5);

                float shadowMask = 1.0 - smoothstep(
                    _HatchShadowStart,
                    _HatchShadowStart + _HatchShadowSoftness,
                    luminance
                );

                float darkMask = 1.0 - smoothstep(
                    _HatchShadowStart - _HatchShadowSoftness,
                    _HatchShadowStart + 0.5 * _HatchShadowSoftness,
                    luminance
                );

                return saturate(hatch1 * shadowMask * 0.35 + hatch2 * darkMask * 0.65);
            }

            float EdgeMask(float2 uv)
            {
                float2 texel = _BlitTexture_TexelSize.xy;

                float3 c  = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
                float3 cx = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(texel.x, 0)).rgb;
                float3 cy = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(0, texel.y)).rgb;

                float l  = CalcLuminance(c);
                float lx = CalcLuminance(cx);
                float ly = CalcLuminance(cy);

                float edge = abs(l - lx) + abs(l - ly);
                return saturate(edge * 8.0);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float2 uv = i.texcoord;

                float3 sceneColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
                float luminance = CalcLuminance(sceneColor);

                float noiseRate = SAMPLE_TEXTURE2D_X(_Noise, sampler_Noise, uv * _NoiseScale).r;

                // 1. 基础旧纸色调
                float3 finalColor = ConstructivismPosterize(luminance, noiseRate);

                // 2. 暗部排线
                float hatch = HatchPattern(uv, luminance);
                finalColor = lerp(finalColor, finalColor * 0.45, hatch * _HatchStrength);

                // 3. 边缘粗糙化：让硬切边更像纸片/油墨
                float edge = EdgeMask(uv);
                float rough = saturate(edge * (1.0 - noiseRate) * _EdgeRoughness);
                finalColor = lerp(finalColor, _InkBlackColor.rgb, rough * 0.45);

                // 4. 纸张颗粒
                finalColor *= lerp(1.0, noiseRate, _NoiseStrength);

                // 5. 可选弱红色斜切，默认关闭
                float redBlock = DiagonalMask(
                    uv,
                    float2(1.0, -0.55),
                    _RedBlockOffset,
                    _RedBlockSoftness
                );

                finalColor = lerp(finalColor, _RedColor.rgb, redBlock * _GeoStrength);

                // 6. 和原画混合
                finalColor = lerp(sceneColor, finalColor, _EffectIntensity);

                return half4(saturate(finalColor), 1.0);
            }

            ENDHLSL
        }
    }
}
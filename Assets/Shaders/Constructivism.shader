Shader "Custom/PostProcess/Constructivism"
{
    Properties
    {
        [Header(Base Tone)]
        _EffectIntensity("Effect Intensity", Range(0,1)) = 1

        _PaperColor("Paper Color / Highlight", Color) = (0.92, 0.90, 0.84, 1)
        _CreamColor("Cream Color / Mid", Color) = (0.85, 0.02, 0.02, 1)
        _WarmGrayColor("Warm Gray Color / Dark Mid", Color) = (0.28, 0.00, 0.00, 1)
        _InkBlackColor("Ink Black Color / Shadow", Color) = (0.02, 0.015, 0.01, 1)

        _BlackThreshold("Black Threshold", Range(0,1)) = 0.22
        _GrayThreshold("Gray Threshold", Range(0,1)) = 0.48
        _CreamThreshold("Cream Threshold", Range(0,1)) = 0.72

        [Header(Print Noise)]
        _Noise("Noise Texture", 2D) = "white" {}
        _NoiseScale("Noise Scale", Range(0.1,20)) = 2.5
        _NoiseThresholdJitter("Noise Threshold Jitter", Range(0,0.2)) = 0.08
        _NoiseStrength("Noise Strength", Range(0,1)) = 0.16

        [Header(Edge Lines)]
        _LineColor("Line Color", Color) = (0.02, 0.015, 0.01, 1)
        _LineIntensity("Line Intensity", Range(0,1)) = 1
        _LineThickness("Line Thickness", Range(0.5,5)) = 1.5

        _DepthThreshold("Depth Threshold", Range(0.0001,0.1)) = 0.015
        _NormalThreshold("Normal Threshold", Range(0.01,1)) = 0.25
        _ColorThreshold("Color Threshold", Range(0.001,0.5)) = 0.08

        _LineFadeStart("Line Fade Start", Float) = 5
        _LineFadeEnd("Line Fade End", Float) = 100

        [Header(Edge Debug)]
        [Toggle(_DEBUG_EDGE)] _DebugEdge("Debug Edge", Float) = 0
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

            #pragma multi_compile_local _ _DEBUG_EDGE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

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

            float4 _LineColor;
            float _LineIntensity;
            float _LineThickness;

            float _DepthThreshold;
            float _NormalThreshold;
            float _ColorThreshold;

            float _LineFadeStart;
            float _LineFadeEnd;

            float cbrt_safe(float x)
            {
                return sign(x) * pow(abs(x), 1.0 / 3.0);
            }

            float3 RGB2OKLAB(float3 c)
            {
                float l = 0.41222147 * c.r + 0.53633254 * c.g + 0.05144599 * c.b;
                float m = 0.21190350 * c.r + 0.68069955 * c.g + 0.10739696 * c.b;
                float s = 0.08830246 * c.r + 0.28171884 * c.g + 0.62997870 * c.b;

                float3 lms = float3(
                    cbrt_safe(l),
                    cbrt_safe(m),
                    cbrt_safe(s)
                );

                return float3(
                    0.21045426 * lms.x + 0.79361778 * lms.y - 0.00407205 * lms.z,
                    1.97799850 * lms.x - 2.42859221 * lms.y + 0.45059371 * lms.z,
                    0.02590404 * lms.x + 0.78277177 * lms.y - 0.80867577 * lms.z
                );
            }

            float3 OKLAB2RGB(float3 lab)
            {
                float L = lab.x;
                float a = lab.y;
                float b = lab.z;

                float l_ = L + 0.3963377774 * a + 0.2158037573 * b;
                float m_ = L - 0.1055613458 * a - 0.0638541728 * b;
                float s_ = L - 0.0894841775 * a - 1.2914855480 * b;

                float l = l_ * l_ * l_;
                float m = m_ * m_ * m_;
                float s = s_ * s_ * s_;

                float3 rgb;
                rgb.r =  4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s;
                rgb.g = -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s;
                rgb.b = -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s;

                return rgb;
            }

            float CalcLuminance(float3 color)
            {
                // Rec.2020 HDR 广色域
                return dot(color, float3(0.2627, 0.6780, 0.0593));

                // 也可以换成 OKLAB：
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

            float RobertsCrossDepth(float2 uv, float2 ts)
            {
                float raw00 = SampleSceneDepth(uv);
                float raw11 = SampleSceneDepth(uv + ts);
                float raw10 = SampleSceneDepth(uv + float2(ts.x, 0));
                float raw01 = SampleSceneDepth(uv + float2(0, ts.y));

                float d00 = LinearEyeDepth(raw00, _ZBufferParams);
                float d11 = LinearEyeDepth(raw11, _ZBufferParams);
                float d10 = LinearEyeDepth(raw10, _ZBufferParams);
                float d01 = LinearEyeDepth(raw01, _ZBufferParams);

                float baseDepth = max(d00, 0.001);

                float diff1 = abs(d11 - d00) / baseDepth;
                float diff2 = abs(d10 - d01) / baseDepth;

                return sqrt(diff1 * diff1 + diff2 * diff2);
            }

            float RobertsCrossNormal(float2 uv, float2 ts)
            {
                float3 n00 = SampleSceneNormals(uv);
                float3 n11 = SampleSceneNormals(uv + ts);
                float3 n10 = SampleSceneNormals(uv + float2(ts.x, 0));
                float3 n01 = SampleSceneNormals(uv + float2(0, ts.y));

                float3 g0 = n11 - n00;
                float3 g1 = n10 - n01;

                return sqrt(dot(g0, g0) + dot(g1, g1));
            }

            float RobertsCrossLuma(float2 uv, float2 ts)
            {
                float3 c00 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
                float3 c11 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + ts).rgb;
                float3 c10 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(ts.x, 0)).rgb;
                float3 c01 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(0, ts.y)).rgb;

                float l00 = CalcLuminance(c00);
                float l11 = CalcLuminance(c11);
                float l10 = CalcLuminance(c10);
                float l01 = CalcLuminance(c01);

                float d1 = l11 - l00;
                float d2 = l10 - l01;

                return sqrt(d1 * d1 + d2 * d2);
            }

            float GetLineFade(float sceneDepth)
            {
                return saturate((_LineFadeEnd - sceneDepth) / max(_LineFadeEnd - _LineFadeStart, 0.001));
            }

            float EdgeMask(float2 uv, out float sceneDepth)
            {
                float rawDepth = SampleSceneDepth(uv);
                sceneDepth = LinearEyeDepth(rawDepth, _ZBufferParams);

                #if defined(UNITY_REVERSED_Z)
                    bool isSky = rawDepth < 0.000001;
                #else
                    bool isSky = rawDepth > 0.999999;
                #endif

                if (isSky)
                    return 0.0;

                float lineFade = GetLineFade(sceneDepth);

                // 远处线条略细，防止城市楼群糊成大块黑色
                float thicknessScale = lerp(0.5, 1.0, lineFade);
                float2 ts = (_LineThickness * thicknessScale) / _ScreenParams.xy;

                float depthEdge = RobertsCrossDepth(uv, ts);
                float normalEdge = RobertsCrossNormal(uv, ts);
                float colorEdge = RobertsCrossLuma(uv, ts);

                float d = step(max(_DepthThreshold, 0.000001), depthEdge);
                float n = step(max(_NormalThreshold, 0.000001), normalEdge);
                float c = step(max(_ColorThreshold, 0.000001), colorEdge);

                return saturate(d + n + c);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float2 uv = i.texcoord;

                float3 sceneColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
                float luminance = CalcLuminance(sceneColor);

                float noiseRate = SAMPLE_TEXTURE2D_X(_Noise, sampler_Noise, uv * _NoiseScale).r;

                // 1. 基础构成主义 / 海报化色调
                float3 finalColor = ConstructivismPosterize(luminance, noiseRate);

                // 2. 纸张颗粒
                finalColor *= lerp(1.0, noiseRate, _NoiseStrength);

                // 3. 三路边缘检测：Depth + Normal + Luma
                float sceneDepth;
                float edge = EdgeMask(uv, sceneDepth);

                float lineFade = GetLineFade(sceneDepth);

                #if defined(_DEBUG_EDGE)
                    return half4(edge.xxx, 1.0);
                #endif

                // 4. 墨线覆盖
                finalColor = lerp(finalColor, _LineColor.rgb, edge * _LineIntensity * lineFade);

                // 5. 和原画混合
                finalColor = lerp(sceneColor, finalColor, _EffectIntensity);

                return half4(saturate(finalColor), 1.0);
            }

            ENDHLSL
        }
    }
}
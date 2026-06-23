Shader "Custom/URP/ColorAdjustment"
{
    Properties
    {
        [Header(Global)]
        _GlobalLift("Global Lift", Range(-0.15, 0.15)) = 0
        _GlobalTint("Global Tint", Color) = (0.5, 0.5, 0.5, 1)
        _GlobalTintStrength("Global Tint Strength", Range(0, 1)) = 0
        _GlobalSaturation("Global Saturation", Range(0, 2)) = 1

        [Header(Shadows)]
        _ShadowLift("Shadow Lift", Range(-0.3, 0.3)) = 0
        _ShadowsTint("Shadows Tint", Color) = (0.5, 0.5, 0.5, 1)
        _ShadowTintStrength("Shadow Tint Strength", Range(0, 1)) = 0
        _ShadowSaturation("Shadow Saturation", Range(0, 2)) = 1

        [Header(Midtones)]
        _MidtoneLift("Midtone Lift", Range(-0.3, 0.3)) = 0
        _MidtonesTint("Midtones Tint", Color) = (0.5, 0.5, 0.5, 1)
        _MidtoneTintStrength("Midtone Tint Strength", Range(0, 1)) = 0
        _MidtoneSaturation("Midtone Saturation", Range(0, 2)) = 1

        [Header(Highlights)]
        _HighlightLift("Highlight Lift", Range(-0.3, 0.3)) = 0
        _HighlightsTint("Highlights Tint", Color) = (0.5, 0.5, 0.5, 1)
        _HighlightTintStrength("Highlight Tint Strength", Range(0, 1)) = 0
        _HighlightSaturation("Highlight Saturation", Range(0, 2)) = 1

        [Header(Range)]
        _ShadowEnd("Shadow End", Range(0, 1)) = 0.33
        _HighlightStart("Highlight Start", Range(0, 1)) = 0.66
        _RangeSoftness("Range Softness", Range(0.001, 0.5)) = 0.15
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "ColorAdjustment"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "../HLSL/PostProcessCommon.hlsl"
            #include "../HLSL/ColorSpace.hlsl"

            float _GlobalLift;
            float4 _GlobalTint;
            float _GlobalTintStrength;
            float _GlobalSaturation;

            float _ShadowLift;
            float4 _ShadowsTint;
            float _ShadowTintStrength;
            float _ShadowSaturation;

            float _MidtoneLift;
            float4 _MidtonesTint;
            float _MidtoneTintStrength;
            float _MidtoneSaturation;

            float _HighlightLift;
            float4 _HighlightsTint;
            float _HighlightTintStrength;
            float _HighlightSaturation;

            float _ShadowEnd;
            float _HighlightStart;
            float _RangeSoftness;
            
            half4 Frag(Varyings input) : SV_Target
            {
                float3 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord).rgb;

                float3 lab = RGBToOKLab(color);

                float L0 = saturate(lab.x);

                // ===== 区域划分 =====
                float shadowMask =
                    1.0 - smoothstep(
                        _ShadowEnd - _RangeSoftness,
                        _ShadowEnd + _RangeSoftness,
                        L0);

                float highlightMask =
                    smoothstep(
                        _HighlightStart - _RangeSoftness,
                        _HighlightStart + _RangeSoftness,
                        L0);

                float midtoneMask =
                    saturate(1.0 - shadowMask - highlightMask);

                // ===== Lift =====
                float lift =
                    _GlobalLift +
                    shadowMask * _ShadowLift +
                    midtoneMask * _MidtoneLift +
                    highlightMask * _HighlightLift;

                lab.x = saturate(lab.x + lift);

                // ===== 染色 =====
                float2 neutralAB =
                    RGBToOKLab(float3(0.5, 0.5, 0.5)).yz;

                float2 globalAB =
                    RGBToOKLab(_GlobalTint.rgb).yz - neutralAB;

                float2 shadowAB =
                    RGBToOKLab(_ShadowsTint.rgb).yz - neutralAB;

                float2 midAB =
                    RGBToOKLab(_MidtonesTint.rgb).yz - neutralAB;

                float2 highAB =
                    RGBToOKLab(_HighlightsTint.rgb).yz - neutralAB;

                // ===============================
                // 分区饱和度（先作用在原始 ab 上）
                // 1 = 不变，<1 去饱和，>1 增饱和
                // ===============================
                float sat =
                    _GlobalSaturation +
                    shadowMask * (_ShadowSaturation - 1.0) +
                    midtoneMask * (_MidtoneSaturation - 1.0) +
                    highlightMask * (_HighlightSaturation - 1.0);

                lab.yz *= max(sat, 0.0);

                // ===============================
                // 分区染色（再叠加偏色）
                // ===============================
                lab.yz += globalAB * _GlobalTintStrength;
                lab.yz += shadowAB * shadowMask * _ShadowTintStrength;
                lab.yz += midAB * midtoneMask * _MidtoneTintStrength;
                lab.yz += highAB * highlightMask * _HighlightTintStrength;

                float3 finalColor = saturate(OKLabToRGB(lab));

                return half4(finalColor, 1);
            }

            ENDHLSL
        }
    }
}
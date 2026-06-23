Shader "Custom/URP/Halftone"
{
    Properties
    {
        [Header(Halftone)]
        _HalftoneScale("Halftone Scale", Range(3, 20)) = 5
        _HalftoneSoftness("Halftone Softness", Range(0, 0.5)) = 0.03

        [Header(Tone)]
        _DotSizeMin("Dot Size Min", Range(0, 0.5)) = 0.05
        _DotSizeMax("Dot Size Max", Range(0.3, 1)) = 0.45

        [Header(Color)]
        _DarkColor("Dark Color", Color) = (0.10, 0.22, 0.38, 1)
        _LightColor("Light Color", Color) = (0.93, 0.88, 0.82, 1)

        // 旧版局部过渡参数，当前由 StyleTransitionComposite 统一处理。
        // 需要单独测试以下可选方案时，可以重新取消注释。
        // [Header(Gradient)]
        // _Noise("Noise Texture", 2D) = "white" {}
        // _NoiseScale("Noise Scale", Range(1, 100)) = 50
        // _StepLim("Step Limit", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }

        Pass
        {
            Name "HalftoneDot"
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragHalftone

            #include "../HLSL/PostProcessCommon.hlsl"
            #include "../HLSL/ColorSpace.hlsl"

            // 仅在启用世界坐标 Noise 过渡方案时需要。
            // #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            // 旧版局部过渡纹理声明，当前由 Composite Pass 统一处理。
            // TEXTURE2D(_Noise);
            // SAMPLER(sampler_Noise);

            float _HalftoneScale;
            float _HalftoneSoftness;

            float _DotSizeMin;
            float _DotSizeMax;

            float4 _DarkColor;
            float4 _LightColor;

            // 旧版局部过渡参数声明。
            // float _StepLim;
            // float _NoiseScale;

            // 屏幕空间重建世界方向 - 当前像素在世界空间朝向哪
            float3 GetViewDirWS(float2 uv)
            {
                float2 ndc = uv * 2.0 - 1.0;
                float4 clipPos = float4(ndc, 1.0, 1.0);

                float4 viewPos = mul(unity_CameraInvProjection, clipPos);
                viewPos /= viewPos.w;

                float3 viewDirVS = normalize(viewPos.xyz);
                float3 viewDirWS = normalize(mul((float3x3)unity_CameraToWorld, viewDirVS));
                return viewDirWS;
            }

            // 方向转到球面uv
            float2 DirToLatLongUV(float3 dir)
            {
                dir = normalize(dir);

                float u = atan2(dir.x, dir.z) / (2.0 * PI) + 0.5;
                float v = asin(dir.y) / PI + 0.5;

                return float2(u, v);
            }

            float3 GetWorldPos(float2 uv, float rawDepth)
            {
                float2 ndcXY = uv * 2.0 - 1.0;

                #if UNITY_UV_STARTS_AT_TOP
                    ndcXY.y = -ndcXY.y;
                #endif

                #if UNITY_REVERSED_Z
                    float ndcZ = rawDepth;
                #else
                    float ndcZ = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, rawDepth);
                #endif

                float4 clipPos = float4(ndcXY, ndcZ, 1.0);
                float4 worldPos = mul(UNITY_MATRIX_I_VP, clipPos);
                return worldPos.xyz / worldPos.w;
            }

            half4 FragHalftone(Varyings input) : SV_Target
            {
                //============================================ 直接半色调点
                float3 color = SampleSource(input.texcoord).rgb;

                // Luma 的 L 作为明度
                float L = dot(color, float3(0.2627, 0.6780, 0.0593));

                // 用 OKLAB 的 L 作为明度
                //float L = saturate(RGBToOKLab(color).x);

                // 屏幕空间 halftone cell
                float2 screenUV = input.texcoord * _ScreenParams.xy / max(_HalftoneScale, 1.0);
                float2 cellUV = frac(screenUV) - 0.5;

                // 当前像素到 cell 中心的距离
                float dist = length(cellUV);

                // 暗部点大，亮部点小
                float radius = lerp(_DotSizeMax, _DotSizeMin, L);


                // 软边抗锯齿
                float aa = fwidth(dist);
                float dot = 1.0 - smoothstep(
                    radius - aa - _HalftoneSoftness,
                    radius + aa + _HalftoneSoftness,
                    dist
                );

                // 直接用点重建图像，而不是把点盖在原图上
                float3 finalColor = lerp(_LightColor.rgb, _DarkColor.rgb, dot);

                //return half4(finalColor, 1.0);

                //===========
                // 屏幕 UV Noise 方案
                //float3 noiseCol = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, input.texcoord).rgb;
                //float noiseRate = noiseCol.r;
                //===========
                //===========
                // 球面 UV Noise 方案
                // float3 dirWS = GetViewDirWS(input.texcoord);
                // float2 noiseUV = DirToLatLongUV(dirWS);

                // // todo: Transform / Scale
                // //noiseUV.x = frac(noiseUV.x * _NoiseTilingX + _NoiseOffsetX);
                // //noiseUV.y = saturate(noiseUV.y * _NoiseTilingY + _NoiseOffsetY);

                // float noiseRate = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, noiseUV).r;
                //===========
                //===========
                // 世界坐标 Noise 方案
                // float depth = SampleSceneDepth(input.texcoord);
                // float3 worldPos = GetWorldPos(input.texcoord, depth);
                // float2 noiseUV = worldPos.xz / _NoiseScale;

                // float noiseRate = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, noiseUV).r;
                //===========

                // half edge = 1.0 - _StepLim;

                // 过渡宽度（你可以调这个，0.01~0.1 之间比较常用）
                // half smoothWidth = 0.05;

                // smoothstep 做平滑
                // half t = smoothstep(edge - smoothWidth, edge + smoothWidth, noiseRate);

                // lerp 混合两个结果
                // half3 col = lerp(color, finalColor, t);

                // 旧版局部过渡输出：启用任意一种 noiseRate 后可恢复此行。
                // return half4(col, 1.0);

                // 当前风格 Pass 只输出完整 Halftone；风格间过渡由 Composite Pass 负责。
                return half4(finalColor, 1.0);


                //============================================ 先像素化再半色调点
                // float scale = max(_HalftoneScale, 1.0);

                // // 屏幕空间 halftone cell 坐标
                // float2 screenUV = input.texcoord * _ScreenParams.xy / scale;
                // float2 cell = floor(screenUV);
                // float2 cellUV = frac(screenUV) - 0.5;

                // // 用 cell 中心采样，而不是逐像素采样
                // float2 cellCenter = (cell + 0.5) * scale / _ScreenParams.xy;
                // float3 color = SampleSource(cellCenter).rgb;


                // // 用 OKLAB 的 L 作为明度
                // float L = saturate(RGBToOKLab(color).x);
                // //float L = dot(color, float3(0.2126, 0.7152, 0.0722));

                // // 如果你有 _Steps，建议打开
                // // float steps = max(_Steps, 2.0);
                // // L = floor(L * steps) / (steps - 1.0);
                // // L = saturate(L);

                // // 当前像素到 cell 中心的距离
                // float dist = length(cellUV);

                // // 暗部点大，亮部点小
                // float radius = lerp(_DotSizeMax, _DotSizeMin, L);

                // // 用 cell 级噪声，而不是像素级噪声
                // float noise = frac(sin(dot(cell, float2(12.9898, 78.233))) * 43758.5453);
                // radius += (noise - 0.5) * 0.02;
                // radius = clamp(radius, 0.001, 0.5);

                // // 软边抗锯齿
                // float aa = fwidth(dist);
                // float dot = 1.0 - smoothstep(
                //     radius - aa - _HalftoneSoftness,
                //     radius + aa + _HalftoneSoftness,
                //     dist
                // );

                // // 直接用点重建图像
                // float3 finalColor = lerp(_LightColor.rgb, _DarkColor.rgb, dot);


                // float3 noiseCol = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, input.texcoord).rgb;
                // float noiseRate = noiseCol.r;
                // if(noiseRate + _StepLim > 1)
                // {
                //     return half4(finalColor, 1.0);
                // }
                // return half4(color, 1.0);
            }

            ENDHLSL
        }
    }

    Fallback Off
}

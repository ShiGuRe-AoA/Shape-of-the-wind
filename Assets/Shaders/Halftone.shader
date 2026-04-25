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

        [Header(Gradient)]
        _Noise("Noise Texture", 2D) = "white" {}
        _NoiseScale("Noise Scale", Range(1, 100)) = 50
        _StepLim("Step Limit",Range(0,1)) = 0.5
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D_X(_BlitTexture);
            SAMPLER(sampler_BlitTexture);
            TEXTURE2D_X(_Noise);
            SAMPLER(sampler_Noise);

            float _HalftoneScale;
            float _HalftoneSoftness;

            float _StepLim;

            float _DotSizeMin;
            float _DotSizeMax;

            float4 _DarkColor;
            float4 _LightColor;

            float _NoiseScale;

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
                float3 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_BlitTexture, input.uv).rgb;

                // Luma 的 L 作为明度
                float L = dot(color, float3(0.2627, 0.6780, 0.0593));

                // 用 OKLAB 的 L 作为明度
                //float L = saturate(RGB2OKLAB(color).x);

                // 屏幕空间 halftone cell
                float2 screenUV = input.uv * _ScreenParams.xy / max(_HalftoneScale, 1.0);
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
                //float3 noiseCol = SAMPLE_TEXTURE2D_X(_Noise, sampler_Noise, input.uv).rgb;
                //float noiseRate = noiseCol.r;
                //===========
                //===========
                // float3 dirWS = GetViewDirWS(input.uv);
                // float2 noiseUV = DirToLatLongUV(dirWS);

                // // todo: Transform / Scale
                // //noiseUV.x = frac(noiseUV.x * _NoiseTilingX + _NoiseOffsetX);
                // //noiseUV.y = saturate(noiseUV.y * _NoiseTilingY + _NoiseOffsetY);
                
                // float noiseRate = SAMPLE_TEXTURE2D_X(_Noise,sampler_Noise,noiseUV).r;
                //===========
                //===========
                float depth = SampleSceneDepth(input.uv);
                float3 worldPos = GetWorldPos(input.uv, depth);
                float2 noiseUV = worldPos.xz / _NoiseScale;

                float noiseRate = SAMPLE_TEXTURE2D_X(_Noise, sampler_Noise, noiseUV).r;
                //===========

                half edge = 1.0 - _StepLim;

                // 过渡宽度（你可以调这个，0.01~0.1 之间比较常用）
                half smoothWidth = 0.05;

                // smoothstep 做平滑
                half t = smoothstep(edge - smoothWidth, edge + smoothWidth, noiseRate);

                // lerp 混合两个结果
                half3 col = lerp(color, finalColor, t);

                return half4(col, 1.0);


                //============================================ 先像素化再半色调点
                // float scale = max(_HalftoneScale, 1.0);

                // // 屏幕空间 halftone cell 坐标
                // float2 screenUV = input.uv * _ScreenParams.xy / scale;
                // float2 cell = floor(screenUV);
                // float2 cellUV = frac(screenUV) - 0.5;

                // // 用 cell 中心采样，而不是逐像素采样
                // float2 cellCenter = (cell + 0.5) * scale / _ScreenParams.xy;
                // float3 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_BlitTexture, cellCenter).rgb;


                // // 用 OKLAB 的 L 作为明度
                // float L = saturate(RGB2OKLAB(color).x);
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


                // float3 noiseCol = SAMPLE_TEXTURE2D_X(_Noise, sampler_Noise, input.uv).rgb;
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
}


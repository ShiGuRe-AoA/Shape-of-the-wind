Shader "Custom/PostProcess/Blueprint"
{
    Properties
    {
        _BlueprintBaseColor("Blueprint Base Color", Color) = (0.10, 0.25, 0.85, 1)
        _BlueprintIntensity("Blueprint Intensity", Range(0,1)) = 1

        _EdgeScale("Edge Scale", Range(0,5)) = 1.5

        _DepthEdgeWeight("Depth Edge Weight", Range(0, 1)) = 0.5
        _DepthEdgeThreshold("Depth Edge Threshold", Range(0.0001,1)) = 0.002
        _NormalEdgeWeight("Normal Edge Weight",Range(0, 1)) = 0.5
        _NormalEdgeThreshold("Normal Edge Threshold", Range(0.001,1)) = 0.15
        _LightEdgeWeight("Light Edge Weight", Range(0, 1)) = 0.5

        _HatchDensity("Hatch Density", Range(1,1000)) = 180
        _HatchStrength("Hatch Strength", Range(0,1)) = 0.15
        _HatchShadowStart("Hatch Shadow Start", Range(0,1)) = 0.35

        _JitterStrength("Jitter Strength", Range(0,0.02)) = 0.0015
        _JitterSpeed("Jitter Speed", Range(0,20)) = 1.5

        _HighlightThreshold("Highlight Threshold", Range(0,1)) = 0.75
        _HighlightSoftness("Highlight Softness", Range(0.001,0.5)) = 0.08
        _HighlightLineDensity("Highlight Line Density", Range(5,1000)) = 260
        _HighlightLineWidth("Highlight Line Width", Range(0.01,0.49)) = 0.12
        _HighlightStrength("Highlight Strength", Range(0,2)) = 1.0
        _HighlightAngle("Highlight Angle", Range(-3.14159,3.14159)) = 0.9
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
            Name "Blueprint"

            ZWrite Off
            ZTest Always
            Cull Off
            Blend One Zero

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN

            TEXTURE2D_X(_SSAO_OcclusionTexture);
            //TEXTURE2D_X(_ScreenSpaceShadowmapTexture);
            //SAMPLER(sampler_LinearClamp);

            float4 _BlueprintBaseColor;
            float _BlueprintIntensity;

            float _EdgeScale;

            float _DepthEdgeWeight;
            float _DepthEdgeThreshold;
            float _NormalEdgeWeight;
            float _NormalEdgeThreshold;
            float _LightEdgeWeight;

            float _HatchDensity;
            float _HatchStrength;
            float _HatchShadowStart;

            float _JitterStrength;
            float _JitterSpeed;

            float _HighlightThreshold;
            float _HighlightSoftness;
            float _HighlightLineDensity;
            float _HighlightLineWidth;
            float _HighlightStrength;
            float _HighlightAngle;

            // ===== 伪随机数 =====
            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            // ===== 随机扰动 =====
            float2 JitterUV(float2 uv)
            {
                float t = _Time.y * _JitterSpeed;
                float2 cell = floor(uv * 200.0 + t);
                float n1 = Hash21(cell);
                float n2 = Hash21(cell + 17.13);
                float2 offset = (float2(n1, n2) - 0.5) * _JitterStrength;
                return uv + offset;
            }
            // ===== OKLAB 空间 =====
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

            // ===== 深度采样 =====
            float SampleLinearDepthSafe(float2 uv)
            {
                float rawDepth = SampleSceneDepth(uv);
            #if UNITY_REVERSED_Z
                return LinearEyeDepth(rawDepth, _ZBufferParams);
            #else
                return LinearEyeDepth(lerp(UNITY_NEAR_CLIP_VALUE, 1.0, rawDepth), _ZBufferParams);
            #endif
            }

            // ===== 法线采样 =====
            float3 SampleSceneNormalSafe(float2 uv)
            {
                return SampleSceneNormals(uv);
            }

            // ===== 距离采样半径 =====
            float GetEdgeDistanceFade(float2 uv)
            {
                float linearDepth = SampleLinearDepthSafe(uv);

                // 这个系数后面你要按场景调
                // 值越大，fade增长越快
                return saturate(linearDepth * 0.06);
            }

            // ===== 深度边缘检测 =====
            
            // float CalcDepthEdge(float2 uv, float2 texelSize)
            // {
            //     float d0 = SampleLinearDepthSafe(uv);
            //     float d1 = SampleLinearDepthSafe(uv + float2(texelSize.x, 0));
            //     float d2 = SampleLinearDepthSafe(uv - float2(texelSize.x, 0));
            //     float d3 = SampleLinearDepthSafe(uv + float2(0, texelSize.y));
            //     float d4 = SampleLinearDepthSafe(uv - float2(0, texelSize.y));

            //     float diff = abs(d1 - d0) + abs(d2 - d0) + abs(d3 - d0) + abs(d4 - d0);
            //     return smoothstep(_DepthEdgeThreshold, _DepthEdgeThreshold * 2.0, diff);
            // }

            float CalcDepthEdge(float2 uv, float2 texelSize, float radiusScale, float thresholdScale)
            {
                float2 offset = texelSize * radiusScale;

                float d0 = SampleLinearDepthSafe(uv);
                float d1 = SampleLinearDepthSafe(uv + float2(offset.x, 0));
                float d2 = SampleLinearDepthSafe(uv - float2(offset.x, 0));
                float d3 = SampleLinearDepthSafe(uv + float2(0, offset.y));
                float d4 = SampleLinearDepthSafe(uv - float2(0, offset.y));

                float diff = abs(d1 - d0) + abs(d2 - d0) + abs(d3 - d0) + abs(d4 - d0);

                float threshold = _DepthEdgeThreshold * thresholdScale;
                
                return smoothstep(threshold, threshold * 2.0, diff);
                //return diff;
                
            }

            // ===== 法线边缘检测 ======
            
            // float CalcNormalEdge(float2 uv, float2 texelSize)
            // {
            //     float3 n0 = SampleSceneNormalSafe(uv);
            //     float3 n1 = SampleSceneNormalSafe(uv + float2(texelSize.x, 0));
            //     float3 n2 = SampleSceneNormalSafe(uv - float2(texelSize.x, 0));
            //     float3 n3 = SampleSceneNormalSafe(uv + float2(0, texelSize.y));
            //     float3 n4 = SampleSceneNormalSafe(uv - float2(0, texelSize.y));

            //     float diff =
            //         distance(n0, n1) +
            //         distance(n0, n2) +
            //         distance(n0, n3) +
            //         distance(n0, n4);

            //     return smoothstep(_NormalEdgeThreshold, _NormalEdgeThreshold * 2.0, diff);
            // }

            float CalcNormalEdge(float2 uv, float2 texelSize, float radiusScale, float thresholdScale)
            {
                float2 offset = texelSize * radiusScale;

                float3 n0 = SampleSceneNormalSafe(uv);
                float3 n1 = SampleSceneNormalSafe(uv + float2(offset.x, 0));
                float3 n2 = SampleSceneNormalSafe(uv - float2(offset.x, 0));
                float3 n3 = SampleSceneNormalSafe(uv + float2(0, offset.y));
                float3 n4 = SampleSceneNormalSafe(uv - float2(0, offset.y));

                float diff =
                    distance(n0, n1) +
                    distance(n0, n2) +
                    distance(n0, n3) +
                    distance(n0, n4);

                float threshold = _NormalEdgeThreshold * thresholdScale;
                
                return smoothstep(threshold, threshold * 2.0, diff);
                //return diff;
            }

            // ===== 主光受光边缘 =====
            float CalcLightEdgeNdotL(float3 normalWS, float3 lightDirWS)
            {
                float L = saturate(dot(normalWS, lightDirWS));
                float diff = abs(ddx(L)) + abs(ddy(L));
                return diff;
            }

            // ===== 阴影边缘 =====
            float CalcShadowEdge(float2 uv)
            {
                float rawShadow = SAMPLE_TEXTURE2D_X(_ScreenSpaceShadowmapTexture, sampler_LinearClamp, uv).r;

                // 0 = 亮，1 = 阴影
                float shadowShade = 1.0 - rawShadow;

                float diff = abs(ddx(shadowShade)) + abs(ddy(shadowShade));
                return diff;
            }

            // ===== 亮度检测 =====
            float CalcLuminance(float3 color)
            {
                //return dot(color, float3(0.299, 0.587, 0.114);
                return RGB2OKLAB(color).x;
            }

            // ===== 暗部排线 =====
            float HatchLinesAngle(float2 uv, float angle)
            {
                float s = sin(angle);
                float c = cos(angle);

                float2 p;
                p.x = uv.x * c - uv.y * s;
                p.y = uv.x * s + uv.y * c;

                float v = frac(p.y);
                return 1.0 - smoothstep(0.45, 0.55, v);
            }

            // 法1 | 按屏幕坐标
            float HatchPattern(float2 uv, float luminance)
            {
                float2 hatchUV = uv * _ScreenParams.xy;

                float scale1 = _HatchDensity;   // 基础密度
                float scale2 = scale1 / 1.2;           // 极暗高密层

                float hatch1 = HatchLinesAngle(hatchUV / scale1, 0.52);
                float hatch2 = HatchLinesAngle(hatchUV / scale2, -0.5);

                // 中暗
                float midMask =
                    1.0 - smoothstep(0.2, 0.5, luminance);

                // 暗区
                float darkMask =
                    1.0 - smoothstep(0, 0.3, luminance);

                // =========================
                // 合成
                // =========================
                float hatch = 0.0;

                hatch += hatch1 * midMask * 0.6;
                hatch += hatch2 * darkMask * 0.3;

                return hatch;
            }

            // 法2 | 按世界坐标
            // 阶1 | 从上往下投影
            float HatchPatternWS(float3 positionWS)
            {
                float2 hatchUV = positionWS.xz * (_HatchDensity * 0.05);

                float diag1 = frac(hatchUV.x + hatchUV.y);
                float line1 = 1.0 - smoothstep(0.45, 0.55, diag1);

                return line1;
            }

            // 阶2 | Triplanar (同时从xyz方向投影,根据表面法线朝向混合)

            float HatchPatternTriplanar(float3 positionWS, float3 normalWS, float luminance)
            {
                float3 n = abs(normalWS);
                n = pow(n, 4.0);
                n /= (n.x + n.y + n.z);

                float scale1 = _HatchDensity * 0.05;   // 基础密度
                float scale2 = scale1 * 2.0;           // 高密层

                // =========================
                // 第一层：中暗区（30° 单线）
                // =========================
                float hatch1 =
                    HatchLinesAngle(positionWS.yz * scale1, 0.52) * n.x +
                    HatchLinesAngle(positionWS.xz * scale1, 0.52) * n.y +
                    HatchLinesAngle(positionWS.xy * scale1, 0.52) * n.z;

                // =========================
                // 第二层：暗区（-25° 反向线）
                // =========================
                float hatch2 =
                    HatchLinesAngle(positionWS.yz * scale2, -0.44) * n.x +
                    HatchLinesAngle(positionWS.xz * scale2, -0.44) * n.y +
                    HatchLinesAngle(positionWS.xy * scale2, -0.44) * n.z;


                // =========================
                // 明暗分层 Mask
                // =========================

                // 中暗
                float midMask = 1.0 - smoothstep(0.2, 0.6, luminance);
                //float midMask = 1.0 - smoothstep(0, 0.3, luminance);


                // 暗区
                float darkMask = 1.0 - smoothstep(0, 0.3, luminance);
                //float darkMask = 1.0 - smoothstep(0, 0.2, luminance);

                // =========================
                // 合成
                // =========================
                float hatch = 0.0;

                hatch += hatch1 * midMask;
                hatch += hatch2 * darkMask * 0.1;

                return hatch;
            }

            float3 GetWorldPos(float2 uv)
            {
                float rawDepth = SampleSceneDepth(uv);

                float deviceDepth = rawDepth;
            #if !UNITY_REVERSED_Z
                deviceDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, rawDepth);
            #endif

                float4 clipPos = float4(uv * 2.0 - 1.0, deviceDepth, 1.0);

            #if UNITY_UV_STARTS_AT_TOP
                clipPos.y = -clipPos.y;
            #endif

                float4 worldPos = mul(UNITY_MATRIX_I_VP, clipPos);
                return worldPos.xyz / max(worldPos.w, 1e-5);
            }

            // ===== 亮部扫描线 =====
            float HighlightLinePattern(float2 uv)
            {
                float s = sin(_HighlightAngle);
                float c = cos(_HighlightAngle);

                float2x2 rot = float2x2(c, -s, s, c);
                float2 ruv = mul(rot, uv * _ScreenParams.xy);

                float stripePos = frac(ruv.y / _HighlightLineDensity);
                float distToCenter = abs(stripePos - 0.5);

                float band = 1.0 - smoothstep(_HighlightLineWidth, _HighlightLineWidth + 0.03, distToCenter);

                return band;
            }


            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float2 uv = i.texcoord;
                float3 positionWS = GetWorldPos(uv);
                float3 normalWS = SampleSceneNormalSafe(uv);

                float2 texelSize = 1.0 / _ScreenParams.xy;

                float2 jitteredUV = JitterUV(uv);

                float3 sceneColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, jitteredUV).rgb;
                // 通过颜色亮度控制排线
                float luminance = CalcLuminance(sceneColor);
                // 通过屏幕全局光阴影控制排线
                //float shadow = SAMPLE_TEXTURE2D_X(_ScreenSpaceShadowmapTexture, sampler_LinearClamp, uv).r;
                float shadow = SAMPLE_TEXTURE2D_X(_SSAO_OcclusionTexture,sampler_LinearClamp,uv).r;

                //====================
                //float depthEdge = CalcDepthEdge(uv, texelSize);
                //float normalEdge = CalcNormalEdge(uv, texelSize);
                //float edge = saturate(max(depthEdge, normalEdge) * _EdgeScale);
                //====================

                //====================
                // float edgeFade = GetEdgeDistanceFade(uv);

                // // 远处采样半径更小，避免一脚跨过多个面
                // // 第二个常数越小越激进
                // float radiusScale = lerp(1.0, 0.5, edgeFade);

                // // 远处阈值略微变严，避免边缘糊成片
                // float depthThresholdScale  = lerp(1.0, 1.15, edgeFade);
                // float normalThresholdScale = lerp(1.0, 1.35, edgeFade);

                // // 分别计算
                // float depthEdge  = CalcDepthEdge(uv, texelSize, radiusScale, depthThresholdScale);
                // float normalEdge = CalcNormalEdge(uv, texelSize, radiusScale, normalThresholdScale);

                // // 远处减少法线边缘影响，保住外轮廓但避免内部线糊成粗带
                // float normalWeight = lerp(1.0, 0.35, edgeFade);

                // // 远处整体略收一点，避免白边膨胀
                // float edgeWeight = lerp(1.0, 0.85, edgeFade);

                // float edge = saturate(max(depthEdge, normalEdge * normalWeight) * _EdgeScale * edgeWeight);
                // //float edge = depthEdge;
                // //float edge = normalEdge;
                //====================

                //====================
                float edgeFade = GetEdgeDistanceFade(uv);

                float radiusScale = lerp(1.0, 0.35, edgeFade);

                float depthThresholdScale  = lerp(1.0, 1.5, edgeFade);
                float normalThresholdScale = lerp(1.0, 1.5, edgeFade);

                float depthEdge  = CalcDepthEdge(uv, texelSize, radiusScale, depthThresholdScale);
                float normalEdge = CalcNormalEdge(uv, texelSize, radiusScale, normalThresholdScale);

                // 主光方向
                Light mainLight = GetMainLight();
                float3 lightDirWS = normalize(mainLight.direction);

                // 如果你发现明暗反了，就改成 -mainLight.direction
                float lightEdgeNdotL = CalcLightEdgeNdotL(normalWS, lightDirWS);
                float lightEdgeShadow = CalcShadowEdge(uv);

                // 光照边缘合并
                float lightEdge = lightEdgeNdotL * 0.45 + lightEdgeShadow * 0.85;


                // 远处减少深度边缘
                float depthWeight = lerp(1.0, 0.35, edgeFade);
                // 远处减少法线边缘，避免内部线糊成粗块
                float normalWeight = lerp(1.0, 0.35, edgeFade);

                // 远处也略微减少光照边缘，避免阴影边界膨胀
                float lightWeight = lerp(1.0, 0.75, edgeFade);

                float edgeWeight = lerp(1.0, 0.85, edgeFade);

                float outerEdge = depthEdge * depthWeight;
                float innerEdge = normalEdge * normalWeight;
                float illumEdge = lightEdge * lightWeight;

                float edge = saturate(
                    (outerEdge * _DepthEdgeWeight + innerEdge * _NormalEdgeWeight + illumEdge * _LightEdgeWeight)
                    * _EdgeScale
                    * edgeWeight
                );

                //====================

                float hatch = HatchPattern(uv,luminance);
                //float hatch = HatchPatternWS(positionWS);
                //float hatch = HatchPatternTriplanar(positionWS, normalWS, luminance);
                //float hatch = HatchPatternTriplanar(positionWS, normalWS, shadow);

                float shadowMask = 1.0 - smoothstep(_HatchShadowStart, 1.0, luminance);
                hatch *= shadowMask * _HatchStrength;

                float highlightMask = smoothstep(
                    _HighlightThreshold - _HighlightSoftness,
                    _HighlightThreshold + _HighlightSoftness,
                    luminance
                );

                float highlightLines = HighlightLinePattern(uv);
                highlightLines *= highlightMask * _HighlightStrength;

                float3 blueprintColor = _BlueprintBaseColor.rgb;
                float3 finalColor = blueprintColor;

                // 暗部斜线
                finalColor -= hatch.xxx;
                //finalColor += hatch.xxx;

                // 轮廓白线
                //finalColor -= edge.xxx;
                finalColor += edge.xxx;

                // 亮部扫描线
                //finalColor -= highlightLines.xxx;
                finalColor += highlightLines.xxx;

                // 少量保留原场景信息，避免纯死蓝
                float scenePreserve = (1.0 - _BlueprintIntensity) * 0.15;
                finalColor = lerp(finalColor, sceneColor, scenePreserve);

                return half4(saturate(finalColor), 1.0);
            }
            ENDHLSL
        }
    }
}
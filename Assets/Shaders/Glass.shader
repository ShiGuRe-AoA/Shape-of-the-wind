Shader "Custom/URP/PostProcess/Glass"
{
    Properties
    {
        [Header(Voronoi)]
        _CellDensity ("Cell Density", Range(1, 200)) = 40
        _AngleOffset ("Angle Offset", Range(0, 20)) = 5

        [Header(Stained Glass)]
        _StainedGlassTex ("Stained Glass Texture", 2D) = "white" {}
        _StainedGlassStrength ("Stained Glass Strength", Range(0, 1)) = 0.5
        _StainedGlassScale ("Stained Glass Scale", Range(0.1, 10)) = 1

        [Header(Edge)]
        _LineWidth ("Line Width", Range(0, 0.2)) = 0.02
        _LineSoftness ("Line Softness", Range(0.0001, 0.2)) = 0.02
        _LineColor ("Line Color", Color) = (0,0,0,1)

        [Header(Mix)]
        _EffectStrength ("Effect Strength", Range(0,1)) = 1

        [Header(Animation)]
        _DriftSpeed ("Drift Speed", Range(0, 2)) = 0.2
        _DriftAmount ("Drift Amount", Range(0, 2)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
        }

        Pass
        {
            Name "VoronoiMosaic"

            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM

            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            //================================================
            // Blit Texture
            //================================================

            TEXTURE2D_X(_BlitTexture);
            SAMPLER(sampler_BlitTexture);

            TEXTURE2D(_StainedGlassTex);
            SAMPLER(sampler_StainedGlassTex);
    
            //================================================
            // Parameters
            //================================================

            float _CellDensity;
            float _AngleOffset;

            float4 _StainedGlassTex_ST;
            float _StainedGlassStrength;
            float _StainedGlassScale;

            float _LineWidth;
            float _LineSoftness;
            float4 _LineColor;

            float _EffectStrength;

            float _DriftSpeed;
            float _DriftAmount;

            //================================================
            // Vertex
            //================================================

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;

                float2 uv = float2(
                    (input.vertexID << 1) & 2,
                    input.vertexID & 2
                );

                output.positionCS = float4(
                    uv * 2.0 - 1.0,
                    0.0,
                    1.0
                );

                output.uv = uv;

                #if UNITY_UV_STARTS_AT_TOP
                output.uv.y = 1.0 - output.uv.y;
                #endif

                return output;
            }

            //================================================
            // OKLAB
            //================================================

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

            //================================================
            // Voronoi Helpers
            //================================================

            inline float2 unity_voronoi_noise_randomVector(
                float2 UV,
                float offset
            )
            {
                float2x2 m = float2x2(
                    15.27, 47.63,
                    99.41, 89.98
                );

                UV = frac(sin(mul(UV, m)) * 46839.32);

                return float2(
                    sin(UV.y * offset) * 0.5 + 0.5,
                    cos(UV.x * offset) * 0.5 + 0.5
                );
            }

            float DistanceToBisector(
                float2 A,
                float2 B,
                float2 C
            )
            {
                float2 ab = B - A;

                if (dot(ab, ab) < 0.000001)
                    return 999.0;

                float2 mid = (A + B) * 0.5;

                return abs(
                    dot(
                        C - mid,
                        normalize(ab)
                    )
                );
            }

            //================================================
            // Voronoi
            //================================================

            void Unity_Voronoi_float(
                float2 UV,
                float2 UVMax,
                float AngleOffset,
                float CellDensity,

                out float F1,
                out float F2,
                out float EdgeDist,

                out float Cells,

                out float2 ControlPointUV1,
                out float2 ControlPointUV2
            )
            {
                CellDensity = max(CellDensity, 0.0001);

                //----------------------------------------
                // aspect corrected space
                //----------------------------------------

                float2 pUV = UV * CellDensity;

                float2 g = floor(pUV);
                float2 f = frac(pUV);

                //----------------------------------------
                // init
                //----------------------------------------

                F1 = 999.0;
                F2 = 999.0;

                EdgeDist = 999.0;

                Cells = 0.0;

                float2 p1 = 0.0;
                float2 p2 = 0.0;

                ControlPointUV1 = UV;
                ControlPointUV2 = UV;

                //----------------------------------------
                // search
                //----------------------------------------

                for (int y = -1; y <= 1; y++)
                {
                    for (int x = -1; x <= 1; x++)
                    {
                        float2 lattice = float2(x, y);

                        float2 offset =
                            unity_voronoi_noise_randomVector(
                                lattice + g,
                                AngleOffset
                            );

                        //--------------------------------
                        // local candidate
                        //--------------------------------

                        float2 candidateLocal =
                            lattice + offset;

                        //--------------------------------
                        // real uv
                        //--------------------------------

                        float2 candidateUV =
                            (g + lattice + offset)
                            / CellDensity;

                        //--------------------------------
                        // prevent outside uv sampling
                        //--------------------------------

                        bool inside =
                            candidateUV.x >= 0.0 &&
                            candidateUV.x <= UVMax.x &&

                            candidateUV.y >= 0.0 &&
                            candidateUV.y <= UVMax.y;

                        if (!inside)
                            continue;

                        //--------------------------------
                        // distance
                        //--------------------------------

                        float d =
                            distance(
                                candidateLocal,
                                f
                            );

                        //--------------------------------
                        // F1
                        //--------------------------------

                        if (d < F1)
                        {
                            F2 = F1;
                            p2 = p1;
                            ControlPointUV2 =
                                ControlPointUV1;

                            F1 = d;
                            p1 = candidateLocal;

                            Cells = offset.x;

                            ControlPointUV1 =
                                candidateUV;
                        }

                        //--------------------------------
                        // F2
                        //--------------------------------

                        else if (d < F2)
                        {
                            F2 = d;

                            p2 = candidateLocal;

                            ControlPointUV2 =
                                candidateUV;
                        }
                    }
                }

                //----------------------------------------
                // strict edge distance
                //----------------------------------------

                EdgeDist =
                    DistanceToBisector(
                        p1,
                        p2,
                        f
                    );
            }

            //================================================
            // Fragment
            //================================================

            half4 Frag(Varyings input) : SV_Target
            {
                //----------------------------------------
                // screen uv
                //----------------------------------------

                float2 screenUV = input.uv;

                //----------------------------------------
                // aspect correction
                //----------------------------------------

                float aspect =
                    _ScreenParams.x /
                    _ScreenParams.y;

                float2 voronoiUV = screenUV;
                voronoiUV.x *= aspect;

                float2 voronoiUVMax =
                    float2(aspect, 1.0);

                float animatedAngleOffset =
                    _AngleOffset +
                    sin(_Time.y * _DriftSpeed) * _DriftAmount;

                //----------------------------------------
                // voronoi
                //----------------------------------------

                float f1;
                float f2;

                float edgeDist;

                float cells;

                float2 controlPointUV1;
                float2 controlPointUV2;

                Unity_Voronoi_float(
                    voronoiUV,
                    voronoiUVMax,

                    //_AngleOffset,
                    animatedAngleOffset,
                    _CellDensity,

                    f1,
                    f2,
                    edgeDist,

                    cells,

                    controlPointUV1,
                    controlPointUV2
                );

                //----------------------------------------
                // restore aspect
                //----------------------------------------

                controlPointUV1.x /= aspect;
                controlPointUV2.x /= aspect;

                //----------------------------------------
                // safe sample
                //----------------------------------------

                controlPointUV1 =
                    clamp(
                        controlPointUV1,
                        0.001,
                        0.999
                    );

                //----------------------------------------
                // original color
                //----------------------------------------

                half4 originalCol =
                    SAMPLE_TEXTURE2D_X(
                        _BlitTexture,
                        sampler_BlitTexture,
                        screenUV
                    );

                //----------------------------------------
                // mosaic color
                //----------------------------------------

                half4 mosaicCol =
                    SAMPLE_TEXTURE2D_X(
                        _BlitTexture,
                        sampler_BlitTexture,
                        controlPointUV1
                    );

                float2 stainedUV = controlPointUV1 * _StainedGlassScale;
                half3 stainedCol = SAMPLE_TEXTURE2D(
                    _StainedGlassTex,
                    sampler_StainedGlassTex,
                    stainedUV
                ).rgb;

                // 保留屏幕明暗，只染色
                //half luminance = dot(mosaicCol.rgb, half3(0.299, 0.587, 0.114));
                half luminance = RGB2OKLAB(mosaicCol.rgb).r;
                half3 stainedGlassCol = stainedCol * luminance * 1.5;

                mosaicCol.rgb = lerp(
                    mosaicCol.rgb,
                    stainedGlassCol,
                    _StainedGlassStrength
                );

                //----------------------------------------
                // edge mask
                //----------------------------------------

                float edgeMask =
                    1.0 -
                    smoothstep(
                        _LineWidth,
                        _LineWidth + _LineSoftness,
                        edgeDist
                    );

                //----------------------------------------
                // edge mix
                //----------------------------------------

                half3 voronoiCol =
                    lerp(
                        mosaicCol.rgb,
                        _LineColor.rgb,
                        edgeMask * _LineColor.a
                    );

                //----------------------------------------
                // final mix
                //----------------------------------------

                half3 finalCol =
                    lerp(
                        originalCol.rgb,
                        voronoiCol,
                        _EffectStrength
                    );

                return half4(
                    finalCol,
                    1.0
                );
            }

            ENDHLSL
        }
    }
}
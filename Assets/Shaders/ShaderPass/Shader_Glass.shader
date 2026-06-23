Shader "Custom/URP/PostProcess/Glass"
{
    Properties
    {
        [Header(Voronoi)]
        _CellDensity ("Cell Density", Range(1, 200)) = 40
        _AngleOffset ("Angle Offset", Range(0, 20)) = 5

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

            #include "../HLSL/PostProcessCommon.hlsl"

            //================================================
            // Blit Texture
            //================================================
    
            //================================================
            // Parameters
            //================================================

            float _CellDensity;
            float _AngleOffset;

            float _LineWidth;
            float _LineSoftness;
            float4 _LineColor;

            float _EffectStrength;

            float _DriftSpeed;
            float _DriftAmount;

            //================================================
            // Vertex
            //================================================
            //================================================
            // OKLAB
            //================================================




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

                float2 screenUV = input.texcoord;

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
                        sampler_LinearClamp,
                        screenUV
                    );

                //----------------------------------------
                // mosaic color
                //----------------------------------------

                half4 mosaicCol =
                    SAMPLE_TEXTURE2D_X(
                        _BlitTexture,
                        sampler_LinearClamp,
                        controlPointUV1
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
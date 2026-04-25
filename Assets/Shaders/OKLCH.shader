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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D_X(_BlitTexture);
            SAMPLER(sampler_BlitTexture);

            float _LightnessOffset;
            float _ChromaScale;
            float _HueShift;

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

            float3 OKLAB2OKLCH(float3 lab)
            {
                float L = lab.x;
                float C = length(lab.yz);
                float H = atan2(lab.z, lab.y);

                return float3(L, C, H);
            }

            float3 OKLCH2OKLAB(float3 lch)
            {
                float L = lch.x;
                float C = lch.y;
                float H = lch.z;

                float a = cos(H) * C;
                float b = sin(H) * C;

                return float3(L, a, b);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_BlitTexture, input.uv).rgb;

                float3 lab = RGB2OKLAB(color);
                float3 lch = OKLAB2OKLCH(lab);

                lch.x += _LightnessOffset;
                lch.y *= _ChromaScale;
                lch.z += _HueShift;

                lab = OKLCH2OKLAB(lch);
                color = OKLAB2RGB(lab);

                return half4(saturate(color), 1.0);
            }

            ENDHLSL
        }
    }
}
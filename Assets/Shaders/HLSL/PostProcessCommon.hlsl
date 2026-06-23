#ifndef STYLIZED_POST_PROCESS_COMMON_INCLUDED
#define STYLIZED_POST_PROCESS_COMMON_INCLUDED

// URP fullscreen Blitter declarations:
// _BlitTexture, sampler_LinearClamp, Attributes, Varyings and Vert.
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

half4 SampleSource(float2 uv)
{
    return SAMPLE_TEXTURE2D_X(
        _BlitTexture,
        sampler_LinearClamp,
        uv
    );
}

float2 GetSourceTexelSize()
{
    return rcp(_ScreenParams.xy);
}

#endif

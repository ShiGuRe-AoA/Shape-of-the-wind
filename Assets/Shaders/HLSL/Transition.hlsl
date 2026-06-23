#ifndef STYLIZED_TRANSITION_INCLUDED
#define STYLIZED_TRANSITION_INCLUDED

// Core.hlsl must be included before this file.

TEXTURE2D(_TransitionNoise);
SAMPLER(sampler_TransitionNoise);

float4 _TransitionNoise_ST;

float _TransitionProgress;
float _TransitionSoftness;
float _TransitionWorldSize;
float4 _TransitionOffset;

float EvaluateTransitionMask(
    float noiseValue,
    float progress,
    float softness)
{
    progress = saturate(progress);
    softness = max(
        softness,
        0.0001
    );

    // Extending the threshold beyond [0, 1]
    // makes both endpoints exact:
    // progress 0 -> mask 0
    // progress 1 -> mask 1.
    float threshold = lerp(
        1.0 + softness,
        -softness,
        progress
    );

    return smoothstep(
        threshold - softness,
        threshold + softness,
        noiseValue
    );
}

float SampleScreenTransition(float2 uv)
{
    float2 noiseUV =
        uv * _TransitionNoise_ST.xy +
        _TransitionNoise_ST.zw +
        _TransitionOffset.xy;

    return SAMPLE_TEXTURE2D(
        _TransitionNoise,
        sampler_TransitionNoise,
        noiseUV
    ).r;
}

float SampleWorldTransition(
    float3 positionWS)
{
    float worldSize = max(
        _TransitionWorldSize,
        0.0001
    );

    float2 noiseUV =
        positionWS.xz / worldSize +
        _TransitionOffset.xy;

    return SAMPLE_TEXTURE2D(
        _TransitionNoise,
        sampler_TransitionNoise,
        noiseUV
    ).r;
}

float3 ApplyTransition(
    float3 originalColor,
    float3 effectColor,
    float noiseValue)
{
    float transitionMask =
        EvaluateTransitionMask(
            noiseValue,
            _TransitionProgress,
            _TransitionSoftness
        );

    return lerp(
        originalColor,
        effectColor,
        transitionMask
    );
}

#endif
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Runtime controller for transitioning between complete style stacks.
/// Persistent Before/After passes are owned by PostProcessRenderFeature and are
/// never replaced by this controller.
/// </summary>
public class StyleTransitionController : MonoBehaviour
{
    [Serializable]
    public class StylePreset
    {
        [Tooltip("Runtime identifier used by TransitionToPreset(string).")]
        public string id;

        public PostProcessRenderFeature_New.EffectStack style =
            new PostProcessRenderFeature_New.EffectStack();
    }

    [Header("Renderer Feature")]
    [SerializeField]
    private ScriptableRendererData rendererData;
    
    private PostProcessRenderFeature_New renderFeature;

    [Header("Initial Style Stack")]
    [SerializeField]
    private PostProcessRenderFeature_New.EffectStack initialStyle =
        new PostProcessRenderFeature_New.EffectStack();

    [Header("Named Style Presets")]
    [SerializeField]
    private StylePreset[] presets =
        new StylePreset[0];

    [Header("Animation")]
    [SerializeField, Min(0.01f)]
    private float defaultDuration = 1f;

    private PostProcessRenderFeature_New.EffectStack
        currentStyle;

    private Coroutine transitionCoroutine;

    private void Awake()
    {
        FindRenderFeature();

        if (renderFeature == null)
            return;

        currentStyle =
            PostProcessRenderFeature_New.CloneStack(
                initialStyle
            );

        renderFeature.SetStyleStacks(
            currentStyle,
            currentStyle
        );

        renderFeature.SetTransitionProgress(0f);
    }

    private void FindRenderFeature()
    {
        if (rendererData == null)
        {
            Debug.LogError(
                "[StyleTransitionController] Renderer Data is null.",
                this
            );

            return;
        }

        foreach (ScriptableRendererFeature feature
                 in rendererData.rendererFeatures)
        {
            if (feature is PostProcessRenderFeature_New targetFeature)
            {
                renderFeature = targetFeature;
                return;
            }
        }

        Debug.LogError(
            $"[StyleTransitionController] " +
            $"Renderer Data '{rendererData.name}' 中没有找到 " +
            $"{nameof(PostProcessRenderFeature)}。",
            this
        );
    }

    public void TransitionToPreset(
        string presetId)
    {
        TransitionToPreset(
            presetId,
            defaultDuration
        );
    }

    public void TransitionToPreset(
        string presetId,
        float duration)
    {
        StylePreset preset =
            FindPreset(presetId);

        if (preset == null)
        {
            Debug.LogWarning(
                $"[StyleTransitionController] " +
                $"Style preset '{presetId}' " +
                $"was not found.",
                this
            );

            return;
        }

        TransitionToStack(
            preset.style,
            duration
        );
    }

    public void TransitionToStack(
        PostProcessRenderFeature_New.EffectStack
            targetStyle)
    {
        TransitionToStack(
            targetStyle,
            defaultDuration
        );
    }

    public void TransitionToStack(
        PostProcessRenderFeature_New.EffectStack
            targetStyle,
        float duration)
    {
        if (renderFeature == null)
            return;

        PostProcessRenderFeature_New.EffectStack
            targetCopy =
                PostProcessRenderFeature_New.CloneStack(
                    targetStyle
                );

        if (transitionCoroutine != null)
        {
            StopCoroutine(
                transitionCoroutine
            );
        }

        transitionCoroutine =
            StartCoroutine(
                TransitionRoutine(
                    targetCopy,
                    Mathf.Max(
                        duration,
                        0.01f
                    )
                )
            );
    }

    /// <summary>
    /// Convenience overload for a style containing
    /// one named ShaderLab pass.
    /// </summary>
    public void TransitionTo(
        Material targetMaterial,
        string targetPassName)
    {
        TransitionTo(
            targetMaterial,
            targetPassName,
            defaultDuration
        );
    }

    public void TransitionTo(
        Material targetMaterial,
        string targetPassName,
        float duration)
    {
        TransitionToStack(
            PostProcessRenderFeature_New
                .CreateSinglePassStack(
                    targetMaterial,
                    targetPassName
                ),
            duration
        );
    }

    public void TransitionToOriginal()
    {
        TransitionToOriginal(
            defaultDuration
        );
    }

    public void TransitionToOriginal(
        float duration)
    {
        TransitionToStack(
            PostProcessRenderFeature_New
                .CreateOriginalStack(),
            duration
        );
    }

    private IEnumerator TransitionRoutine(
        PostProcessRenderFeature_New.EffectStack
            targetStyle,
        float duration)
    {
        // Only the From/To style stacks change.
        // Persistent passes configured on the
        // Renderer Feature stay active throughout
        // the transition.
        renderFeature.SetStyleStacks(
            currentStyle,
            targetStyle
        );

        renderFeature.SetTransitionProgress(0f);

        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed +=
                Time.unscaledDeltaTime;

            float t =
                Mathf.Clamp01(
                    elapsed / duration
                );

            // Smooth temporal progression.
            // Spatial breakup is produced by the
            // transition noise in
            // StyleTransitionComposite.shader.
            t = t * t * (3f - 2f * t);

            renderFeature.SetTransitionProgress(t);

            yield return null;
        }

        currentStyle =
            PostProcessRenderFeature_New.CloneStack(
                targetStyle
            );

        // Collapse both branches onto the completed
        // target so the stable state evaluates only
        // one style stack.
        renderFeature.SetStyleStacks(
            currentStyle,
            currentStyle
        );

        renderFeature.SetTransitionProgress(0f);

        transitionCoroutine = null;
    }

    private StylePreset FindPreset(
        string presetId)
    {
        if (presets == null)
            return null;

        for (int i = 0;
             i < presets.Length;
             i++)
        {
            StylePreset preset =
                presets[i];

            if (preset != null &&
                string.Equals(
                    preset.id,
                    presetId,
                    StringComparison.Ordinal
                ))
            {
                return preset;
            }
        }

        return null;
    }
}
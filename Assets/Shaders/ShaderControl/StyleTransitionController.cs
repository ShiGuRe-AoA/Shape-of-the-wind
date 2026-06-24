using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 控制完整风格栈之间的过渡。
///
/// 当过渡过程中收到新目标时，不会立刻停止当前过渡，
/// 而是只保存最新目标，在当前过渡结束后继续切换，
/// 避免出现 1 -> 2 -> 1 -> 3。
/// </summary>
public class StyleTransitionController : MonoBehaviour
{
    [Serializable]
    public class StylePreset
    {
        [Tooltip("供 TransitionToPreset 调用的风格 ID。")]
        public string id;

        [Tooltip("该风格包含的完整 Pass 栈。")]
        public PostProcessRenderFeature_New.EffectStack style =
            new PostProcessRenderFeature_New.EffectStack();
    }

    [Header("Renderer Feature")]

    [Tooltip("包含 PostProcessRenderFeature 的 Renderer Data。")]
    [SerializeField]
    private ScriptableRendererData rendererData;

    [Header("Initial Style")]

    [SerializeField]
    private PostProcessRenderFeature_New.EffectStack initialStyle =
        new PostProcessRenderFeature_New.EffectStack();

    [Header("Style Presets")]

    [SerializeField]
    private StylePreset[] presets =
        Array.Empty<StylePreset>();

    [Header("Transition")]

    [SerializeField, Min(0.01f)]
    private float defaultDuration = 1f;

    private PostProcessRenderFeature_New renderFeature;

    // 当前已经完成并稳定显示的风格。
    private PostProcessRenderFeature_New.EffectStack currentStyle;

    private Coroutine transitionCoroutine;

    // 当前过渡期间收到的最新目标。
    // 旧的待处理目标会被新目标覆盖。
    private PostProcessRenderFeature_New.EffectStack pendingStyle;
    private float pendingDuration;
    private bool hasPendingTransition;

    private void Awake()
    {
        FindRenderFeature();

        if (renderFeature == null)
        {
            Debug.LogError(
                "[StyleTransitionController] " +
                "没有找到 PostProcessRenderFeature。",
                this
            );

            enabled = false;
            return;
        }

        currentStyle =
            PostProcessRenderFeature_New.CloneStack(
                initialStyle
            );

        // 初始状态下 From 和 To 相同，
        // 因此只需要渲染一个风格栈。
        renderFeature.SetStyleStacks(
            currentStyle,
            currentStyle
        );

        renderFeature.SetTransitionProgress(0f);
    }

    private void OnDisable()
    {
        if (transitionCoroutine != null)
        {
            StopCoroutine(transitionCoroutine);
            transitionCoroutine = null;
        }

        pendingStyle = null;
        hasPendingTransition = false;

        // 禁用时恢复到最后完整完成的风格，
        // 避免画面停留在过渡中间状态。
        if (renderFeature != null &&
            currentStyle != null)
        {
            renderFeature.SetStyleStacks(
                currentStyle,
                currentStyle
            );

            renderFeature.SetTransitionProgress(0f);
        }
    }

    /// <summary>
    /// 根据 ID 切换到预设风格。
    /// </summary>
    public void TransitionToPreset(string presetId)
    {
        TransitionToPreset(
            presetId,
            defaultDuration
        );
    }

    /// <summary>
    /// 根据 ID 切换到预设风格。
    /// </summary>
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
                $"找不到风格预设：'{presetId}'。",
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
        PostProcessRenderFeature_New.EffectStack targetStyle)
    {
        TransitionToStack(
            targetStyle,
            defaultDuration
        );
    }

    /// <summary>
    /// 切换到指定的完整风格栈。
    ///
    /// 如果当前正在过渡，不会打断当前协程，
    /// 而是保存最新目标，当前过渡结束后继续处理。
    /// </summary>
    public void TransitionToStack(
        PostProcessRenderFeature_New.EffectStack targetStyle,
        float duration)
    {
        if (renderFeature == null)
            return;

        PostProcessRenderFeature_New.EffectStack targetCopy =
            PostProcessRenderFeature_New.CloneStack(
                targetStyle
            );

        float safeDuration =
            Mathf.Max(duration, 0.01f);

        // 当前正在过渡时，不停止旧协程。
        // 只保存最新目标，避免重新从旧 currentStyle 开始。
        if (transitionCoroutine != null)
        {
            pendingStyle = targetCopy;
            pendingDuration = safeDuration;
            hasPendingTransition = true;

            return;
        }

        // 当前已经处于目标风格，不重复切换。
        if (AreStylesEqual(
                currentStyle,
                targetCopy))
        {
            return;
        }

        transitionCoroutine =
            StartCoroutine(
                TransitionRoutine(
                    targetCopy,
                    safeDuration
                )
            );
    }

    /// <summary>
    /// 切换到只包含一个 Pass 的风格。
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

    /// <summary>
    /// 切换到只包含一个 Pass 的风格。
    /// </summary>
    public void TransitionTo(
        Material targetMaterial,
        string targetPassName,
        float duration)
    {
        PostProcessRenderFeature_New.EffectStack targetStyle =
            PostProcessRenderFeature_New.CreateSinglePassStack(
                targetMaterial,
                targetPassName
            );

        TransitionToStack(
            targetStyle,
            duration
        );
    }

    /// <summary>
    /// 切换到无风格状态。
    /// Persistent Before 和 Persistent After 仍会保留。
    /// </summary>
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
            PostProcessRenderFeature_New.CreateOriginalStack(),
            duration
        );
    }

    private IEnumerator TransitionRoutine(
        PostProcessRenderFeature_New.EffectStack targetStyle,
        float duration)
    {
        // 当前完整风格作为 From，
        // 新目标作为 To。
        renderFeature.SetStyleStacks(
            currentStyle,
            targetStyle
        );

        renderFeature.SetTransitionProgress(0f);

        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;

            float progress =
                Mathf.Clamp01(
                    elapsed / duration
                );

            // SmoothStep 时间曲线。
            // 空间上的破碎过渡由 Composite Shader 的 Noise 决定。
            progress =
                progress *
                progress *
                (3f - 2f * progress);

            renderFeature.SetTransitionProgress(
                progress
            );

            yield return null;
        }

        // 当前过渡完整结束后，
        // 目标风格才正式成为 currentStyle。
        currentStyle =
            PostProcessRenderFeature_New.CloneStack(
                targetStyle
            );

        // 将 From 和 To 收拢为相同风格。
        // 稳定状态只会计算一个风格分支。
        renderFeature.SetStyleStacks(
            currentStyle,
            currentStyle
        );

        renderFeature.SetTransitionProgress(0f);

        transitionCoroutine = null;

        // 过渡期间可能经过了多个高度区域。
        // 这里只处理最后收到的目标。
        if (hasPendingTransition)
        {
            PostProcessRenderFeature_New.EffectStack nextStyle =
                pendingStyle;

            float nextDuration =
                pendingDuration;

            pendingStyle = null;
            hasPendingTransition = false;

            if (!AreStylesEqual(
                    currentStyle,
                    nextStyle))
            {
                TransitionToStack(
                    nextStyle,
                    nextDuration
                );
            }
        }
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

            if (preset == null)
                continue;

            if (string.Equals(
                    preset.id,
                    presetId,
                    StringComparison.Ordinal))
            {
                return preset;
            }
        }

        return null;
    }

    /// <summary>
    /// 从 Renderer Data 中找到 PostProcessRenderFeature。
    /// </summary>
    private void FindRenderFeature()
    {
        renderFeature = null;

        if (rendererData == null)
        {
            Debug.LogError(
                "[StyleTransitionController] " +
                "Renderer Data 没有设置。",
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

                Debug.Log(
                    $"[StyleTransitionController] " +
                    $"已找到 Renderer Feature：{targetFeature.name}",
                    this
                );

                return;
            }
        }

        Debug.LogError(
            $"[StyleTransitionController] " +
            $"Renderer Data '{rendererData.name}' 中没有找到 " +
            $"{nameof(PostProcessRenderFeature_New)}。",
            this
        );
    }

    /// <summary>
    /// 比较两个风格栈是否完全相同。
    /// </summary>
    private static bool AreStylesEqual(
        PostProcessRenderFeature_New.EffectStack a,
        PostProcessRenderFeature_New.EffectStack b)
    {
        if (ReferenceEquals(a, b))
            return true;

        if (a == null || b == null)
            return false;

        if (a.useOriginal != b.useOriginal)
            return false;

        // 两边都是原图时，无需继续比较 Pass。
        if (a.useOriginal)
            return true;

        PostProcessRenderFeature_New.EffectPassEntry[] passesA =
            a.passes ??
            Array.Empty<PostProcessRenderFeature_New.EffectPassEntry>();

        PostProcessRenderFeature_New.EffectPassEntry[] passesB =
            b.passes ??
            Array.Empty<PostProcessRenderFeature_New.EffectPassEntry>();

        if (passesA.Length != passesB.Length)
            return false;

        for (int i = 0;
             i < passesA.Length;
             i++)
        {
            PostProcessRenderFeature_New.EffectPassEntry passA =
                passesA[i];

            PostProcessRenderFeature_New.EffectPassEntry passB =
                passesB[i];

            if (ReferenceEquals(passA, passB))
                continue;

            if (passA == null || passB == null)
                return false;

            if (passA.available != passB.available)
                return false;

            if (passA.material != passB.material)
                return false;

            if (!string.Equals(
                    passA.passName,
                    passB.passName,
                    StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
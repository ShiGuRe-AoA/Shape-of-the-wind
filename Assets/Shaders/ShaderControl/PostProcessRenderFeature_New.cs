using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Stylized post-processing pipeline:
///
/// Camera
///   -> Persistent Before[]
///   -> From Style[] / To Style[]
///   -> Noise Composite
///   -> Persistent After[]
///   -> Camera
///
/// Every entry selects one named ShaderLab Pass. A style may contain multiple
/// sequential passes, while persistent passes remain active across style changes.
/// </summary>
public class PostProcessRenderFeature_New : ScriptableRendererFeature
{
    private static readonly int TransitionProgressId =
        Shader.PropertyToID("_TransitionProgress");

    [Serializable]
    public class EffectPassEntry
    {
        [Tooltip("Whether this pass participates in the stack.")]
        public bool available = true;

        [Tooltip("Material containing the ShaderLab pass.")]
        public Material material;

        [Tooltip("ShaderLab Pass Name. Empty means pass index 0.")]
        public string passName;
    }

    [Serializable]
    public class EffectStack
    {
        [Tooltip("Ignore the pass list and use the input image unchanged.")]
        public bool useOriginal;

        [Tooltip("Passes are executed from top to bottom.")]
        public EffectPassEntry[] passes = new EffectPassEntry[0];
    }

    [Serializable]
    public class Settings
    {
        [Header("Persistent Before")]
        [Tooltip("Always executed before both style branches.")]
        public EffectPassEntry[] persistentBefore =
            new EffectPassEntry[0];

        [Header("Transition Styles")]
        public EffectStack fromStyle = new EffectStack();
        public EffectStack toStyle = new EffectStack();

        [Header("Composite")]
        [Tooltip(
            "Material using Hidden/Custom/URP/StyleTransitionComposite."
        )]
        public Material compositeMaterial;

        [Tooltip("Composite ShaderLab Pass Name.")]
        public string compositePassName = "StyleTransition";

        [Header("Persistent After")]
        [Tooltip("Always executed after the style transition composite.")]
        public EffectPassEntry[] persistentAfter =
            new EffectPassEntry[0];

        [Header("Render")]
        public RenderPassEvent passEvent =
            RenderPassEvent.AfterRenderingPostProcessing;

        public bool renderInSceneView = true;

        [Tooltip("Enable when any configured effect samples scene depth.")]
        public bool requireDepth = true;

        [Tooltip("Enable when any configured effect samples scene normals.")]
        public bool requireNormals = true;
    }

    private sealed class StackedStyleTransitionPass
        : ScriptableRenderPass
    {
        private static readonly int StyleBTextureId =
            Shader.PropertyToID("_StyleBTexture");

        private readonly ProfilingSampler profilingSampler =
            new ProfilingSampler(
                "Stacked Stylized Style Transition"
            );

        private readonly HashSet<string> warningCache =
            new HashSet<string>();

        private Settings settings;

        // Result after Persistent Before[]. This texture must remain
        // unchanged while both style branches are rendered from it.
        private RTHandle baseRT;

        // Reused as a ping-pong target for Persistent Before[] and
        // later as the composite output. These stages never overlap.
        private RTHandle utilityRT;

        private RTHandle styleART;
        private RTHandle styleBRT;

        // Shared scratch texture. Style A, Style B and
        // Persistent After[] are rendered sequentially, so one
        // scratch target is sufficient.
        private RTHandle stackScratchRT;

        public StackedStyleTransitionPass(Settings settings)
        {
            SetSettings(settings);
        }

        public void SetSettings(Settings newSettings)
        {
            settings = newSettings;

            if (settings == null)
                return;

            renderPassEvent = settings.passEvent;

            ScriptableRenderPassInput input =
                ScriptableRenderPassInput.None;

            if (settings.requireDepth)
                input |= ScriptableRenderPassInput.Depth;

            if (settings.requireNormals)
                input |= ScriptableRenderPassInput.Normal;

            ConfigureInput(input);
        }

        public override void OnCameraSetup(
            CommandBuffer cmd,
            ref RenderingData renderingData)
        {
            if (settings == null)
                return;

            RenderTextureDescriptor descriptor =
                renderingData.cameraData.cameraTargetDescriptor;

            descriptor.depthBufferBits = 0;
            descriptor.msaaSamples = 1;

            RenderingUtils.ReAllocateIfNeeded(
                ref baseRT,
                descriptor,
                FilterMode.Bilinear,
                TextureWrapMode.Clamp,
                name: "_StylizedBase"
            );

            RenderingUtils.ReAllocateIfNeeded(
                ref utilityRT,
                descriptor,
                FilterMode.Bilinear,
                TextureWrapMode.Clamp,
                name: "_StylizedUtility"
            );

            RenderingUtils.ReAllocateIfNeeded(
                ref styleART,
                descriptor,
                FilterMode.Bilinear,
                TextureWrapMode.Clamp,
                name: "_StylizedStyleA"
            );

            RenderingUtils.ReAllocateIfNeeded(
                ref styleBRT,
                descriptor,
                FilterMode.Bilinear,
                TextureWrapMode.Clamp,
                name: "_StylizedStyleB"
            );

            RenderingUtils.ReAllocateIfNeeded(
                ref stackScratchRT,
                descriptor,
                FilterMode.Bilinear,
                TextureWrapMode.Clamp,
                name: "_StylizedStackScratch"
            );
        }

        public override void Execute(
            ScriptableRenderContext context,
            ref RenderingData renderingData)
        {
            if (settings == null)
                return;

            CameraData cameraData =
                renderingData.cameraData;

            if (cameraData.isPreviewCamera)
                return;

            if (!settings.renderInSceneView &&
                cameraData.isSceneViewCamera)
            {
                return;
            }

            RTHandle cameraColorTarget =
                cameraData.renderer.cameraColorTargetHandle;

            CommandBuffer cmd =
                CommandBufferPool.Get(
                    "Stacked Stylized Style Transition"
                );

            using (new ProfilingScope(
                cmd,
                profilingSampler))
            {
                // 1. Fixed preprocessing shared by both
                // style branches.
                RenderPassStack(
                    cmd,
                    cameraColorTarget,
                    baseRT,
                    utilityRT,
                    settings.persistentBefore,
                    "persistent-before"
                );

                float progress =
                    GetTransitionProgress();

                // 2. Endpoint optimization:
                // only evaluate the visible style.
                if (progress <= 0.0001f)
                {
                    RenderStyleStack(
                        cmd,
                        baseRT,
                        styleART,
                        stackScratchRT,
                        settings.fromStyle,
                        "from-style"
                    );

                    RenderPassStack(
                        cmd,
                        styleART,
                        cameraColorTarget,
                        utilityRT,
                        settings.persistentAfter,
                        "persistent-after"
                    );
                }
                else if (progress >= 0.9999f)
                {
                    RenderStyleStack(
                        cmd,
                        baseRT,
                        styleBRT,
                        stackScratchRT,
                        settings.toStyle,
                        "to-style"
                    );

                    RenderPassStack(
                        cmd,
                        styleBRT,
                        cameraColorTarget,
                        utilityRT,
                        settings.persistentAfter,
                        "persistent-after"
                    );
                }
                else
                {
                    // Both style stacks start from the exact
                    // same preprocessed image.
                    RenderStyleStack(
                        cmd,
                        baseRT,
                        styleART,
                        stackScratchRT,
                        settings.fromStyle,
                        "from-style"
                    );

                    RenderStyleStack(
                        cmd,
                        baseRT,
                        styleBRT,
                        stackScratchRT,
                        settings.toStyle,
                        "to-style"
                    );

                    CompositeStyles(cmd);

                    // 4. Fixed postprocessing remains active
                    // after every transition.
                    RenderPassStack(
                        cmd,
                        utilityRT,
                        cameraColorTarget,
                        stackScratchRT,
                        settings.persistentAfter,
                        "persistent-after"
                    );
                }
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        private void CompositeStyles(CommandBuffer cmd)
        {
            int compositePass = ResolvePassIndex(
                settings.compositeMaterial,
                settings.compositePassName,
                "composite"
            );

            if (compositePass < 0)
            {
                // Missing composite material/pass:
                // safely display style A.
                Blitter.BlitCameraTexture(
                    cmd,
                    styleART,
                    utilityRT
                );

                return;
            }

            // _BlitTexture receives Style A from Blitter.
            // Style B is supplied separately for the final
            // noise-driven interpolation.
            cmd.SetGlobalTexture(
                StyleBTextureId,
                styleBRT.nameID
            );

            Blitter.BlitCameraTexture(
                cmd,
                styleART,
                utilityRT,
                settings.compositeMaterial,
                compositePass
            );
        }

        private float GetTransitionProgress()
        {
            Material material =
                settings.compositeMaterial;

            if (material == null ||
                !material.HasProperty(
                    TransitionProgressId))
            {
                return 0f;
            }

            return Mathf.Clamp01(
                material.GetFloat(
                    TransitionProgressId
                )
            );
        }

        private void RenderStyleStack(
            CommandBuffer cmd,
            RTHandle source,
            RTHandle destination,
            RTHandle scratch,
            EffectStack style,
            string usage)
        {
            if (style == null ||
                style.useOriginal)
            {
                Blitter.BlitCameraTexture(
                    cmd,
                    source,
                    destination
                );

                return;
            }

            RenderPassStack(
                cmd,
                source,
                destination,
                scratch,
                style.passes,
                usage
            );
        }

        /// <summary>
        /// Executes valid entries sequentially and guarantees
        /// that the final result ends in finalDestination.
        /// Invalid/disabled entries are skipped.
        /// </summary>
        private void RenderPassStack(
            CommandBuffer cmd,
            RTHandle source,
            RTHandle finalDestination,
            RTHandle scratch,
            EffectPassEntry[] entries,
            string usage)
        {
            RTHandle currentSource = source;
            RTHandle currentDestination =
                finalDestination;

            bool executedAnyPass = false;

            if (entries != null)
            {
                for (int i = 0;
                     i < entries.Length;
                     i++)
                {
                    EffectPassEntry entry =
                        entries[i];

                    if (entry == null ||
                        !entry.available ||
                        entry.material == null)
                    {
                        continue;
                    }

                    int passIndex =
                        ResolvePassIndex(
                            entry.material,
                            entry.passName,
                            usage
                        );

                    if (passIndex < 0)
                        continue;

                    Blitter.BlitCameraTexture(
                        cmd,
                        currentSource,
                        currentDestination,
                        entry.material,
                        passIndex
                    );

                    executedAnyPass = true;
                    currentSource =
                        currentDestination;

                    currentDestination =
                        ReferenceEquals(
                            currentDestination,
                            finalDestination
                        )
                            ? scratch
                            : finalDestination;
                }
            }

            if (!executedAnyPass)
            {
                Blitter.BlitCameraTexture(
                    cmd,
                    source,
                    finalDestination
                );

                return;
            }

            // An even number of executed passes ends in
            // scratch. Copy it back so callers can always
            // consume finalDestination without tracking parity.
            if (!ReferenceEquals(
                currentSource,
                finalDestination))
            {
                Blitter.BlitCameraTexture(
                    cmd,
                    currentSource,
                    finalDestination
                );
            }
        }

        private int ResolvePassIndex(
            Material material,
            string passName,
            string usage)
        {
            if (material == null)
                return -1;

            if (string.IsNullOrWhiteSpace(
                passName))
            {
                return material.passCount > 0
                    ? 0
                    : -1;
            }

            int passIndex =
                material.FindPass(passName);

            if (passIndex >= 0)
                return passIndex;

            string warningKey =
                material.GetInstanceID() +
                ":" +
                passName +
                ":" +
                usage;

            if (warningCache.Add(warningKey))
            {
                Debug.LogWarning(
                    $"[PostProcessRenderFeature] " +
                    $"Shader '{material.shader.name}' " +
                    $"does not contain the {usage} " +
                    $"pass '{passName}'."
                );
            }

            return -1;
        }

        public void Dispose()
        {
            baseRT?.Release();
            utilityRT?.Release();
            styleART?.Release();
            styleBRT?.Release();
            stackScratchRT?.Release();
        }
    }

    public Settings settings =
        new Settings();

    private StackedStyleTransitionPass
        transitionPass;

    public override void Create()
    {
        transitionPass =
            new StackedStyleTransitionPass(
                settings
            );
    }

    public override void AddRenderPasses(
        ScriptableRenderer renderer,
        ref RenderingData renderingData)
    {
        if (transitionPass == null)
            return;

        transitionPass.SetSettings(settings);
        renderer.EnqueuePass(transitionPass);
    }

    /// <summary>
    /// Replaces only the switchable style branches.
    /// Persistent Before/After settings remain untouched.
    /// </summary>
    public void SetStyleStacks(
        EffectStack fromStyle,
        EffectStack toStyle)
    {
        settings.fromStyle =
            CloneStack(fromStyle);

        settings.toStyle =
            CloneStack(toStyle);
    }

    public void SetFromStyle(EffectStack style)
    {
        settings.fromStyle =
            CloneStack(style);
    }

    public void SetToStyle(EffectStack style)
    {
        settings.toStyle =
            CloneStack(style);
    }

    public void SetPersistentBefore(
        EffectPassEntry[] passes)
    {
        settings.persistentBefore =
            ClonePassArray(passes);
    }

    public void SetPersistentAfter(
        EffectPassEntry[] passes)
    {
        settings.persistentAfter =
            ClonePassArray(passes);
    }

    /// <summary>
    /// Backward-compatible convenience API for a style
    /// containing one pass. A null material means the
    /// original/preprocessed image.
    /// </summary>
    public void SetStyles(
        Material fromMaterial,
        string fromPassName,
        Material toMaterial,
        string toPassName)
    {
        SetStyleStacks(
            CreateSinglePassStack(
                fromMaterial,
                fromPassName
            ),
            CreateSinglePassStack(
                toMaterial,
                toPassName
            )
        );
    }

    public void SetTransitionProgress(
        float progress)
    {
        if (settings.compositeMaterial == null)
            return;

        settings.compositeMaterial.SetFloat(
            TransitionProgressId,
            Mathf.Clamp01(progress)
        );
    }

    public static EffectStack CreateOriginalStack()
    {
        return new EffectStack
        {
            useOriginal = true,
            passes = new EffectPassEntry[0]
        };
    }

    public static EffectStack CreateSinglePassStack(
        Material material,
        string passName)
    {
        if (material == null)
            return CreateOriginalStack();

        return new EffectStack
        {
            useOriginal = false,

            passes = new[]
            {
                new EffectPassEntry
                {
                    available = true,
                    material = material,
                    passName = passName
                }
            }
        };
    }

    public static EffectStack CloneStack(
        EffectStack source)
    {
        if (source == null)
            return CreateOriginalStack();

        return new EffectStack
        {
            useOriginal =
                source.useOriginal,

            passes =
                ClonePassArray(
                    source.passes
                )
        };
    }

    public static EffectPassEntry[] ClonePassArray(
        EffectPassEntry[] source)
    {
        if (source == null ||
            source.Length == 0)
        {
            return new EffectPassEntry[0];
        }

        EffectPassEntry[] result =
            new EffectPassEntry[source.Length];

        for (int i = 0;
             i < source.Length;
             i++)
        {
            EffectPassEntry entry =
                source[i];

            if (entry == null)
                continue;

            result[i] =
                new EffectPassEntry
                {
                    available =
                        entry.available,

                    material =
                        entry.material,

                    passName =
                        entry.passName
                };
        }

        return result;
    }

    protected override void Dispose(
        bool disposing)
    {
        transitionPass?.Dispose();
    }
}
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class ShadowMaskRenderFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public Material processMaterial;
        public RenderPassEvent passEvent = RenderPassEvent.AfterRenderingPrePasses;
        public string outputTextureName = "_BlueprintShadowMaskTex";
    }

    class ShadowMaskPass : ScriptableRenderPass
    {
        private static readonly string ProfilerTag = "Blueprint Shadow Mask Pass";

        private readonly Material processMaterial;
        private readonly int outputTextureId;

        private RTHandle tempSource;
        private RTHandle shadowMaskRT;

        public ShadowMaskPass(Material processMaterial, RenderPassEvent passEvent, string outputTextureName)
        {
            this.processMaterial = processMaterial;
            this.renderPassEvent = passEvent;
            outputTextureId = Shader.PropertyToID(outputTextureName);
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            if (processMaterial == null) return;

            RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            desc.colorFormat = RenderTextureFormat.R8;

            RenderingUtils.ReAllocateIfNeeded(
                ref tempSource,
                desc,
                FilterMode.Bilinear,
                TextureWrapMode.Clamp,
                name: "_BlueprintShadowMaskTemp"
            );

            RenderingUtils.ReAllocateIfNeeded(
                ref shadowMaskRT,
                desc,
                FilterMode.Bilinear,
                TextureWrapMode.Clamp,
                name: "_BlueprintShadowMaskRT"
            );
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (processMaterial == null) return;
            if (renderingData.cameraData.isPreviewCamera) return;

            CommandBuffer cmd = CommandBufferPool.Get(ProfilerTag);

            // 先把任意有效输入 blit 到 tempSource，只是为了驱动 fullscreen pass
            RTHandle cameraColorTarget = renderingData.cameraData.renderer.cameraColorTargetHandle;
            Blitter.BlitCameraTexture(cmd, cameraColorTarget, tempSource);

            // 再通过 processMaterial 输出到 shadowMaskRT
            Blitter.BlitCameraTexture(cmd, tempSource, shadowMaskRT, processMaterial, 0);

            // 暴露给后续后处理 shader
            cmd.SetGlobalTexture(outputTextureId, shadowMaskRT);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public void Dispose()
        {
            tempSource?.Release();
            shadowMaskRT?.Release();
        }
    }

    public Settings settings = new Settings();

    private ShadowMaskPass pass;

    public override void Create()
    {
        pass = new ShadowMaskPass(settings.processMaterial, settings.passEvent, settings.outputTextureName);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (settings.processMaterial == null) return;
        renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing)
    {
        pass?.Dispose();
    }
}
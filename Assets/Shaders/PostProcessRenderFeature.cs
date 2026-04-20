using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class PostProcessRenderFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class MaterialEntry
    {
        public Material material;
        public bool available = true;
    }

    [System.Serializable]
    public class Settings
    {
        public MaterialEntry[] materials;
        public RenderPassEvent passEvent = RenderPassEvent.AfterRenderingPostProcessing;
    }

    class MultiMaterialPass : ScriptableRenderPass
    {
        private MaterialEntry[] materials;
        private RTHandle tempRT_A;
        private RTHandle tempRT_B;

        public MultiMaterialPass(MaterialEntry[] materials, RenderPassEvent passEvent)
        {
            this.materials = materials;
            this.renderPassEvent = passEvent;
        }

        public void SetMaterials(MaterialEntry[] materials)
        {
            this.materials = materials;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            if (materials == null || materials.Length == 0) return;

            RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;

            RenderingUtils.ReAllocateIfNeeded(
                ref tempRT_A,
                desc,
                FilterMode.Bilinear,
                TextureWrapMode.Clamp,
                name: "_PostTempA"
            );

            RenderingUtils.ReAllocateIfNeeded(
                ref tempRT_B,
                desc,
                FilterMode.Bilinear,
                TextureWrapMode.Clamp,
                name: "_PostTempB"
            );
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (materials == null || materials.Length == 0) return;
            if (renderingData.cameraData.isPreviewCamera) return;

            RTHandle cameraColorTarget = renderingData.cameraData.renderer.cameraColorTargetHandle;
            CommandBuffer cmd = CommandBufferPool.Get("Multi Material Multi Pass");

            // 先把相机颜色拷到 A，作为初始输入
            Blitter.BlitCameraTexture(cmd, cameraColorTarget, tempRT_A);

            RTHandle source = tempRT_A;
            RTHandle destination = tempRT_B;

            for (int i = 0; i < materials.Length; i++)
            {
                MaterialEntry entry = materials[i];
                if (entry == null || !entry.available || entry.material == null)
                    continue;

                Material mat = entry.material;
                int passCount = mat.passCount;

                for (int passIndex = 0; passIndex < passCount; passIndex++)
                {
                    Blitter.BlitCameraTexture(cmd, source, destination, mat, passIndex);

                    // 交换 source / destination
                    RTHandle temp = source;
                    source = destination;
                    destination = temp;
                }
            }

            // 最终结果写回相机目标
            Blitter.BlitCameraTexture(cmd, source, cameraColorTarget);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public void Dispose()
        {
            tempRT_A?.Release();
            tempRT_B?.Release();
        }
    }

    public Settings settings = new Settings();
    private MultiMaterialPass pass;

    public override void Create()
    {
        pass = new MultiMaterialPass(settings.materials, settings.passEvent);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (settings.materials == null || settings.materials.Length == 0) return;

        pass.SetMaterials(settings.materials);
        renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing)
    {
        pass?.Dispose();
    }
}
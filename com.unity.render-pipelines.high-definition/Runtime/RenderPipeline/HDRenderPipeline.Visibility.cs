using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.HighDefinition
{
    public partial class HDRenderPipeline
    {
        HDBRGCallbacks m_HDBRGCallbacks;
        internal Material m_VisibilityMaterial;

        struct VBufferOutput
        {
            public TextureHandle vbuffer;
        }

        internal class HDBRGCallbacks : IBRGCallbacks
        {
            private HDRenderPipeline m_HDRenderPipeline;

            public HDBRGCallbacks(HDRenderPipeline pipeline)
            {
                m_HDRenderPipeline = pipeline;
                m_HDRenderPipeline.InitializeVisibilityPass();
            }

            public BRGInternalSRPConfig GetSRPConfig() => m_HDRenderPipeline.GetBRGSRPConfig();

            public void Dispose() => m_HDRenderPipeline.ShutdownVisibilityPass();
        }

        internal void InitializeVisibilityPass()
        {
            m_VisibilityMaterial = CoreUtils.CreateEngineMaterial(defaultResources.shaders.visibilityPS);
        }

        internal void ShutdownVisibilityPass()
        {
            CoreUtils.Destroy(m_VisibilityMaterial);
            m_VisibilityMaterial = null;
        }

        internal bool IsVisibilityPassEnabled()
        {
            return m_VisibilityMaterial != null;
        }

        internal BRGInternalSRPConfig GetBRGSRPConfig()
        {
            return new BRGInternalSRPConfig()
            {
                overrideMaterial = m_VisibilityMaterial
            };
        }

        class VBufferPassData
        {
            public FrameSettings frameSettings;
            public RendererListHandle rendererList;
        }

        void RenderVBuffer(RenderGraph renderGraph, HDCamera hdCamera, CullingResults cull, ref PrepassOutput output)
        {
            output.vbuffer = new VBufferOutput();

            if (!IsVisibilityPassEnabled())
            {
                return;
            }

            using (var builder = renderGraph.AddRenderPass<VBufferPassData>("VBuffer", out var passData, ProfilingSampler.Get(HDProfileId.VBuffer)))
            {
                builder.AllowRendererListCulling(false);

                FrameSettings frameSettings = hdCamera.frameSettings;

                passData.frameSettings = frameSettings;

                output.depthBuffer = builder.UseDepthBuffer(output.depthBuffer, DepthAccess.ReadWrite);
                output.vbuffer.vbuffer = builder.UseColorBuffer(renderGraph.CreateTexture(
                    new TextureDesc(Vector2.one, true, true)
                    {
                        colorFormat = GraphicsFormat.R8G8B8A8_UNorm,
                        clearBuffer = true,//TODO: for now clear
                        clearColor = Color.clear,
                        name = "VisibilityBuffer"
                    }), 0);

                passData.rendererList = builder.UseRendererList(
                   renderGraph.CreateRendererList(CreateOpaqueRendererListDesc(
                        cull, hdCamera.camera,
                        HDShaderPassNames.s_VBufferName, m_CurrentRendererConfigurationBakedLighting, null, null, m_VisibilityMaterial, excludeObjectMotionVectors: false)));

                builder.SetRenderFunc(
                    (VBufferPassData data, RenderGraphContext context) =>
                    {
                        DrawOpaqueRendererList(context, data.frameSettings, data.rendererList);
                    });
            }

            PushFullScreenDebugTexture(renderGraph, output.vbuffer.vbuffer, FullScreenDebugMode.VisibilityBuffer);
        }
    }
}


namespace UnityEngine.Rendering.HighDefinition
{
    public partial class HDRenderPipeline
    {
        HDBRGCallbacks m_HDBRGCallbacks;
        internal Material m_VisibilityMaterial;

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
    }
}

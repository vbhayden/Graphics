
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
                m_HDRenderPipeline.m_VisibilityMaterial = CoreUtils.CreateEngineMaterial(m_HDRenderPipeline.defaultResources.shaders.finalPassPS);
            }

            public BRGInternalSRPConfig GetSRPConfig()
            {
                return new BRGInternalSRPConfig()
                {
                    overrideMaterial = m_HDRenderPipeline.m_VisibilityMaterial
                };
            }

            public void Dispose()
            {
                CoreUtils.Destroy(m_HDRenderPipeline.m_VisibilityMaterial);
            }
        }

    }
}

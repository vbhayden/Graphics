using UnityEditor.GraphToolsFoundation.Overdrive;
using UnityEditor.GraphToolsFoundation.Overdrive.BasicModel;
using UnityEditor.ShaderGraph.GraphUI.DataModel;
using UnityEditor.ShaderGraph.Registry;
using UnityEngine;

namespace UnityEditor.ShaderGraph.GraphUI
{
    /// <summary>
    /// PreviewNodeModel is backed by a registry key, but not graph data. It's only used for previews, and shouldn't
    /// exist on the graph.
    /// </summary>
    public class SearcherPreviewNodeModel : NodeModel
    {
        [SerializeField]
        RegistryKey m_RegistryKey;

        public RegistryKey registryKey
        {
            get => m_RegistryKey;
            set => m_RegistryKey = value;
        }

        protected override void OnDefineNode()
        {
            base.OnDefineNode();

            var stencil = (ShaderGraphStencil) GraphModel.Stencil;
            var registry = stencil.GetRegistry();
            var reader = registry.GetDefaultTopology(registryKey);

            if (reader == null) return;
            foreach (var portReader in reader.GetPorts())
            {
                AddPortFromReader(portReader);
            }
        }

        void AddPortFromReader(GraphDelta.IPortReader portReader)
        {
            var isInput = portReader.GetFlags().isInput;
            var orientation = portReader.GetFlags().isHorizontal
                ? PortOrientation.Horizontal
                : PortOrientation.Vertical;

            var type = ShaderGraphTypes.GetTypeHandleFromKey(portReader.GetRegistryKey());

            if (isInput)
                this.AddDataInputPort(portReader.GetName(), type, orientation: orientation);
            else
                this.AddDataOutputPort(portReader.GetName(), type, orientation: orientation);
        }
    }
}

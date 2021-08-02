﻿using UnityEditor.GraphToolsFoundation.Overdrive;
using UnityEditor.ShaderGraph.GraphUI.DataModel;
using UnityEngine.UIElements;

namespace UnityEditor.ShaderGraph.GraphUI.GraphElements
{
    public class RegistryKeyPart : BaseModelUIPart
    {
        public RegistryKeyPart(string name, IGraphElementModel model, IModelUI ownerElement, string parentClassName) :
            base(name, model, ownerElement, parentClassName)
        {
        }

        protected override void BuildPartUI(VisualElement parent)
        {
            m_Root = new Label();
            parent.Add(m_Root);
        }

        protected override void UpdatePartFromModel()
        {
            if (m_Model is not GraphDataNodeModel graphDataNode) return;
            m_Root.text = $"Registry Key: {graphDataNode.registryKey}";
        }

        Label m_Root;
        public override VisualElement Root => m_Root;
    }
}

using UnityEngine;
using System.Collections.Generic;

namespace UnityEditor.ShaderGraph
{
    class ShaderGraphMetadata : ScriptableObject
    {
        public string outputNodeTypeName;
        public List<Object> assetDependencies;
    }
}

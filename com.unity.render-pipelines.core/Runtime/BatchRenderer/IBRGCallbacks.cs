using System;
using System.Collections.Generic;
using System.Reflection;

namespace UnityEngine.Rendering
{
    public struct BRGInternalSRPConfig
    {
        public Material overrideMaterial;
    }

    public interface IBRGCallbacks
    {
        public BRGInternalSRPConfig GetSRPConfig();
    }
}

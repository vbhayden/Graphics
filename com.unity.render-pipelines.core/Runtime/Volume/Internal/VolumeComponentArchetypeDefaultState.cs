using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace UnityEngine.Rendering
{
    /// <summary>
    /// Manages the default data set of the volume components
    /// </summary>
    class VolumeComponentArchetypeDefaultState : VolumeComponentArchetypeExtension
    {
        public struct Factory : IVolumeComponentArchetypeExtensionFactory<VolumeComponentArchetypeDefaultState>
        {
            [return: NotNull]
            public VolumeComponentArchetypeDefaultState Create([DisallowNull] VolumeComponentArchetype volumeComponentArchetype)
            {
                var componentsDefaultState = volumeComponentArchetype.AsArray()
                    .Select(type => (VolumeComponent)ScriptableObject.CreateInstance(type)).ToArray();

                return new VolumeComponentArchetypeDefaultState(componentsDefaultState);
            }
        }
        VolumeComponent[] componentsDefaultState { get; }

        VolumeComponentArchetypeDefaultState(VolumeComponent[] componentsDefaultState)
        {
            this.componentsDefaultState = componentsDefaultState;
        }

        // Faster version of OverrideData to force replace values in the global state
        public void ReplaceData([DisallowNull] VolumeStack stack)
        {
            foreach (var component in componentsDefaultState)
            {
                var target = stack.GetComponent(component.GetType());
                var count = component.parameters.Count;

                for (var i = 0; i < count; i++)
                {
                    if (target.parameters[i] != null)
                    {
                        target.parameters[i].overrideState = false;
                        target.parameters[i].SetValue(component.parameters[i]);
                    }
                }
            }
        }
    }

    static class VolumeComponentTypeSetDefaultStateExtension
    {
        public static bool GetOrAddDefaultState(
            [DisallowNull] this VolumeComponentArchetype archetype,
            [NotNullWhen(true)] out VolumeComponentArchetypeDefaultState extension)
            => archetype.GetOrAddExtension<VolumeComponentArchetypeDefaultState, VolumeComponentArchetypeDefaultState.Factory>(out extension);
    }
}

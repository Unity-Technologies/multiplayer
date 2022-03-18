using UnityEngine;
using Unity.Entities;
using Unity.Scenes;

#if UNITY_EDITOR
namespace Unity.NetCode.Samples
{
    [ExecuteAlways]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [AlwaysSynchronizeSystem]
    public partial class ConfigureEditorSystem : SystemBase
    {
        protected override void OnCreate()
        {
            if (UnityEditor.EditorApplication.isPlaying)
                return;
            // Editor World
            World.GetOrCreateSystem<SceneSystem>().BuildConfigurationGUID = ConfigureClientSystems.ClientBuildSettingsGUID;
        }

        protected override void OnUpdate()
        {
        }
    }
}
#endif

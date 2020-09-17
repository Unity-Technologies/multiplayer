using UnityEngine;
using Unity.Entities;
using Unity.Jobs;
using Unity.Scenes;

#if UNITY_EDITOR
[ExecuteAlways]
[UpdateInGroup(typeof(InitializationSystemGroup))]
[AlwaysSynchronizeSystem]
public class ConfigureEditorSystem : SystemBase
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
#endif

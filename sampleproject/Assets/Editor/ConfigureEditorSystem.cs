using Authoring.Hybrid;
using UnityEngine;
using Unity.Entities;
using Unity.Entities.Build;
using Unity.Entities.Conversion;
using Unity.Scenes;

#if UNITY_EDITOR
namespace Unity.NetCode.Samples
{
    [WorldSystemFilter(WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [AlwaysSynchronizeSystem]
    public partial class ConfigureEditorSystem : SystemBase
    {
        protected override void OnCreate()
        {
            if (UnityEditor.EditorApplication.isPlaying)
                return;
            // Editor World
            ref var sceneSystemGuid = ref EntityManager.GetComponentDataRW<SceneSystemData>(World.GetExistingSystem<SceneSystem>()).ValueRW;
            sceneSystemGuid.BuildConfigurationGUID = ConfigureClientSystems.ClientBuildSettingsGUID;
            if (LiveConversionSettings.IsBuiltinBuildsEnabled)
            {
                var clientGuid = ((Authoring.Hybrid.ClientSettings)DotsGlobalSettings.Instance.ClientProvider).GetSettingGUID(NetCodeClientTarget.Client);
                sceneSystemGuid.BuildConfigurationGUID = clientGuid;
            }
        }

        protected override void OnUpdate()
        {
        }
    }
}
#endif

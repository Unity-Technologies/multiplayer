using Unity.Entities;
#if UNITY_EDITOR
using Unity.Entities.Conversion;
using Unity.Entities.Build;
using Authoring.Hybrid;
#endif
using Unity.Scenes;
using Hash128 = Unity.Entities.Hash128;

namespace Unity.NetCode.Samples
{
#if UNITY_CLIENT || UNITY_EDITOR
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation|WorldSystemFilterFlags.ThinClientSimulation)]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial class ConfigureClientSystems : SystemBase
    {
        public static Hash128 ClientBuildSettingsGUID => new Hash128("450f20c3e50de4631a862cc7b7da1ae6");

        protected override void OnCreate()
        {
            ref var sceneSystemGuid = ref EntityManager.GetComponentDataRW<SceneSystemData>(World.GetExistingSystem<SceneSystem>()).ValueRW;
            sceneSystemGuid.BuildConfigurationGUID = ClientBuildSettingsGUID;
#if UNITY_EDITOR
            if (LiveConversionSettings.IsBuiltinBuildsEnabled)
            {
                var clientGuid = ((Authoring.Hybrid.ClientSettings)DotsGlobalSettings.Instance.ClientProvider).GetSettingGUID(NetCodeClientTarget.Client);
                sceneSystemGuid.BuildConfigurationGUID = clientGuid;
            }
#endif
        }

        protected override void OnUpdate()
        {
        }
    }
#endif

#if UNITY_SERVER || UNITY_EDITOR
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [CreateAfter(typeof(SceneSystem))]
    public partial class ConfigureServerSystems : SystemBase
    {
#if !UNITY_DOTSRUNTIME
        public static Hash128 ServerBuildSettingsGUID => new Hash128("687ff696e4df441e5931eb81db4d1db0");
#else
        public static Hash128 ServerBuildSettingsGUID => new Hash128("9b3a397188c16436d8802a1edfb882cc");
#endif

        protected override void OnCreate()
        {
            ref var sceneSystemGuid = ref EntityManager.GetComponentDataRW<SceneSystemData>(World.GetExistingSystem<SceneSystem>()).ValueRW;
            sceneSystemGuid.BuildConfigurationGUID = ServerBuildSettingsGUID;
#if UNITY_EDITOR && !UNITY_DOTSRUNTIME
            if (LiveConversionSettings.IsBuiltinBuildsEnabled)
            {
                var instance = DotsGlobalSettings.Instance;
                if (MultiplayerPlayModePreferences.ServerLoadDataType == MultiplayerPlayModePreferences.ServerWorldDataToLoad.Server)
                    sceneSystemGuid.BuildConfigurationGUID = instance.GetServerGUID();
                else if (MultiplayerPlayModePreferences.ServerLoadDataType ==
                         MultiplayerPlayModePreferences.ServerWorldDataToLoad.ClientAndServer)
                {
                    var clientAndServerGuid = ((ClientSettings)instance.ClientProvider).GetSettingGUID(NetCodeClientTarget.ClientAndServer);
                    sceneSystemGuid.BuildConfigurationGUID = clientAndServerGuid;
                }
            }
#endif
        }

        protected override void OnUpdate()
        {
        }
    }
#endif
}

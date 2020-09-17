using Unity.Entities;
using Unity.NetCode;
using Unity.Scenes;
using Hash128 = Unity.Entities.Hash128;

#if UNITY_CLIENT || UNITY_EDITOR
[UpdateInGroup(typeof(ClientInitializationSystemGroup))]
public class ConfigureClientSystems : SystemBase
{
    public static Hash128 ClientBuildSettingsGUID => new Hash128("450f20c3e50de4631a862cc7b7da1ae6");

    protected override void OnCreate()
    {
        World.GetOrCreateSystem<SceneSystem>().BuildConfigurationGUID = ClientBuildSettingsGUID;
    }

    protected override void OnUpdate()
    {
    }
}
#endif

#if UNITY_SERVER || UNITY_EDITOR
[UpdateInGroup(typeof(ServerInitializationSystemGroup))]
public class ConfigureServerSystems : SystemBase
{
    public static Hash128 ServerBuildSettingsGUID => new Hash128("69018010a3ed1485a922ec6aeaa79daf");

    protected override void OnCreate()
    {
        World.GetOrCreateSystem<SceneSystem>().BuildConfigurationGUID = ServerBuildSettingsGUID;
    }

    protected override void OnUpdate()
    {
    }
}
#endif

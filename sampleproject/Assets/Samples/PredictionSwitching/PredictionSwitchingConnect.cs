using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;

[UpdateInGroup(typeof(ClientInitializationSystemGroup))]
public partial class PredictionSwitchingConnectClientSystem : SystemBase
{
    protected override void OnCreate()
    {
        RequireSingletonForUpdate<PredictionSwitchingSpawner>();
    }
    protected override void OnUpdate()
    {
        Entities
            .WithStructuralChanges()
            .WithoutBurst()
            .WithNone<NetworkStreamInGame>()
            .WithAll<NetworkIdComponent>()
            .ForEach((Entity entity) =>
        {
            EntityManager.AddComponentData(entity, default(NetworkStreamInGame));
        }).Run();
    }
}

[UpdateInGroup(typeof(ServerInitializationSystemGroup))]
public partial class PredictionSwitchingConnectServerSystem : SystemBase
{
    protected override void OnCreate()
    {
        RequireSingletonForUpdate<PredictionSwitchingSpawner>();
    }
    protected override void OnUpdate()
    {
        var spawner = GetSingleton<PredictionSwitchingSpawner>();
        Entities
            .WithStructuralChanges()
            .WithoutBurst()
            .WithNone<NetworkStreamInGame>()
            .ForEach((Entity entity, ref CommandTargetComponent target, in NetworkIdComponent netId) =>
        {
            target.targetEntity = EntityManager.Instantiate(spawner.Player);
            EntityManager.SetComponentData(target.targetEntity, new GhostOwnerComponent{NetworkId = netId.Value});
            EntityManager.AddComponentData(entity, default(NetworkStreamInGame));
            // Add the player to the linked entity group so it is destroyed automatically on disconnect
            EntityManager.GetBuffer<LinkedEntityGroup>(entity).Add(new LinkedEntityGroup{Value = target.targetEntity});
        }).Run();
    }
}

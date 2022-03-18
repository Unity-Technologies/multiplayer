using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;
using Unity.Collections;

[UpdateInGroup(typeof(ClientSimulationSystemGroup))]
[AlwaysSynchronizeSystem]
public partial class GoInGameLagClientSystem : SystemBase
{
    protected override void OnCreate()
    {
        RequireSingletonForUpdate<LagCompensationSpawner>();
        RequireForUpdate(GetEntityQuery(ComponentType.ReadOnly<NetworkIdComponent>(), ComponentType.Exclude<NetworkStreamInGame>()));
    }

    protected override void OnUpdate()
    {
        var commandBuffer = new EntityCommandBuffer(Allocator.Temp);
        Entities.WithNone<NetworkStreamInGame>().ForEach((Entity ent, in NetworkIdComponent id) =>
        {
            commandBuffer.AddComponent<NetworkStreamInGame>(ent);
        }).Run();
        commandBuffer.Playback(EntityManager);
    }
}
[UpdateInGroup(typeof(ServerSimulationSystemGroup))]
[AlwaysSynchronizeSystem]
public partial class GoInGameLagServerSystem : SystemBase
{
    BeginSimulationEntityCommandBufferSystem m_Barrier;
    protected override void OnCreate()
    {
        RequireSingletonForUpdate<LagCompensationSpawner>();
        RequireForUpdate(GetEntityQuery(ComponentType.ReadOnly<NetworkIdComponent>(), ComponentType.Exclude<NetworkStreamInGame>()));
        m_Barrier = World.GetExistingSystem<BeginSimulationEntityCommandBufferSystem>();
    }

    protected override void OnUpdate()
    {
        var commandBuffer = m_Barrier.CreateCommandBuffer();
        var playerPrefab = GetSingleton<LagCompensationSpawner>().prefab;
        Entities.WithNone<NetworkStreamInGame>().ForEach((Entity ent, in NetworkIdComponent id) =>
        {
            commandBuffer.AddComponent<NetworkStreamInGame>(ent);
            var player = commandBuffer.Instantiate(playerPrefab);
            commandBuffer.SetComponent(player, new GhostOwnerComponent{NetworkId = id.Value});
            // Add the player to the linked entity group so it is destroyed automatically on disconnect
            commandBuffer.AppendToBuffer(ent, new LinkedEntityGroup{Value = player});
        }).Run();
    }
}

public struct LagPlayer : IComponentData
{
}

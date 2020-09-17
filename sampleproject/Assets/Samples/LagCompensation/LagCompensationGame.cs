using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;
using Unity.Collections;

public struct EnableLagCompensationGame : IComponentData
{}

// Control system updating in the default world
[UpdateInWorld(UpdateInWorld.TargetWorld.Default)]
[AlwaysSynchronizeSystem]
public class LagCompensationGame : SystemBase
{
    // Singleton component to trigger connections once from a control system
    struct InitGameComponent : IComponentData
    {
    }
    protected override void OnCreate()
    {
        RequireSingletonForUpdate<InitGameComponent>();
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "LagCompensation")
            return;
        // Create singleton, require singleton for update so system runs once
        EntityManager.CreateEntity(typeof(InitGameComponent));
    }

    protected override void OnUpdate()
    {
        // Destroy singleton to prevent system from running again
        EntityManager.DestroyEntity(GetSingletonEntity<InitGameComponent>());
        foreach (var world in World.All)
        {
            var network = world.GetExistingSystem<NetworkStreamReceiveSystem>();
            if (world.GetExistingSystem<ClientSimulationSystemGroup>() != null)
            {
                world.EntityManager.CreateEntity(typeof(EnableLagCompensationGame));
                // Client worlds automatically connect to localhost
                NetworkEndPoint ep = NetworkEndPoint.LoopbackIpv4;
                ep.Port = 7979;
                network.Connect(ep);
            }
            #if UNITY_EDITOR
            else if (world.GetExistingSystem<ServerSimulationSystemGroup>() != null)
            {
                world.EntityManager.CreateEntity(typeof(EnableLagCompensationGame));
                var tickRate = world.EntityManager.CreateEntity();
                world.EntityManager.AddComponentData(tickRate, new ClientServerTickRate
                {
                    SimulationTickRate = 60
                });
                // Server world automatically listen for connections from any host
                NetworkEndPoint ep = NetworkEndPoint.AnyIpv4;
                ep.Port = 7979;
                network.Listen(ep);
            }
            #endif
        }
    }
}

[UpdateInGroup(typeof(ClientSimulationSystemGroup))]
[AlwaysSynchronizeSystem]
public class GoInGameLagClientSystem : SystemBase
{
    protected override void OnCreate()
    {
        RequireSingletonForUpdate<EnableLagCompensationGame>();
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
public class GoInGameLagServerSystem : SystemBase
{
    protected override void OnCreate()
    {
        RequireSingletonForUpdate<EnableLagCompensationGame>();
        RequireForUpdate(GetEntityQuery(ComponentType.ReadOnly<NetworkIdComponent>(), ComponentType.Exclude<NetworkStreamInGame>()));
    }

    protected override void OnUpdate()
    {
        var commandBuffer = new EntityCommandBuffer(Allocator.Temp);
        var ghostId = -1;
        var prefabs = EntityManager.GetBuffer<GhostPrefabBuffer>(GetSingletonEntity<GhostPrefabCollectionComponent>());
        for (int i = 0; i < prefabs.Length; ++i)
        {
            if (EntityManager.HasComponent<LagPlayer>(prefabs[i].Value))
                ghostId = i;
        }
        var playerPrefab = prefabs[ghostId].Value;
        Entities.WithNone<NetworkStreamInGame>().ForEach((Entity ent, in NetworkIdComponent id) =>
        {
            commandBuffer.AddComponent<NetworkStreamInGame>(ent);
            var player = commandBuffer.Instantiate(playerPrefab);
            commandBuffer.SetComponent(player, new GhostOwnerComponent{NetworkId = id.Value});
            commandBuffer.SetComponent(ent, new CommandTargetComponent{targetEntity = player});
        }).Run();
        commandBuffer.Playback(EntityManager);
    }
}

public struct LagPlayer : IComponentData
{
}

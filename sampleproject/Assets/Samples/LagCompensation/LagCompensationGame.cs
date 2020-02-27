using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;

// Control system updating in the default world
[UpdateInWorld(UpdateInWorld.TargetWorld.Default)]
public class LagCompensationGame : ComponentSystem
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
        foreach (var world in World.AllWorlds)
        {
            var network = world.GetExistingSystem<NetworkStreamReceiveSystem>();
            if (world.GetExistingSystem<ClientSimulationSystemGroup>() != null)
            {
                // Client worlds automatically connect to localhost
                NetworkEndPoint ep = NetworkEndPoint.LoopbackIpv4;
                ep.Port = 7979;
                network.Connect(ep);
            }
            #if UNITY_EDITOR
            else if (world.GetExistingSystem<ServerSimulationSystemGroup>() != null)
            {
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
public class GoInGameLagClientSystem : ComponentSystem
{
    protected override void OnCreate()
    {
        RequireSingletonForUpdate<EnableLagCompensationGhostReceiveSystemComponent>();
    }

    protected override void OnUpdate()
    {
        Entities.WithNone<NetworkStreamInGame>().ForEach((Entity ent, ref NetworkIdComponent id) =>
        {
            PostUpdateCommands.AddComponent<NetworkStreamInGame>(ent);
        });
    }
}
[UpdateInGroup(typeof(ServerSimulationSystemGroup))]
public class GoInGameLagServerSystem : ComponentSystem
{
    protected override void OnCreate()
    {
        RequireSingletonForUpdate<EnableLagCompensationGhostSendSystemComponent>();
    }

    protected override void OnUpdate()
    {
        Entities.WithNone<NetworkStreamInGame>().ForEach((Entity ent, ref NetworkIdComponent id) =>
        {
            PostUpdateCommands.AddComponent<NetworkStreamInGame>(ent);
            var ghostId = LagCompensationGhostSerializerCollection.FindGhostType<LagPlayerSnapshotData>();
            var playerPrefab = EntityManager.GetBuffer<GhostPrefabBuffer>(GetSingleton<GhostPrefabCollectionComponent>().serverPrefabs)[ghostId].Value;
            var player = PostUpdateCommands.Instantiate(playerPrefab);
            PostUpdateCommands.SetComponent(player, new LagPlayer{playerId = id.Value});
            PostUpdateCommands.SetComponent(ent, new CommandTargetComponent{targetEntity = player});
        });
    }
}

public struct LagPlayer : IComponentData
{
    [GhostDefaultField]
    public int playerId;
}

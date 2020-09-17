using System;
using AOT;
using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;
using Unity.Burst;
using Unity.Collections;

public struct EnableNetCubeGame : IComponentData
{}

// Control system updating in the default world
[UpdateInWorld(UpdateInWorld.TargetWorld.Default)]
[AlwaysSynchronizeSystem]
public class Game : SystemBase
{
    // Singleton component to trigger connections once from a control system
    struct InitGameComponent : IComponentData
    {
    }
    protected override void OnCreate()
    {
        RequireSingletonForUpdate<InitGameComponent>();
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "NetCube")
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
                world.EntityManager.CreateEntity(typeof(EnableNetCubeGame));
                // Client worlds automatically connect to localhost
                NetworkEndPoint ep = NetworkEndPoint.LoopbackIpv4;
                ep.Port = 7979;
#if UNITY_EDITOR
                ep = NetworkEndPoint.Parse(ClientServerBootstrap.RequestedAutoConnect, 7979);
#endif
                network.Connect(ep);
            }
            #if UNITY_EDITOR || UNITY_SERVER
            else if (world.GetExistingSystem<ServerSimulationSystemGroup>() != null)
            {
                world.EntityManager.CreateEntity(typeof(EnableNetCubeGame));
                // Server world automatically listen for connections from any host
                NetworkEndPoint ep = NetworkEndPoint.AnyIpv4;
                ep.Port = 7979;
                network.Listen(ep);
            }
            #endif
        }
    }
}

// RPC request from client to server for game to go "in game" and send snapshots / inputs
public struct GoInGameRequest : IRpcCommand
{
}

// When client has a connection with network id, go in game and tell server to also go in game
[UpdateInGroup(typeof(ClientSimulationSystemGroup))]
[AlwaysSynchronizeSystem]
public class GoInGameClientSystem : SystemBase
{
    protected override void OnCreate()
    {
        RequireSingletonForUpdate<EnableNetCubeGame>();
        RequireForUpdate(GetEntityQuery(ComponentType.ReadOnly<NetworkIdComponent>(), ComponentType.Exclude<NetworkStreamInGame>()));
    }

    protected override void OnUpdate()
    {
        var commandBuffer = new EntityCommandBuffer(Allocator.Temp);
        Entities.WithNone<NetworkStreamInGame>().ForEach((Entity ent, in NetworkIdComponent id) =>
        {
            commandBuffer.AddComponent<NetworkStreamInGame>(ent);
            var req = commandBuffer.CreateEntity();
            commandBuffer.AddComponent<GoInGameRequest>(req);
            commandBuffer.AddComponent(req, new SendRpcCommandRequestComponent { TargetConnection = ent });
        }).Run();
        commandBuffer.Playback(EntityManager);
    }
}

// When server receives go in game request, go in game and delete request
[UpdateInGroup(typeof(ServerSimulationSystemGroup))]
[AlwaysSynchronizeSystem]
public class GoInGameServerSystem : SystemBase
{
    protected override void OnCreate()
    {
        RequireSingletonForUpdate<EnableNetCubeGame>();
        RequireForUpdate(GetEntityQuery(ComponentType.ReadOnly<GoInGameRequest>(), ComponentType.ReadOnly<ReceiveRpcCommandRequestComponent>()));
    }

    protected override void OnUpdate()
    {
        var ghostCollection = GetSingletonEntity<GhostPrefabCollectionComponent>();
        var prefab = Entity.Null;
        var prefabs = EntityManager.GetBuffer<GhostPrefabBuffer>(ghostCollection);
        for (int ghostId = 0; ghostId < prefabs.Length; ++ghostId)
        {
            if (EntityManager.HasComponent<MovableCubeComponent>(prefabs[ghostId].Value))
                prefab = prefabs[ghostId].Value;
        }

        var commandBuffer = new EntityCommandBuffer(Allocator.Temp);
        var networkIdFromEntity = GetComponentDataFromEntity<NetworkIdComponent>(true);
        Entities.WithReadOnly(networkIdFromEntity).ForEach((Entity reqEnt, in GoInGameRequest req, in ReceiveRpcCommandRequestComponent reqSrc) =>
        {
            commandBuffer.AddComponent<NetworkStreamInGame>(reqSrc.SourceConnection);
            UnityEngine.Debug.Log(String.Format("Server setting connection {0} to in game", networkIdFromEntity[reqSrc.SourceConnection].Value));

            var player = commandBuffer.Instantiate(prefab);
            commandBuffer.SetComponent(player, new GhostOwnerComponent { NetworkId = networkIdFromEntity[reqSrc.SourceConnection].Value});

            commandBuffer.AddBuffer<CubeInput>(player);
            commandBuffer.SetComponent(reqSrc.SourceConnection, new CommandTargetComponent {targetEntity = player});

            commandBuffer.DestroyEntity(reqEnt);
        }).Run();
        commandBuffer.Playback(EntityManager);
    }
}

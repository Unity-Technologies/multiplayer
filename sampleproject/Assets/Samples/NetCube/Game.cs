using System;
using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

// RPC request from client to server for game to go "in game" and send snapshots / inputs
public struct GoInGameRequest : IRpcCommand
{
}

// When client has a connection with network id, go in game and tell server to also go in game
[UpdateInGroup(typeof(ClientSimulationSystemGroup))]
public partial class GoInGameClientSystem : SystemBase
{
    protected override void OnCreate()
    {
        RequireSingletonForUpdate<NetCubeSpawner>();
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
public partial class GoInGameServerSystem : SystemBase
{
    protected override void OnCreate()
    {
        RequireSingletonForUpdate<NetCubeSpawner>();
        RequireForUpdate(GetEntityQuery(ComponentType.ReadOnly<GoInGameRequest>(), ComponentType.ReadOnly<ReceiveRpcCommandRequestComponent>()));
    }

    protected override void OnUpdate()
    {
        var prefab = GetSingleton<NetCubeSpawner>().Cube;
        var prefabName = new FixedString32Bytes(EntityManager.GetName(prefab));
        var worldName = new FixedString32Bytes(World.Name);

        var commandBuffer = new EntityCommandBuffer(Allocator.Temp);
        var networkIdFromEntity = GetComponentDataFromEntity<NetworkIdComponent>(true);
        Entities.WithReadOnly(networkIdFromEntity).ForEach((Entity reqEnt, in GoInGameRequest req, in ReceiveRpcCommandRequestComponent reqSrc) =>
        {
            commandBuffer.AddComponent<NetworkStreamInGame>(reqSrc.SourceConnection);
            var networkIdComponent = networkIdFromEntity[reqSrc.SourceConnection];

            Debug.Log($"'{worldName}' setting connection '{networkIdComponent.Value}' to in game, spawning a Ghost '{prefabName}' for them!");

            var player = commandBuffer.Instantiate(prefab);
            commandBuffer.SetComponent(player, new GhostOwnerComponent { NetworkId = networkIdComponent.Value});

            // Add the player to the linked entity group so it is destroyed automatically on disconnect
            commandBuffer.AppendToBuffer(reqSrc.SourceConnection, new LinkedEntityGroup{Value = player});

            // Give each NetworkId their own spawn pos:
            {
                var isEven = (networkIdComponent.Value & 1) == 0;
                var staggeredXPos = networkIdComponent.Value * math.@select(.55f, -.55f, isEven) + math.@select(-0.25f, 0.25f, isEven);
                var preventZFighting = -0.01f * networkIdComponent.Value;
                commandBuffer.SetComponent(player, new Translation { Value = new float3(staggeredXPos, preventZFighting, 0) });
            }

            commandBuffer.DestroyEntity(reqEnt);
        }).Run();
        commandBuffer.Playback(EntityManager);
    }
}

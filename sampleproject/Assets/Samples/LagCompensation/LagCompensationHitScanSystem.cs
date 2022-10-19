using Unity.Entities;
using Unity.NetCode;
using Unity.Jobs;
using Unity.Collections;
using Unity.Physics;

public struct LagCompensationEnabled : IComponentData
{}

[UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
public partial class LagCompensationHitScanSystem : SystemBase
{
    private EndSimulationEntityCommandBufferSystem m_ecbSystem;
    private bool m_IsServer;
    private EntityQuery m_ConnectionQuery;
    protected override void OnCreate()
    {
        m_ecbSystem = World.GetExistingSystemManaged<EndSimulationEntityCommandBufferSystem>();
        m_IsServer = World.IsServer();
        m_ConnectionQuery = GetEntityQuery(typeof(NetworkIdComponent));
        RequireForUpdate<LagCompensationSpawner>();
        RequireForUpdate(m_ConnectionQuery);
    }
    protected override void OnUpdate()
    {
        var collisionHistory = GetSingleton<PhysicsWorldHistorySingleton>();
        var physicsWorld = GetSingleton<PhysicsWorldSingleton>().PhysicsWorld;
        var networkTime = GetSingleton<NetworkTime>();
        var predictingTick = networkTime.ServerTick;
        // Do not perform hit-scan when rolling back, only when simulating the latest tick
        if (!networkTime.IsFirstTimeFullyPredictingTick)
            return;
        var isServer = m_IsServer;
        var commandBuffer = m_ecbSystem.CreateCommandBuffer();
        // Generate a list of all connections
        var connectionEntities = m_ConnectionQuery.ToEntityArray(World.UpdateAllocator.ToAllocator);
        var connections = m_ConnectionQuery.ToComponentDataArray<NetworkIdComponent>(World.UpdateAllocator.ToAllocator);
        var enableFromEntity = GetComponentLookup<LagCompensationEnabled>(true);

        // Not using burst since there is a static used to update the UI
        Dependency = Entities
            .WithReadOnly(enableFromEntity)
            .WithReadOnly(physicsWorld)
            .WithAll<Simulate>()
            .ForEach((Entity entity, DynamicBuffer<RayTraceCommand> commands, in CommandDataInterpolationDelay delay, in GhostOwnerComponent owner) =>
        {
            // If there is no data for the tick or a fire was not requested - do not process anything
            if (!commands.GetDataAtTick(predictingTick, out var cmd))
                return;
            if (cmd.lastFire != predictingTick)
                return;

            // Get the collision world to use given the tick currently being predicted and the interpolation delay for the connection
            collisionHistory.GetCollisionWorldFromTick(predictingTick, enableFromEntity.HasComponent(entity) ? delay.Delay : 0, ref physicsWorld, out var collWorld);
            var rayInput = new Unity.Physics.RaycastInput();
            rayInput.Start = cmd.origin;
            rayInput.End = cmd.origin + cmd.direction * 100;
            rayInput.Filter = Unity.Physics.CollisionFilter.Default;
            bool hit = collWorld.CastRay(rayInput);
            var ent = commandBuffer.CreateEntity();
            commandBuffer.AddComponent(ent, new LagHitStatus {Tick = predictingTick, Hit = hit, IsServer = isServer});
            if (isServer)
            {
                for (int i = 0; i < connections.Length; ++i)
                {
                    if (connections[i].Value == owner.NetworkId)
                        commandBuffer.AddComponent(ent, new SendRpcCommandRequestComponent{TargetConnection = connectionEntities[i]});
                }
            }
        }).Schedule(Dependency);

        m_ecbSystem.AddJobHandleForProducer(Dependency);
    }
}

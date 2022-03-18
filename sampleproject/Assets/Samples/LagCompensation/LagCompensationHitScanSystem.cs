using Unity.Entities;
using Unity.NetCode;
using Unity.Jobs;
using Unity.Collections;

public struct LagCompensationEnabled : IComponentData
{}

[UpdateInGroup(typeof(GhostPredictionSystemGroup))]
public partial class LagCompensationHitScanSystem : SystemBase
{
    private PhysicsWorldHistory m_physicsHistory;
    private GhostPredictionSystemGroup m_predictionGroup;
    private EndSimulationEntityCommandBufferSystem m_ecbSystem;
    private bool m_IsServer;
    private EntityQuery m_ConnectionQuery;
    protected override void OnCreate()
    {
        m_physicsHistory = World.GetExistingSystem<PhysicsWorldHistory>();
        m_predictionGroup = World.GetExistingSystem<GhostPredictionSystemGroup>();
        m_ecbSystem = World.GetExistingSystem<EndSimulationEntityCommandBufferSystem>();
        m_IsServer = World.GetExistingSystem<ServerSimulationSystemGroup>() != null;
        m_ConnectionQuery = GetEntityQuery(typeof(NetworkIdComponent));
        RequireSingletonForUpdate<LagCompensationSpawner>();
        RequireForUpdate(m_ConnectionQuery);
    }
    protected override void OnUpdate()
    {
        var collisionHistory = m_physicsHistory.CollisionHistory;
        uint predictingTick = m_predictionGroup.PredictingTick;
        // Do not perform hit-scan when rolling back, only when simulating the latest tick
        if (!m_predictionGroup.IsFinalPredictionTick)
            return;
        var isServer = m_IsServer;
        var commandBuffer = m_ecbSystem.CreateCommandBuffer();
        // Generate a list of all connections
        var connectionEntities = m_ConnectionQuery.ToEntityArray(Allocator.TempJob);
        var connections = m_ConnectionQuery.ToComponentDataArray<NetworkIdComponent>(Allocator.TempJob);
        var enableFromEntity = GetComponentDataFromEntity<LagCompensationEnabled>(true);

        // Not using burst since there is a static used to update the UI
        Dependency = Entities
            .WithDisposeOnCompletion(connectionEntities)
            .WithDisposeOnCompletion(connections)
            .WithReadOnly(enableFromEntity)
            .ForEach((Entity entity, DynamicBuffer<RayTraceCommand> commands, in CommandDataInterpolationDelay delay, in GhostOwnerComponent owner) =>
        {
            // If there is no data for the tick or a fire was not requested - do not process anything
            if (!commands.GetDataAtTick(predictingTick, out var cmd))
                return;
            if (cmd.lastFire != predictingTick)
                return;

            // Get the collision world to use given the tick currently being predicted and the interpolation delay for the connection
            collisionHistory.GetCollisionWorldFromTick(predictingTick, enableFromEntity.HasComponent(entity) ? delay.Delay : 0, out var collWorld);
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
        }).Schedule(JobHandle.CombineDependencies(Dependency, m_physicsHistory.LastPhysicsJobHandle));

        m_ecbSystem.AddJobHandleForProducer(Dependency);
        m_physicsHistory.LastPhysicsJobHandle = Dependency;
    }
}

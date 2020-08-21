using Unity.NetCode;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Transforms;
using Unity.Jobs;

namespace Asteroids.Server
{
    [UpdateInGroup(typeof(ServerSimulationSystemGroup))]
    [UpdateBefore(typeof(GhostSendSystem))]
    public class ShipRelevancySphereSystem : SystemBase
    {
        struct ConnectionRelevancy
        {
            public int ConnectionId;
            public float3 Position;
        }
        GhostSendSystem m_GhostSendSystem;
        NativeList<ConnectionRelevancy> m_Connections;
        EntityQuery m_GhostQuery;
        EntityQuery m_ConnectionQuery;
        protected override void OnCreate()
        {
            m_GhostQuery = GetEntityQuery(ComponentType.ReadOnly<GhostComponent>());
            m_ConnectionQuery = GetEntityQuery(ComponentType.ReadOnly<NetworkIdComponent>());
            RequireForUpdate(m_ConnectionQuery);
            m_Connections = new NativeList<ConnectionRelevancy>(16, Allocator.Persistent);
            m_GhostSendSystem = World.GetExistingSystem<GhostSendSystem>();
            RequireSingletonForUpdate<ServerSettings>();
        }
        protected override void OnDestroy()
        {
            m_Connections.Dispose();
        }
        protected override void OnUpdate()
        {
            var settings = GetSingleton<ServerSettings>();
            if (settings.relevancyRadius == 0)
            {
                m_GhostSendSystem.GhostRelevancyMode = GhostRelevancyMode.Disabled;
                return;
            }
            m_GhostSendSystem.GhostRelevancyMode = GhostRelevancyMode.SetIsIrrelevant;

            m_Connections.Clear();
            var relevantSet = m_GhostSendSystem.GhostRelevancySet;
            var parallelRelevantSet = relevantSet.AsParallelWriter();

            var maxRelevantSize = m_GhostQuery.CalculateEntityCount() * m_ConnectionQuery.CalculateEntityCount();

            var clearHandle = Job.WithCode(() => {
                relevantSet.Clear();
                if (relevantSet.Capacity < maxRelevantSize)
                    relevantSet.Capacity = maxRelevantSize;
            }).Schedule(m_GhostSendSystem.GhostRelevancySetWriteHandle);

            var connections = m_Connections;
            var transFromEntity = GetComponentDataFromEntity<Translation>(true);
            var connectionHandle = Entities
                .WithReadOnly(transFromEntity)
                .ForEach((in NetworkIdComponent netId, in CommandTargetComponent target) => {
                if (target.targetEntity == Entity.Null)
                    return;
                var pos = transFromEntity[target.targetEntity].Value;
                connections.Add(new ConnectionRelevancy{ConnectionId = netId.Value, Position = pos});
            }).Schedule(Dependency);

            Dependency = Entities
                .WithReadOnly(connections)
                .ForEach((in GhostComponent ghost, in Translation pos) => {
                for (int i = 0; i < connections.Length; ++i)
                {
                    if (math.distance(pos.Value, connections[i].Position) > settings.relevancyRadius)
                        parallelRelevantSet.TryAdd(new RelevantGhostForConnection(connections[i].ConnectionId, ghost.ghostId), 1);
                }
            }).ScheduleParallel(JobHandle.CombineDependencies(connectionHandle, clearHandle));
            m_GhostSendSystem.GhostRelevancySetWriteHandle = Dependency;
        }
    }
}

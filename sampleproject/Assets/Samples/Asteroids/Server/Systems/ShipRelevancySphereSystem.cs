using Unity.Burst;
using Unity.NetCode;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Transforms;
using Unity.Jobs;

namespace Asteroids.Server
{
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct ShipRelevancySphereISystem : ISystem
    {
        struct ConnectionRelevancy
        {
            public int ConnectionId;
            public float3 Position;
        }
        NativeList<ConnectionRelevancy> m_Connections;
        EntityQuery m_GhostQuery;
        EntityQuery m_ConnectionQuery;
        ComponentLookup<Translation> m_Translations;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            var builder = new EntityQueryBuilder(Allocator.Temp).WithAll<GhostComponent>();
            m_GhostQuery = state.GetEntityQuery(builder);

            builder.Reset();
            builder.WithAll<NetworkIdComponent>();
            m_ConnectionQuery = state.GetEntityQuery(builder);

            m_Translations = state.GetComponentLookup<Translation>(true);
            m_Connections = new NativeList<ConnectionRelevancy>(16, Allocator.Persistent);

            state.RequireForUpdate(m_ConnectionQuery);
            state.RequireForUpdate<ServerSettings>();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var settings = SystemAPI.GetSingleton<ServerSettings>();

            ref var ghostRelevancy = ref SystemAPI.GetSingletonRW<GhostRelevancy>().ValueRW;
            if (settings.levelData.relevancyRadius == 0)
            {
                ghostRelevancy.GhostRelevancyMode = GhostRelevancyMode.Disabled;
                return;
            }
            ghostRelevancy.GhostRelevancyMode = GhostRelevancyMode.SetIsIrrelevant;

            m_Connections.Clear();

            var relevantSet = ghostRelevancy.GhostRelevancySet;
            var parallelRelevantSet = relevantSet.AsParallelWriter();
            var maxRelevantSize = m_GhostQuery.CalculateEntityCount() * m_ConnectionQuery.CalculateEntityCount();

            var clearJob = new ClearRelevancySet
            {
                maxRelevantSize = maxRelevantSize,
                relevantSet = relevantSet
            };
            var clearHandle = clearJob.Schedule(state.Dependency);

            m_Translations.Update(ref state);

            var connectionJob = new ConnectionRelevancyJob
            {
                transFromEntity = m_Translations,
                connections = m_Connections
            };
            var connectionHandle = connectionJob.Schedule(state.Dependency);

            var updateJob = new UpdateConnectionRelevancyJob
            {
                relevancyRadius = settings.levelData.relevancyRadius,
                connections = m_Connections,
                parallelRelevantSet = parallelRelevantSet
            };
            state.Dependency = JobHandle.CombineDependencies(state.Dependency, connectionHandle, clearHandle);
            state.Dependency = updateJob.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        partial struct UpdateConnectionRelevancyJob : IJobEntity
        {
            public float relevancyRadius;
            [ReadOnly] public NativeList<ConnectionRelevancy> connections;
            public NativeParallelHashMap<RelevantGhostForConnection, int>.ParallelWriter parallelRelevantSet;

            public void Execute(Entity entity, in GhostComponent ghost, in Translation pos)
            {
                for (int i = 0; i < connections.Length; ++i)
                {
                    if (math.distance(pos.Value, connections[i].Position) > relevancyRadius)
                        parallelRelevantSet.TryAdd(new RelevantGhostForConnection(connections[i].ConnectionId, ghost.ghostId), 1);
                }
            }

        }
        [BurstCompile]
        partial struct ConnectionRelevancyJob : IJobEntity
        {
            public ComponentLookup<Translation> transFromEntity;
            public NativeList<ConnectionRelevancy> connections;

            public void Execute(in NetworkIdComponent netId, in CommandTargetComponent target)
            {
                if (target.targetEntity == Entity.Null || !transFromEntity.HasComponent(target.targetEntity))
                    return;
                var pos = transFromEntity[target.targetEntity].Value;
                connections.Add(new ConnectionRelevancy{ConnectionId = netId.Value, Position = pos});
            }
        }

        [BurstCompile]
        struct ClearRelevancySet : IJob
        {
            public int maxRelevantSize;
            public NativeParallelHashMap<RelevantGhostForConnection, int> relevantSet;
            public void Execute()
            {
                relevantSet.Clear();
                if (relevantSet.Capacity < maxRelevantSize)
                    relevantSet.Capacity = maxRelevantSize;
            }
        }
    }
}

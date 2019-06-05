
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Networking.Transport.Utilities;

[UpdateInGroup(typeof(GhostSpawnSystemGroup))]
[AlwaysUpdateSystem]
public abstract class DefaultGhostSpawnSystem<T> : JobComponentSystem
    where T: struct, ISnapshotData<T>
{
    public int GhostType { get; set; }
    public NativeList<T> NewGhosts => m_NewGhosts;
    public NativeList<int> NewGhostIds => m_NewGhostIds;
    private NativeList<T> m_NewGhosts;
    private NativeList<int> m_NewGhostIds;
    private EntityArchetype m_Archetype;
    private EntityArchetype m_PredictedArchetype;
    private EntityArchetype m_InitialArchetype;
    private NativeHashMap<int, GhostEntity> m_GhostMap;
    private NativeHashMap<int, GhostEntity>.Concurrent m_ConcurrentGhostMap;
    private EntityQuery m_DestroyGroup;
    private EntityQuery m_SpawnRequestGroup;

    private NativeList<Entity> m_InvalidGhosts;

    struct DelayedSpawnGhost
    {
        public int ghostId;
        public uint spawnTick;
        public Entity oldEntity;
    }

    public struct PredictSpawnGhost
    {
        public T snapshotData;
        public Entity entity;
    }
    private NativeList<PredictSpawnGhost> m_PredictSpawnGhosts;
    private NativeHashMap<int, int> m_PredictionSpawnCleanupMap;

    private NativeQueue<DelayedSpawnGhost> m_DelayedSpawnQueue;
    private NativeQueue<DelayedSpawnGhost>.Concurrent m_ConcurrentDelayedSpawnQueue;
    private NativeList<DelayedSpawnGhost> m_CurrentDelayedSpawnList;
    private NativeQueue<DelayedSpawnGhost> m_PredictedSpawnQueue;
    private NativeQueue<DelayedSpawnGhost>.Concurrent m_ConcurrentPredictedSpawnQueue;
    // The entities which need to wait to be spawned on the right tick (interpolated)
    private NativeList<DelayedSpawnGhost> m_CurrentPredictedSpawnList;
    private EndSimulationEntityCommandBufferSystem m_Barrier;

    protected abstract EntityArchetype GetGhostArchetype();
    protected abstract EntityArchetype GetPredictedGhostArchetype();

    protected virtual JobHandle UpdateNewInterpolatedEntities(NativeArray<Entity> entities, JobHandle inputDeps)
    {
        return inputDeps;
    }
    protected virtual JobHandle UpdateNewPredictedEntities(NativeArray<Entity> entities, JobHandle inputDeps)
    {
        return inputDeps;
    }

    protected virtual JobHandle MarkPredictedGhosts(NativeArray<T> snapshots, NativeArray<int> predictionMask,
        NativeList<PredictSpawnGhost> predictSpawnGhosts, JobHandle inputDeps)
    {
        return inputDeps;
    }

    protected override void OnCreateManager()
    {
        m_NewGhosts = new NativeList<T>(16, Allocator.Persistent);
        m_NewGhostIds = new NativeList<int>(16, Allocator.Persistent);
        m_Archetype = GetGhostArchetype();
        m_PredictedArchetype = GetPredictedGhostArchetype();
        m_InitialArchetype = EntityManager.CreateArchetype(ComponentType.ReadWrite<T>(), ComponentType.ReadWrite<ReplicatedEntityComponent>());

        m_GhostMap = World.GetOrCreateSystem<GhostReceiveSystemGroup>().GhostEntityMap;
        m_ConcurrentGhostMap = m_GhostMap.ToConcurrent();
        m_DestroyGroup = GetEntityQuery(ComponentType.ReadOnly<T>(),
            ComponentType.Exclude<ReplicatedEntityComponent>(), ComponentType.Exclude<PredictedSpawnRequestComponent>());
        m_SpawnRequestGroup = GetEntityQuery(ComponentType.ReadOnly<T>(),
            ComponentType.ReadOnly<PredictedSpawnRequestComponent>());

        m_InvalidGhosts = new NativeList<Entity>(1024, Allocator.Persistent);
        m_DelayedSpawnQueue = new NativeQueue<DelayedSpawnGhost>(Allocator.Persistent);
        m_CurrentDelayedSpawnList = new NativeList<DelayedSpawnGhost>(1024, Allocator.Persistent);
        m_ConcurrentDelayedSpawnQueue = m_DelayedSpawnQueue.ToConcurrent();
        m_PredictedSpawnQueue = new NativeQueue<DelayedSpawnGhost>(Allocator.Persistent);
        m_CurrentPredictedSpawnList = new NativeList<DelayedSpawnGhost>(1024, Allocator.Persistent);
        m_ConcurrentPredictedSpawnQueue = m_PredictedSpawnQueue.ToConcurrent();
        m_Barrier = World.GetOrCreateSystem<EndSimulationEntityCommandBufferSystem>();
        
        m_PredictSpawnGhosts = new NativeList<PredictSpawnGhost>(16, Allocator.Persistent);
        m_PredictionSpawnCleanupMap = new NativeHashMap<int, int>(16, Allocator.Persistent);
    }

    protected override void OnDestroyManager()
    {
        m_NewGhosts.Dispose();
        m_NewGhostIds.Dispose();

        m_InvalidGhosts.Dispose();
        m_DelayedSpawnQueue.Dispose();
        m_CurrentDelayedSpawnList.Dispose();
        m_PredictedSpawnQueue.Dispose();
        m_CurrentPredictedSpawnList.Dispose();

        m_PredictSpawnGhosts.Dispose();
        m_PredictionSpawnCleanupMap.Dispose();
    }

    [BurstCompile]
    struct CopyInitialStateJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Entity> entities;
        [ReadOnly] public NativeList<T> newGhosts;
        [ReadOnly] public NativeList<int> newGhostIds;
        [NativeDisableParallelForRestriction] public BufferFromEntity<T> snapshotFromEntity;
        public NativeHashMap<int, GhostEntity>.Concurrent ghostMap;
        public int ghostType;
        public NativeQueue<DelayedSpawnGhost>.Concurrent pendingSpawnQueue;
        public NativeQueue<DelayedSpawnGhost>.Concurrent predictedSpawnQueue;
        [DeallocateOnJobCompletion][ReadOnly] public NativeArray<int> predictionMask;
        [ReadOnly] public NativeList<PredictSpawnGhost> predictionSpawnGhosts;
        public NativeHashMap<int, int>.Concurrent predictionSpawnCleanupMap;
        public EntityCommandBuffer.Concurrent commandBuffer;
        public void Execute(int i)
        {
            var entity = entities[i];
            if (predictionMask[i] == 0)
            {
                pendingSpawnQueue.Enqueue(new DelayedSpawnGhost{ghostId = newGhostIds[i], spawnTick = newGhosts[i].Tick, oldEntity = entity});
            }
            // If multiple entities map to the same prediction spawned entity, the first one will get it, the others are treated like regular spawns
            else if (predictionMask[i] > 1 && predictionSpawnCleanupMap.TryAdd(predictionMask[i]-2, 1))
            {
                commandBuffer.DestroyEntity(i, entity);
                entity = predictionSpawnGhosts[predictionMask[i]-2].entity;
            }
            else
            {
                predictedSpawnQueue.Enqueue(new DelayedSpawnGhost
                    {ghostId = newGhostIds[i], spawnTick = newGhosts[i].Tick, oldEntity = entity});
            }
            var snapshot = snapshotFromEntity[entity];
            snapshot.ResizeUninitialized(1);
            snapshot[0] = newGhosts[i];
            ghostMap.TryAdd(newGhostIds[i], new GhostEntity
            {
                entity = entity,
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                ghostType = ghostType
#endif
            });
        }
    }

    [BurstCompile]
    struct DelayedSpawnJob : IJob
    {
        [ReadOnly] public NativeArray<Entity> entities;
        [ReadOnly] public NativeList<DelayedSpawnGhost> delayedGhost;
        [NativeDisableParallelForRestriction] public BufferFromEntity<T> snapshotFromEntity;
        public NativeHashMap<int, GhostEntity> ghostMap;
        public int ghostType;
        public void Execute()
        {
            for (int i = 0; i < entities.Length; ++i)
            {
                var newSnapshot = snapshotFromEntity[entities[i]];
                var oldSnapshot = snapshotFromEntity[delayedGhost[i].oldEntity];
                newSnapshot.ResizeUninitialized(oldSnapshot.Length);
                for (int snap = 0; snap < newSnapshot.Length; ++snap)
                    newSnapshot[snap] = oldSnapshot[snap];
                ghostMap.Remove(delayedGhost[i].ghostId);
                ghostMap.TryAdd(delayedGhost[i].ghostId, new GhostEntity
                {
                    entity = entities[i],
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    ghostType = ghostType
#endif
                });
            }
        }
    }
    [BurstCompile]
    struct ClearNewJob : IJob
    {
        [DeallocateOnJobCompletion] public NativeArray<Entity> entities;
        [DeallocateOnJobCompletion] public NativeArray<Entity> visibleEntities;
        [DeallocateOnJobCompletion] public NativeArray<Entity> visiblePredictedEntities;
        [DeallocateOnJobCompletion] public NativeArray<Entity> predictSpawnEntities;
        [DeallocateOnJobCompletion] public NativeArray<Entity> predictSpawnRequests;
        public NativeList<T> newGhosts;
        public NativeList<int> newGhostIds;
        public void Execute()
        {
            newGhosts.Clear();
            newGhostIds.Clear();
        }
    }

    struct PredictSpawnJob : IJob
    {
        public NativeArray<Entity> requests;
        public NativeArray<Entity> entities;
        public BufferFromEntity<T> snapshotFromEntity;
        public EntityCommandBuffer commandBuffer;
        public NativeList<PredictSpawnGhost> predictSpawnGhosts;
        public void Execute()
        {
            for (int i = 0; i < requests.Length; ++i)
            {
                var srcSnap = snapshotFromEntity[requests[i]];
                var dstSnap = snapshotFromEntity[entities[i]];
                dstSnap.ResizeUninitialized(1);
                dstSnap[0] = srcSnap[0];
                commandBuffer.DestroyEntity(requests[i]);
                predictSpawnGhosts.Add(new PredictSpawnGhost {snapshotData = srcSnap[0], entity = entities[i]});
            }
        }
    }
    [BurstCompile]
    struct PredictSpawnCleanupJob : IJob
    {
        public NativeHashMap<int, int> predictionSpawnCleanupMap;
        public NativeList<PredictSpawnGhost> predictionSpawnGhosts;
        public uint interpolationTarget;
        public EntityCommandBuffer commandBuffer;
        public ComponentType replicatedEntityComponentType;
        public void Execute()
        {
            var keys = predictionSpawnCleanupMap.GetKeyArray(Allocator.Temp);
            for (var i = 0; i < keys.Length; ++i)
                predictionSpawnGhosts[keys[i]] = default(PredictSpawnGhost);
            for (int i = 0; i < predictionSpawnGhosts.Length; ++i)
            {
                if (predictionSpawnGhosts[i].entity != Entity.Null &&
                    SequenceHelpers.IsNewer(interpolationTarget, predictionSpawnGhosts[i].snapshotData.Tick))
                {
                    // Trigger a delete of the entity
                    commandBuffer.RemoveComponent(predictionSpawnGhosts[i].entity, replicatedEntityComponentType);
                    predictionSpawnGhosts[i] = default(PredictSpawnGhost);
                }
                if (predictionSpawnGhosts[i].entity == Entity.Null)
                {
                    predictionSpawnGhosts.RemoveAtSwapBack(i);
                    --i;
                }
            }
        }
    }

    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        EntityManager.DestroyEntity(m_DestroyGroup);
        EntityManager.DestroyEntity(m_InvalidGhosts);
        m_InvalidGhosts.Clear();

        var targetTick = NetworkTimeSystem.interpolateTargetTick;
        m_CurrentDelayedSpawnList.Clear();
        while (m_DelayedSpawnQueue.Count > 0 &&
               !SequenceHelpers.IsNewer(m_DelayedSpawnQueue.Peek().spawnTick, targetTick))
        {
            var ghost = m_DelayedSpawnQueue.Dequeue();
            GhostEntity gent;
            if (m_GhostMap.TryGetValue(ghost.ghostId, out gent))
            {
                m_CurrentDelayedSpawnList.Add(ghost);
                m_InvalidGhosts.Add(gent.entity);
            }
        }
        m_CurrentPredictedSpawnList.Clear();
        while (m_PredictedSpawnQueue.Count > 0)
        {
            var ghost = m_PredictedSpawnQueue.Dequeue();
            GhostEntity gent;
            if (m_GhostMap.TryGetValue(ghost.ghostId, out gent))
            {
                m_CurrentPredictedSpawnList.Add(ghost);
                m_InvalidGhosts.Add(gent.entity);
            }
        }

        var delayedEntities = default(NativeArray<Entity>);
        delayedEntities = new NativeArray<Entity>(m_CurrentDelayedSpawnList.Length, Allocator.TempJob);
        if (m_CurrentDelayedSpawnList.Length > 0)
            EntityManager.CreateEntity(m_Archetype, delayedEntities);

        var predictedEntities = default(NativeArray<Entity>);
        predictedEntities = new NativeArray<Entity>(m_CurrentPredictedSpawnList.Length, Allocator.TempJob);
        if (m_CurrentPredictedSpawnList.Length > 0)
            EntityManager.CreateEntity(m_PredictedArchetype, predictedEntities);

        var predictSpawnRequests = m_SpawnRequestGroup.ToEntityArray(Allocator.TempJob);
        var predictSpawnEntities = new NativeArray<Entity>(predictSpawnRequests.Length, Allocator.TempJob);
        if (predictSpawnEntities.Length > 0)
            EntityManager.CreateEntity(m_PredictedArchetype, predictSpawnEntities);

        var newEntities = default(NativeArray<Entity>);
        newEntities = new NativeArray<Entity>(m_NewGhosts.Length, Allocator.TempJob);
        if (m_NewGhosts.Length > 0)
            EntityManager.CreateEntity(m_InitialArchetype, newEntities);

        if (m_CurrentDelayedSpawnList.Length > 0)
        {
            var delayedjob = new DelayedSpawnJob
            {
                entities = delayedEntities,
                delayedGhost = m_CurrentDelayedSpawnList,
                snapshotFromEntity = GetBufferFromEntity<T>(),
                ghostMap = m_GhostMap,
                ghostType = GhostType
            };
            inputDeps = delayedjob.Schedule(inputDeps);
            inputDeps = UpdateNewInterpolatedEntities(delayedEntities, inputDeps);
        }
        // FIXME: current and predicted can run in parallel I think
        if (m_CurrentPredictedSpawnList.Length > 0)
        {
            var delayedjob = new DelayedSpawnJob
            {
                entities = predictedEntities,
                delayedGhost = m_CurrentPredictedSpawnList,
                snapshotFromEntity = GetBufferFromEntity<T>(),
                ghostMap = m_GhostMap,
                ghostType = GhostType
            };
            inputDeps = delayedjob.Schedule(inputDeps);
            inputDeps = UpdateNewPredictedEntities(predictedEntities, inputDeps);
        }
        if (predictSpawnRequests.Length > 0)
        {
            var spawnJob = new PredictSpawnJob
            {
                requests = predictSpawnRequests,
                entities = predictSpawnEntities,
                snapshotFromEntity = GetBufferFromEntity<T>(),
                commandBuffer = m_Barrier.CreateCommandBuffer(),
                predictSpawnGhosts = m_PredictSpawnGhosts
            };
            inputDeps = spawnJob.Schedule(inputDeps);
            inputDeps = UpdateNewPredictedEntities(predictSpawnEntities, inputDeps);
        }

        m_PredictionSpawnCleanupMap.Clear();
        if (m_NewGhosts.Length > 0)
        {
            if (m_PredictionSpawnCleanupMap.Capacity < m_NewGhosts.Length)
                m_PredictionSpawnCleanupMap.Capacity = m_NewGhosts.Length;
            var predictionMask = new NativeArray<int>(m_NewGhosts.Length, Allocator.TempJob);
            inputDeps = MarkPredictedGhosts(m_NewGhosts, predictionMask, m_PredictSpawnGhosts, inputDeps);
            var job = new CopyInitialStateJob
            {
                entities = newEntities,
                newGhosts = m_NewGhosts,
                newGhostIds = m_NewGhostIds,
                snapshotFromEntity = GetBufferFromEntity<T>(),
                ghostMap = m_ConcurrentGhostMap,
                ghostType = GhostType,
                pendingSpawnQueue = m_ConcurrentDelayedSpawnQueue,
                predictedSpawnQueue = m_ConcurrentPredictedSpawnQueue,
                predictionMask = predictionMask,
                predictionSpawnGhosts = m_PredictSpawnGhosts,
                predictionSpawnCleanupMap = m_PredictionSpawnCleanupMap.ToConcurrent(),
                commandBuffer = m_Barrier.CreateCommandBuffer().ToConcurrent()
            };
            inputDeps = job.Schedule(newEntities.Length, 8, inputDeps);
        }

        var spawnClearJob = new PredictSpawnCleanupJob
        {
            predictionSpawnCleanupMap = m_PredictionSpawnCleanupMap,
            predictionSpawnGhosts = m_PredictSpawnGhosts,
            interpolationTarget = targetTick,
            commandBuffer = m_Barrier.CreateCommandBuffer(),
            replicatedEntityComponentType = ComponentType.ReadWrite<ReplicatedEntityComponent>()
        };
        inputDeps = spawnClearJob.Schedule(inputDeps);
        m_Barrier.AddJobHandleForProducer(inputDeps);

        var clearJob = new ClearNewJob
        {
            entities = newEntities,
            visibleEntities = delayedEntities,
            visiblePredictedEntities = predictedEntities,
            newGhosts = m_NewGhosts,
            newGhostIds = m_NewGhostIds,
            predictSpawnEntities = predictSpawnEntities,
            predictSpawnRequests = predictSpawnRequests
        };
        return clearJob.Schedule(inputDeps);
    }
}


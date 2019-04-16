using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Networking.Transport;
using Unity.Networking.Transport.LowLevel.Unsafe;
using Unity.Networking.Transport.Utilities;

public struct ReplicatedEntity : IComponentData
{}

[UpdateInGroup(typeof(ClientSimulationSystemGroup))]
[UpdateAfter(typeof(NetworkStreamReceiveSystem))]
public class GhostReceiveSystemGroup : ComponentSystemGroup
{
    private GhostReceiveSystem m_recvSystem;
    protected override void OnCreateManager()
    {
        m_recvSystem = World.GetOrCreateManager<GhostReceiveSystem>();
    }

    protected override void OnUpdate()
    {
        base.OnUpdate();
        m_recvSystem.Update();
    }
}

[DisableAutoCreation]
[UpdateInGroup(typeof(ClientSimulationSystemGroup))]
public class GhostSpawnSystemGroup : ComponentSystemGroup
{}

[AlwaysUpdateSystem]
[DisableAutoCreation]
public class GhostReceiveSystem : JobComponentSystem
{
    private ComponentGroup playerGroup;

    private GhostDeserializerCollection serializers;

    public struct GhostEntity
    {
        public Entity entity;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        public int ghostType;
#endif
    }

    struct DelayedDespawnGhost
    {
        public Entity ghost;
        public uint tick;
    }

    public NativeHashMap<int, GhostEntity> GhostEntityMap => m_ghostEntityMap;
    private NativeHashMap<int, GhostEntity> m_ghostEntityMap;
    private BeginSimulationEntityCommandBufferSystem m_Barrier;

    private NativeQueue<DelayedDespawnGhost> m_DelayedDespawnQueue;
    protected override void OnCreateManager()
    {
        m_ghostEntityMap = new NativeHashMap<int, GhostEntity>(2048, Allocator.Persistent);

        playerGroup = GetComponentGroup(
            ComponentType.ReadWrite<NetworkStreamConnection>(),
            ComponentType.ReadOnly<PlayerStateComponentData>(),
            ComponentType.Exclude<NetworkStreamDisconnected>());

        m_Barrier = World.GetOrCreateManager<BeginSimulationEntityCommandBufferSystem>();
        m_CompressionModel = new NetworkCompressionModel(Allocator.Persistent);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        m_NetStats = new NativeArray<uint>(serializers.Length * 3 + 3, Allocator.Persistent);
#endif

        m_DelayedDespawnQueue = new NativeQueue<DelayedDespawnGhost>(Allocator.Persistent);
        
        serializers.Initialize(World);
    }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
    public NativeArray<uint> NetStats => m_NetStats;
    private NativeArray<uint> m_NetStats;
#endif
    private NetworkCompressionModel m_CompressionModel;

    protected override void OnDestroyManager()
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        m_NetStats.Dispose();
#endif
        m_CompressionModel.Dispose();
        m_ghostEntityMap.Dispose();
        m_DelayedDespawnQueue.Dispose();
    }

    struct ClearGhostsJob : IJobProcessComponentDataWithEntity<ReplicatedEntity>
    {
        public EntityCommandBuffer.Concurrent commandBuffer;
        public void Execute(Entity entity, int index, [ReadOnly] ref ReplicatedEntity repl)
        {
            commandBuffer.RemoveComponent<ReplicatedEntity>(index, entity);
        }
    }

    struct ClearMapJob : IJob
    {
        public NativeHashMap<int, GhostEntity> ghostMap;
        public void Execute()
        {
            ghostMap.Clear();
        }
    }

    [BurstCompile]
    struct ReadStreamJob : IJob
    {
        public EntityCommandBuffer commandBuffer;
        [DeallocateOnJobCompletion] public NativeArray<Entity> players;
        public BufferFromEntity<IncomingSnapshotDataStreamBufferComponent> snapshotFromEntity;
        public ComponentDataFromEntity<NetworkSnapshotAck> snapshotAckFromEntity;
        public NativeHashMap<int, GhostEntity> ghostEntityMap;
        public NetworkCompressionModel compressionModel;
        public GhostDeserializerCollection serializers;
        #if ENABLE_UNITY_COLLECTIONS_CHECKS
        public NativeArray<uint> netStats;
        #endif
        public ComponentType replicatedEntityType;
        public NativeQueue<DelayedDespawnGhost> delayedDespawnQueue;
        public uint targetTick;
        public unsafe void Execute()
        {
            // FIXME: should handle any number of connections with individual ghost mappings for each
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (players.Length > 1)
                throw new InvalidOperationException("Ghost receive system only supports a single connection");
#endif
            while (delayedDespawnQueue.Count > 0 &&
                   !SequenceHelpers.IsNewer(delayedDespawnQueue.Peek().tick, targetTick))
            {
                commandBuffer.RemoveComponent(delayedDespawnQueue.Dequeue().ghost, replicatedEntityType);
            }
            
            var snapshot = snapshotFromEntity[players[0]];
            if (snapshot.Length == 0)
                return;

            var dataStream =
                DataStreamUnsafeUtility.CreateReaderFromExistingData((byte*)snapshot.GetUnsafePtr(), snapshot.Length);
            // Read the ghost stream
            // find entities to spawn or destroy
            var readCtx = new DataStreamReader.Context();
            var serverTick = dataStream.ReadUInt(ref readCtx);
            var ack = snapshotAckFromEntity[players[0]];
            if (ack.LastReceivedSnapshotByLocal != 0 && !SequenceHelpers.IsNewer(serverTick, ack.LastReceivedSnapshotByLocal))
                return;
            if (ack.LastReceivedSnapshotByLocal != 0)
                ack.ReceivedSnapshotByLocalMask <<= (int)(serverTick - ack.LastReceivedSnapshotByLocal);
            ack.ReceivedSnapshotByLocalMask |= 1;
            ack.LastReceivedSnapshotByLocal = serverTick;
            snapshotAckFromEntity[players[0]] = ack;

            uint despawnLen = dataStream.ReadUInt(ref readCtx);
            uint updateLen = dataStream.ReadUInt(ref readCtx);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            int startPos = dataStream.GetBitsRead(ref readCtx);
#endif
            for (var i = 0; i < despawnLen; ++i)
            {
                int ghostId = (int)dataStream.ReadPackedUInt(ref readCtx, compressionModel);
                GhostEntity ent;
                if (!ghostEntityMap.TryGetValue(ghostId, out ent))
                    continue;

                ghostEntityMap.Remove(ghostId);
                delayedDespawnQueue.Enqueue(new DelayedDespawnGhost {ghost = ent.entity, tick = serverTick});
            }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            netStats[0] = despawnLen;
            netStats[1] = (uint) (dataStream.GetBitsRead(ref readCtx) - startPos);
            netStats[2] = 0;
            for (int i = 0; i < serializers.Length; ++i)
            {
                netStats[i * 3 + 3] = 0;
                netStats[i * 3 + 4] = 0;
                netStats[i * 3 + 5] = 0;
            }
            uint statCount = 0;
            uint uncompressedCount = 0;
#endif

            uint targetArch = 0;
            uint targetArchLen = 0;
            uint baselineTick = 0;
            uint baselineTick2 = 0;
            uint baselineTick3 = 0;
            uint baselineLen = 0;
            int newGhosts = 0;
            for (var i = 0; i < updateLen; ++i)
            {
                if (targetArchLen == 0)
                {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    int curPos = dataStream.GetBitsRead(ref readCtx);
                    if (statCount > 0)
                    {
                        int statType = (int) targetArch;
                        netStats[statType * 3 + 3] = netStats[statType * 3 + 3] + statCount;
                        netStats[statType * 3 + 4] = netStats[statType * 3 + 4] + (uint)(curPos - startPos);
                        netStats[statType * 3 + 5] = netStats[statType * 3 + 5] + uncompressedCount;
                    }

                    startPos = curPos;
                    statCount = 0;
                    uncompressedCount = 0;
#endif
                    targetArch = dataStream.ReadPackedUInt(ref readCtx, compressionModel);
                    targetArchLen = dataStream.ReadPackedUInt(ref readCtx, compressionModel);
                }
                --targetArchLen;

                if (baselineLen == 0)
                {
                    baselineTick = serverTick - dataStream.ReadPackedUInt(ref readCtx, compressionModel);
                    baselineTick2 = serverTick - dataStream.ReadPackedUInt(ref readCtx, compressionModel);
                    baselineTick3 = serverTick - dataStream.ReadPackedUInt(ref readCtx, compressionModel);
                    baselineLen = dataStream.ReadPackedUInt(ref readCtx, compressionModel);
                }
                --baselineLen;

                int ghostId = (int)dataStream.ReadPackedUInt(ref readCtx, compressionModel);
                GhostEntity gent;
                if (ghostEntityMap.TryGetValue(ghostId, out gent))
                {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    if (gent.ghostType != targetArch)
                        throw new InvalidOperationException("Received a ghost with an invalid ghost type");
                        //throw new InvalidOperationException("Received a ghost with an invalid ghost type " + targetArch + ", expected " + gent.ghostType);
#endif
                    serializers.Deserialize((int) targetArch, gent.entity, serverTick, baselineTick, baselineTick2, baselineTick3,
                        dataStream, ref readCtx, compressionModel);
                }
                else
                {
                    ++newGhosts;
                    serializers.Spawn((int)targetArch, ghostId, serverTick, dataStream, ref readCtx, compressionModel);
                }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                ++statCount;
                if (baselineTick == serverTick)
                    ++uncompressedCount;
#endif
            }
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (statCount > 0)
            {
                int curPos = dataStream.GetBitsRead(ref readCtx);
                int statType = (int) targetArch;
                netStats[statType * 3 + 3] = netStats[statType * 3 + 3] + statCount;
                netStats[statType * 3 + 4] = netStats[statType * 3 + 4] + (uint)(curPos - startPos);
                netStats[statType * 3 + 5] = netStats[statType * 3 + 5] + uncompressedCount;
            }
#endif
            while (ghostEntityMap.Capacity < ghostEntityMap.Length + newGhosts)
                ghostEntityMap.Capacity += 1024;
        }
    }

    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        var commandBuffer = m_Barrier.CreateCommandBuffer();
        if (playerGroup.IsEmptyIgnoreFilter)
        {
            m_DelayedDespawnQueue.Clear();
            var clearMapJob = new ClearMapJob
            {
                ghostMap = m_ghostEntityMap
            };
            var clearHandle = clearMapJob.Schedule(inputDeps);
            var clearJob = new ClearGhostsJob
            {
                commandBuffer = commandBuffer.ToConcurrent()
            };
            inputDeps = clearJob.Schedule(this, inputDeps);
            m_Barrier.AddJobHandleForProducer(inputDeps);
            return JobHandle.CombineDependencies(inputDeps, clearHandle);
        }

        serializers.BeginDeserialize(this);
        JobHandle playerHandle;
        var readJob = new ReadStreamJob
        {
            commandBuffer = commandBuffer,
            players = playerGroup.ToEntityArray(Allocator.TempJob, out playerHandle),
            snapshotFromEntity = GetBufferFromEntity<IncomingSnapshotDataStreamBufferComponent>(),
            snapshotAckFromEntity = GetComponentDataFromEntity<NetworkSnapshotAck>(),
            ghostEntityMap = m_ghostEntityMap,
            compressionModel = m_CompressionModel,
            serializers = serializers,
            #if ENABLE_UNITY_COLLECTIONS_CHECKS
            netStats = m_NetStats,
            #endif
            replicatedEntityType = ComponentType.ReadWrite<ReplicatedEntity>(),
            delayedDespawnQueue = m_DelayedDespawnQueue,
            targetTick = NetworkTimeSystem.interpolateTargetTick
        };
        inputDeps = readJob.Schedule(JobHandle.CombineDependencies(inputDeps, playerHandle));

        m_Barrier.AddJobHandleForProducer(inputDeps);
        return inputDeps;
    }

    public static T InvokeSpawn<T>(uint snapshot,
        DataStreamReader reader, ref DataStreamReader.Context ctx, NetworkCompressionModel compressionModel)
        where T : struct, ISnapshotData<T>
    {
        var snapshotData = default(T);
        var baselineData = default(T);
        snapshotData.Deserialize(snapshot, ref baselineData, reader, ref ctx, compressionModel);
        return snapshotData;
    }

    public static void InvokeDeserialize<T>(BufferFromEntity<T> snapshotFromEntity,
        Entity entity, uint snapshot, uint baseline, uint baseline2, uint baseline3,
        DataStreamReader reader, ref DataStreamReader.Context ctx, NetworkCompressionModel compressionModel)
    where T: struct, ISnapshotData<T>
    {
        DynamicBuffer<T> snapshotArray = snapshotFromEntity[entity];
        var baselineData = default(T);
        if (baseline != snapshot)
        {
            for (int i = 0; i < snapshotArray.Length; ++i)
            {
                if (snapshotArray[i].Tick == baseline)
                {
                    baselineData = snapshotArray[i];
                    break;
                }
            }
        }
        if (baseline3 != snapshot)
        {
            var baselineData2 = default(T);
            var baselineData3 = default(T);
            for (int i = 0; i < snapshotArray.Length; ++i)
            {
                if (snapshotArray[i].Tick == baseline2)
                {
                    baselineData2 = snapshotArray[i];
                }
                if (snapshotArray[i].Tick == baseline3)
                {
                    baselineData3 = snapshotArray[i];
                }
            }

            baselineData.PredictDelta(snapshot, ref baselineData2, ref baselineData3);
        }
        var data = default(T);
        data.Deserialize(snapshot, ref baselineData, reader, ref ctx, compressionModel);
        // Replace the oldest snapshot, or add a new one
        if (snapshotArray.Length == GhostSendSystem.SnapshotHistorySize)
            snapshotArray.RemoveAt(0);
        snapshotArray.Add(data);
    }

}

[UpdateInGroup(typeof(GhostSpawnSystemGroup))]
[AlwaysUpdateSystem]
// FIXME: should delay create and destroy to match interpolation time
public abstract class DefaultGhostSpawnSystem<T> : JobComponentSystem where T: struct, ISnapshotData<T>
{
    public int GhostType { get; set; }
    public NativeList<T> NewGhosts => m_NewGhosts;
    public NativeList<int> NewGhostIds => m_NewGhostIds;
    private NativeList<T> m_NewGhosts;
    private NativeList<int> m_NewGhostIds;
    private EntityArchetype m_Archetype;
    private EntityArchetype m_InitialArchetype;
    private NativeHashMap<int, GhostReceiveSystem.GhostEntity> m_GhostMap;
    private NativeHashMap<int, GhostReceiveSystem.GhostEntity>.Concurrent m_ConcurrentGhostMap;
    private ComponentGroup m_DestroyGroup;

    private NativeList<Entity> m_InvalidGhosts;

    struct DelayedSpawnGhost
    {
        public int ghostId;
        public uint spawnTick;
        public Entity oldEntity;
    }

    private NativeQueue<DelayedSpawnGhost> m_DelayedSpawnQueue;
    private NativeQueue<DelayedSpawnGhost>.Concurrent m_ConcurrentDelayedSpawnQueue;
    private NativeList<DelayedSpawnGhost> m_CurrentDelayedSpawnList;

    protected abstract EntityArchetype GetGhostArchetype();

    protected virtual JobHandle UpdateNewEntities(NativeArray<Entity> entities, JobHandle inputDeps)
    {
        return inputDeps;
    }

    protected override void OnCreateManager()
    {
        m_NewGhosts = new NativeList<T>(16, Allocator.Persistent);
        m_NewGhostIds = new NativeList<int>(16, Allocator.Persistent);
        m_Archetype = GetGhostArchetype();
        m_InitialArchetype = EntityManager.CreateArchetype(ComponentType.ReadWrite<T>(), ComponentType.ReadWrite<ReplicatedEntity>());

        m_GhostMap = World.GetOrCreateManager<GhostReceiveSystem>().GhostEntityMap;
        m_ConcurrentGhostMap = m_GhostMap.ToConcurrent();
        m_DestroyGroup = GetComponentGroup(ComponentType.ReadOnly<T>(),
            ComponentType.Exclude<ReplicatedEntity>());

        m_InvalidGhosts = new NativeList<Entity>(1024, Allocator.Persistent);
        m_DelayedSpawnQueue = new NativeQueue<DelayedSpawnGhost>(Allocator.Persistent);
        m_CurrentDelayedSpawnList = new NativeList<DelayedSpawnGhost>(1024, Allocator.Persistent);
        m_ConcurrentDelayedSpawnQueue = m_DelayedSpawnQueue.ToConcurrent();
    }

    protected override void OnDestroyManager()
    {
        m_NewGhosts.Dispose();
        m_NewGhostIds.Dispose();

        m_InvalidGhosts.Dispose();
        m_DelayedSpawnQueue.Dispose();
        m_CurrentDelayedSpawnList.Dispose();
    }

    [BurstCompile]
    struct CopyInitialStateJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Entity> entities;
        [ReadOnly] public NativeList<T> newGhosts;
        [ReadOnly] public NativeList<int> newGhostIds;
        [NativeDisableParallelForRestriction] public BufferFromEntity<T> snapshotFromEntity;
        public NativeHashMap<int, GhostReceiveSystem.GhostEntity>.Concurrent ghostMap;
        public int ghostType;
        public NativeQueue<DelayedSpawnGhost>.Concurrent pendingSpawnQueue;
        public void Execute(int i)
        {
            var snapshot = snapshotFromEntity[entities[i]];
            snapshot.ResizeUninitialized(1);
            snapshot[0] = newGhosts[i];
            ghostMap.TryAdd(newGhostIds[i], new GhostReceiveSystem.GhostEntity
            {
                entity = entities[i],
                #if ENABLE_UNITY_COLLECTIONS_CHECKS
                ghostType = ghostType
                #endif
            });
            pendingSpawnQueue.Enqueue(new DelayedSpawnGhost{ghostId = newGhostIds[i], spawnTick = newGhosts[i].Tick, oldEntity = entities[i]});
        }
    }
    [BurstCompile]
    struct DelayedSpawnJob : IJob
    {
        [ReadOnly] public NativeArray<Entity> entities;
        [ReadOnly] public NativeList<DelayedSpawnGhost> delayedGhost;
        [NativeDisableParallelForRestriction] public BufferFromEntity<T> snapshotFromEntity;
        public NativeHashMap<int, GhostReceiveSystem.GhostEntity> ghostMap;
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
                ghostMap.TryAdd(delayedGhost[i].ghostId, new GhostReceiveSystem.GhostEntity
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
        public NativeList<T> newGhosts;
        public NativeList<int> newGhostIds;
        public void Execute()
        {
            newGhosts.Clear();
            newGhostIds.Clear();
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
            GhostReceiveSystem.GhostEntity gent;
            if (m_GhostMap.TryGetValue(ghost.ghostId, out gent))
            {
                m_CurrentDelayedSpawnList.Add(ghost);
                m_InvalidGhosts.Add(gent.entity);
            }
        }

        var delayedEntities = default(NativeArray<Entity>);
        delayedEntities = new NativeArray<Entity>(m_CurrentDelayedSpawnList.Length, Allocator.TempJob);
        if (m_CurrentDelayedSpawnList.Length > 0)
            EntityManager.CreateEntity(m_Archetype, delayedEntities);

        var entities = default(NativeArray<Entity>);
        entities = new NativeArray<Entity>(m_NewGhosts.Length, Allocator.TempJob);
        if (m_NewGhosts.Length > 0)
            EntityManager.CreateEntity(m_InitialArchetype, entities);

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
            inputDeps = UpdateNewEntities(delayedEntities, inputDeps);
        }

        if (m_NewGhosts.Length > 0)
        {
            var job = new CopyInitialStateJob
            {
                entities = entities,
                newGhosts = m_NewGhosts,
                newGhostIds = m_NewGhostIds,
                snapshotFromEntity = GetBufferFromEntity<T>(),
                ghostMap = m_ConcurrentGhostMap,
                ghostType = GhostType,
                pendingSpawnQueue = m_ConcurrentDelayedSpawnQueue
            };
            inputDeps = job.Schedule(entities.Length, 8, inputDeps);
        }

        var clearJob = new ClearNewJob
        {
            entities = entities,
            visibleEntities = delayedEntities,
            newGhosts = m_NewGhosts,
            newGhostIds = m_NewGhostIds,
        };
        return clearJob.Schedule(inputDeps);
    }
}

using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Networking.Transport;
using Unity.Networking.Transport.LowLevel.Unsafe;
using Unity.Networking.Transport.Utilities;

internal struct GhostEntity
{
    public Entity entity;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
    public int ghostType;
#endif
}

[AlwaysUpdateSystem]
[UpdateInGroup(typeof(GhostReceiveSystemGroup))]
[UpdateAfter(typeof(GhostUpdateSystemGroup))]
public class GhostReceiveSystem<TGhostDeserializerCollection> : JobComponentSystem
    where TGhostDeserializerCollection : struct, IGhostDeserializerCollection
{
    private EntityQuery playerGroup;

    private TGhostDeserializerCollection serializers;


    struct DelayedDespawnGhost
    {
        public Entity ghost;
        public uint tick;
    }

    private NativeHashMap<int, GhostEntity> m_ghostEntityMap;
    private BeginSimulationEntityCommandBufferSystem m_Barrier;

    private NativeQueue<DelayedDespawnGhost> m_DelayedDespawnQueue;
    protected override void OnCreateManager()
    {
        serializers = default(TGhostDeserializerCollection);
        m_ghostEntityMap = World.GetOrCreateSystem<GhostReceiveSystemGroup>().GhostEntityMap;

        playerGroup = GetEntityQuery(
            ComponentType.ReadWrite<NetworkStreamConnection>(),
            ComponentType.ReadOnly<NetworkStreamInGame>(),
            ComponentType.Exclude<NetworkStreamDisconnected>());

        m_Barrier = World.GetOrCreateSystem<BeginSimulationEntityCommandBufferSystem>();
        m_CompressionModel = new NetworkCompressionModel(Allocator.Persistent);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        m_NetStats = new NativeArray<uint>(serializers.Length * 3 + 3, Allocator.Persistent);
        World.GetOrCreateSystem<GhostStatsSystem>().SetStatsBuffer(m_NetStats, serializers.CreateSerializerNameList());
#endif

        m_DelayedDespawnQueue = new NativeQueue<DelayedDespawnGhost>(Allocator.Persistent);

        serializers.Initialize(World);
    }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
    private NativeArray<uint> m_NetStats;
#endif
    private NetworkCompressionModel m_CompressionModel;

    protected override void OnDestroyManager()
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        m_NetStats.Dispose();
#endif
        m_CompressionModel.Dispose();
        m_DelayedDespawnQueue.Dispose();
    }

    struct ClearGhostsJob : IJobForEachWithEntity<ReplicatedEntityComponent>
    {
        public EntityCommandBuffer.Concurrent commandBuffer;
        public void Execute(Entity entity, int index, [ReadOnly] ref ReplicatedEntityComponent repl)
        {
            commandBuffer.RemoveComponent<ReplicatedEntityComponent>(index, entity);
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
        public ComponentDataFromEntity<NetworkSnapshotAckComponent> snapshotAckFromEntity;
        public NativeHashMap<int, GhostEntity> ghostEntityMap;
        public NetworkCompressionModel compressionModel;
        public TGhostDeserializerCollection serializers;
        #if ENABLE_UNITY_COLLECTIONS_CHECKS
        public NativeArray<uint> netStats;
        #endif
        public ComponentType replicatedEntityType;
        public NativeQueue<DelayedDespawnGhost> delayedDespawnQueue;
        public uint targetTick;
        [ReadOnly] public ComponentDataFromEntity<PredictedEntityComponent> predictedFromEntity;
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
                // if predicted, despawn now, otherwise wait
                if (predictedFromEntity.Exists(ent.entity))
                    commandBuffer.RemoveComponent(ent.entity, replicatedEntityType);
                else
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
            snapshotAckFromEntity = GetComponentDataFromEntity<NetworkSnapshotAckComponent>(),
            ghostEntityMap = m_ghostEntityMap,
            compressionModel = m_CompressionModel,
            serializers = serializers,
            #if ENABLE_UNITY_COLLECTIONS_CHECKS
            netStats = m_NetStats,
            #endif
            replicatedEntityType = ComponentType.ReadWrite<ReplicatedEntityComponent>(),
            delayedDespawnQueue = m_DelayedDespawnQueue,
            targetTick = NetworkTimeSystem.interpolateTargetTick,
            predictedFromEntity = GetComponentDataFromEntity<PredictedEntityComponent>(true)
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
        // Replace the oldest snapshot and add a new one
        if (snapshotArray.Length == GhostSystemConstants.SnapshotHistorySize)
            snapshotArray.RemoveAt(0);
        snapshotArray.Add(data);
    }

}

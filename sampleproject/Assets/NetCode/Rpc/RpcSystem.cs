using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Networking.Transport;
using Unity.Networking.Transport.LowLevel.Unsafe;
using UnityEngine;


[UpdateInGroup(typeof(ClientAndServerSimulationSystemGroup))]
[UpdateAfter(typeof(NetworkStreamReceiveSystem))]
public class RpcSystem<TRpcCollection> : JobComponentSystem
    where TRpcCollection : struct, IRpcCollection
{
    private InternalRpcCollection m_InternalRpcCollection;
    private int m_InternalRpcCollectionLength;
    private TRpcCollection m_RpcCollection;
    private EntityQuery m_RpcBufferGroup;
    private BeginSimulationEntityCommandBufferSystem m_Barrier;
    protected override void OnCreateManager()
    {
        m_InternalRpcCollection = default(InternalRpcCollection);
        m_InternalRpcCollectionLength = m_InternalRpcCollection.Length;
        m_RpcCollection = default(TRpcCollection);
        #if ENABLE_UNITY_COLLECTIONS_CHECKS
        Debug.Assert(UnsafeUtility.SizeOf<OutgoingRpcDataStreamBufferComponent>() == 1);
        Debug.Assert(UnsafeUtility.SizeOf<IncomingRpcDataStreamBufferComponent>() == 1);
        #endif
        m_Barrier = World.GetOrCreateSystem<BeginSimulationEntityCommandBufferSystem>();
        m_RpcBufferGroup = GetEntityQuery(ComponentType.ReadWrite<IncomingRpcDataStreamBufferComponent>());
    }

    struct RpcExecJob : IJobChunk
    {
        public EntityCommandBuffer.Concurrent commandBuffer;
        [ReadOnly] public ArchetypeChunkEntityType entityType;
        public ArchetypeChunkBufferType<IncomingRpcDataStreamBufferComponent> bufferType;
        public InternalRpcCollection internalRpcCollection;
        public int internalRpcCollectionLength;
        public TRpcCollection rpcCollection;
        public unsafe void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
        {
            var entities = chunk.GetNativeArray(entityType);
            var bufferAccess = chunk.GetBufferAccessor(bufferType);
            for (int i = 0; i < bufferAccess.Length; ++i)
            {
                var dynArray = bufferAccess[i];
                DataStreamReader reader = DataStreamUnsafeUtility.CreateReaderFromExistingData((byte*)dynArray.GetUnsafePtr(), dynArray.Length);
                var ctx = default(DataStreamReader.Context);
                while (reader.GetBytesRead(ref ctx) < reader.Length)
                {
                    int type = reader.ReadInt(ref ctx);
                    if (type < internalRpcCollectionLength)
                        internalRpcCollection.ExecuteRpc(type, reader, ref ctx, entities[i], commandBuffer, chunkIndex);
                    else
                        rpcCollection.ExecuteRpc(type-internalRpcCollectionLength, reader, ref ctx, entities[i], commandBuffer, chunkIndex);
                }

                dynArray.Clear();
            }
        }
    }

    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        // Deserialize the command type from the reader stream
        // Execute the RPC
        var execJob = new RpcExecJob();
        execJob.commandBuffer = m_Barrier.CreateCommandBuffer().ToConcurrent();
        execJob.entityType = GetArchetypeChunkEntityType();
        execJob.bufferType = GetArchetypeChunkBufferType<IncomingRpcDataStreamBufferComponent>();
        execJob.rpcCollection = m_RpcCollection;
        execJob.internalRpcCollection = m_InternalRpcCollection;
        execJob.internalRpcCollectionLength = m_InternalRpcCollectionLength;
        var handle = execJob.Schedule(m_RpcBufferGroup, inputDeps);
        m_Barrier.AddJobHandleForProducer(handle);
        return handle;
    }

    public RpcQueue<T> GetRpcQueue<T>() where T : struct, IRpcCommand
    {
        int t = m_RpcCollection.GetRpcFromType<T>();
        #if ENABLE_UNITY_COLLECTIONS_CHECKS
        if (t < 0)
            throw new InvalidOperationException("Trying to get a rpc type which is not registered");
        #endif
        return new RpcQueue<T>{rpcType = t+m_InternalRpcCollectionLength};
    }
}

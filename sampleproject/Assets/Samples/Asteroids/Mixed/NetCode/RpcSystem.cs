using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Networking.Transport;
using Unity.Networking.Transport.LowLevel.Unsafe;
using UnityEngine;

public interface RpcCommand
{
    void Execute(Entity connection, EntityCommandBuffer.Concurrent commandBuffer, int jobIndex);
    void Serialize(DataStreamWriter writer);
    void Deserialize(DataStreamReader reader, ref DataStreamReader.Context ctx);
}

public struct RpcQueue<T>  where T : struct, RpcCommand
{
    internal int rpcType;
    public unsafe void Schedule(DynamicBuffer<OutgoingRpcDataStreamBufferComponent> buffer, T data)
    {
        DataStreamWriter writer = new DataStreamWriter(128, Allocator.Temp);
        if (buffer.Length == 0)
            writer.Write((byte)NetworkStreamProtocol.Rpc);
        writer.Write(rpcType);
        data.Serialize(writer);
        var prevLen = buffer.Length;
        buffer.ResizeUninitialized(buffer.Length + writer.Length);
        byte* ptr = (byte*)buffer.GetUnsafePtr();
        ptr += prevLen;
        UnsafeUtility.MemCpy(ptr, writer.GetUnsafeReadOnlyPtr(), writer.Length);
    }
}

public struct OutgoingRpcDataStreamBufferComponent : IBufferElementData
{
    public byte Value;
}
public struct IncomingRpcDataStreamBufferComponent : IBufferElementData
{
    public byte Value;
}
[UpdateInGroup(typeof(ClientAndServerSimulationSystemGroup))]
[UpdateAfter(typeof(NetworkStreamReceiveSystem))]
public class RpcSystem : JobComponentSystem
{
    private Type[] m_RpcTypes;
    private ComponentGroup m_RpcBufferGroup;
    private BeginSimulationEntityCommandBufferSystem m_Barrier;
    protected override void OnCreateManager()
    {
        m_RpcTypes = new Type[] {typeof(RpcSetNetworkId), typeof(RpcLoadLevel), typeof(RpcLevelLoaded), typeof(RpcSpawn)};
        #if ENABLE_UNITY_COLLECTIONS_CHECKS
        Debug.Assert(UnsafeUtility.SizeOf<OutgoingRpcDataStreamBufferComponent>() == 1);
        Debug.Assert(UnsafeUtility.SizeOf<IncomingRpcDataStreamBufferComponent>() == 1);
        #endif
        m_Barrier = World.GetOrCreateManager<BeginSimulationEntityCommandBufferSystem>();
        m_RpcBufferGroup = GetComponentGroup(ComponentType.ReadWrite<IncomingRpcDataStreamBufferComponent>());
    }

    struct RpcExecJob : IJobChunk
    {
        public EntityCommandBuffer.Concurrent commandBuffer;
        [ReadOnly] public ArchetypeChunkEntityType entityType;
        public ArchetypeChunkBufferType<IncomingRpcDataStreamBufferComponent> bufferType;
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
                    switch (type)
                    {
                    case 0:
                    {
                        var tmp = new RpcSetNetworkId();
                        tmp.Deserialize(reader, ref ctx);
                        tmp.Execute(entities[i], commandBuffer, chunkIndex);
                        break;
                    }
                    case 1:
                    {
                        var tmp = new RpcLoadLevel();
                        tmp.Deserialize(reader, ref ctx);
                        tmp.Execute(entities[i], commandBuffer, chunkIndex);
                        break;
                    }
                    case 2:
                    {
                        var tmp = new RpcLevelLoaded();
                        tmp.Deserialize(reader, ref ctx);
                        tmp.Execute(entities[i], commandBuffer, chunkIndex);
                        break;
                    }
                    case 3:
                    {
                        var tmp = new RpcSpawn();
                        tmp.Deserialize(reader, ref ctx);
                        tmp.Execute(entities[i], commandBuffer, chunkIndex);
                        break;
                    }
                    }
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
        var handle = execJob.Schedule(m_RpcBufferGroup, inputDeps);
        m_Barrier.AddJobHandleForProducer(handle);
        return handle;
    }

    public RpcQueue<T> GetRpcQueue<T>() where T : struct, RpcCommand
    {
        int t = 0;
        while (t < m_RpcTypes.Length && m_RpcTypes[t] != typeof(T))
            ++t;
        #if ENABLE_UNITY_COLLECTIONS_CHECKS
        if (t >= m_RpcTypes.Length)
            throw new InvalidOperationException("Trying to get a rpc type which is not registered");
        #endif
        return new RpcQueue<T>{rpcType = t};
    }
}

using System;
using System.Threading;
using Unity.Networking.Transport.LowLevel.Unsafe;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace Unity.Networking.Transport
{
    public interface INetworkPipelineSender
    {
        unsafe int Send(NetworkConnection con, network_iovec* iov, int iovLen);
    }
    public interface INetworkPipelineReceiver
    {
        void PushDataEvent(NetworkConnection con, NativeSlice<byte> data);
    }

    public struct InboundBufferVec
    {
        public NativeSlice<byte> buffer1;
        public NativeSlice<byte> buffer2;
    }
    public struct NetworkPipelineContext
    {
        public NativeSlice<byte> internalSharedProcessBuffer;
        public NativeSlice<byte> internalProcessBuffer;
        public DataStreamWriter header;
        public long timestamp;
    }

    public class NetworkPipelineInitilizeAttribute : Attribute
    {
        public NetworkPipelineInitilizeAttribute(Type t)
        {
            m_ParameterType = t;
        }

        public Type ParameterType => m_ParameterType;
        private Type m_ParameterType;
    }
    public interface INetworkPipelineStage
    {
        NativeSlice<byte> Receive(NetworkPipelineContext ctx, NativeSlice<byte> inboundBuffer, ref bool needsResume, ref bool needsUpdate, ref bool needsSendUpdate);
        InboundBufferVec Send(NetworkPipelineContext ctx, InboundBufferVec inboundBuffer, ref bool needsResume, ref bool needsUpdate);

        void InitializeConnection(NativeSlice<byte> sendProcessBuffer, NativeSlice<byte> recvProcessBuffer, NativeSlice<byte> sharedProcessBuffer);

        int ReceiveCapacity { get; }
        int SendCapacity { get; }
        int HeaderCapacity { get; }
        int SharedStateCapacity { get; }
    }

    public interface INetworkPipelineStageCollection
    {
        int GetStageId(Type type);

        void Initialize(params INetworkParameter[] param);
        void InvokeInitialize(int pipelineStageId, NativeSlice<byte> sendProcessBuffer, NativeSlice<byte> recvProcessBuffer, NativeSlice<byte> sharedStateBuffer);
        InboundBufferVec InvokeSend(int pipelineStageId, NetworkPipelineContext ctx, InboundBufferVec inboundBuffer, ref bool needsResume, ref bool needsUpdate);
        NativeSlice<byte> InvokeReceive(int pipelineStageId, NetworkPipelineContext ctx, NativeSlice<byte> inboundBuffer, ref bool needsResume, ref bool needsUpdate, ref bool needsSendUpdate);

        int GetReceiveCapacity(int pipelineStageId);
        int GetSendCapacity(int pipelineStageId);
        int GetHeaderCapacity(int pipelineStageId);
        int GetSharedStateCapacity(int pipelineStageId);
    }

    public struct NetworkPipeline
    {
        internal int Id;
        public static NetworkPipeline Null => default(NetworkPipeline);

        public static bool operator ==(NetworkPipeline lhs, NetworkPipeline rhs)
        {
            return lhs.Id == rhs.Id;
        }

        public static bool operator !=(NetworkPipeline lhs, NetworkPipeline rhs)
        {
            return lhs.Id != rhs.Id;
        }

        public override bool Equals(object compare)
        {
            return this == (NetworkPipeline) compare;
        }

        public override int GetHashCode()
        {
            return Id;
        }

        public bool Equals(NetworkPipeline connection)
        {
            return connection.Id == Id;
        }
    }

    public struct NetworkPipelineParams : INetworkParameter
    {
        public int initialCapacity;
    }

    internal struct NetworkPipelineProcessor<TNetworkPipelineStageCollection> : IDisposable where TNetworkPipelineStageCollection: struct, INetworkPipelineStageCollection
    {
        public Concurrent ToConcurrent()
        {
            var concurrent = new Concurrent
            {
                m_StageCollection = m_StageCollection,
                m_Pipelines = m_Pipelines,
                m_StageList = m_StageList,
                m_SendStageNeedsUpdateWrite = m_SendStageNeedsUpdateRead.ToConcurrent(),
                sizePerConnection = sizePerConnection,
                sendBuffer = m_SendBuffer,
                sharedBuffer = m_SharedBuffer,
                m_timestamp = m_timestamp
            };
            return concurrent;
        }
        public struct Concurrent
        {
            // FIXME: read-only?
            internal TNetworkPipelineStageCollection m_StageCollection;
            [ReadOnly] internal NativeList<PipelineImpl> m_Pipelines;
            [ReadOnly] internal NativeList<int> m_StageList;
            internal NativeQueue<UpdatePipeline>.Concurrent m_SendStageNeedsUpdateWrite;
            [ReadOnly] internal NativeArray<int> sizePerConnection;
            // TODO: not really read-only, just hacking the safety system
            [ReadOnly] internal NativeList<byte> sharedBuffer;
            [ReadOnly] internal NativeList<byte> sendBuffer;
            [ReadOnly] internal NativeArray<long> m_timestamp;

            public unsafe int Send<T>(T driver, NetworkPipeline pipeline, NetworkConnection connection, NativeSlice<byte> payloadData) where T : struct, INetworkPipelineSender
            {
                var p = m_Pipelines[pipeline.Id-1];

                var connectionId = connection.m_NetworkId;
                int startStage = 0;

                // TODO: not really read-only, just hacking the safety system
                NativeArray<byte> tmpBuffer = sendBuffer;
                int* sendBufferLock = (int*) tmpBuffer.GetUnsafeReadOnlyPtr();
                sendBufferLock += connectionId * sizePerConnection[SendSizeOffset] / 4;

                while (Interlocked.CompareExchange(ref *sendBufferLock, 1, 0) != 0)
                {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    throw new InvalidOperationException("The parallel network driver needs to process a single unique connection per job, processing a single connection multiple times in a parallel for is not supported.");
#endif
                }

                NativeList<UpdatePipeline> currentUpdates = new NativeList<UpdatePipeline>(128, Allocator.Temp);
                ProcessPipelineSend(driver, startStage, pipeline, connection, payloadData, currentUpdates);
                // Move the updates requested in this iteration to the concurrent queue so it can be read/parsed in update routine
                for (int i = 0; i < currentUpdates.Length; ++i)
                    m_SendStageNeedsUpdateWrite.Enqueue(currentUpdates[i]);

                Interlocked.Exchange(ref *sendBufferLock, 0);
                return payloadData.Length;
            }

            internal unsafe void ProcessPipelineSend<T>(T driver, int startStage, NetworkPipeline pipeline, NetworkConnection connection,
                NativeSlice<byte> payloadBuffer, NativeList<UpdatePipeline> currentUpdates) where T : struct, INetworkPipelineSender
            {
                NetworkPipelineContext ctx = default(NetworkPipelineContext);
                ctx.timestamp = m_timestamp[0];
                var p = m_Pipelines[pipeline.Id-1];
                var connectionId = connection.m_NetworkId;

                var resumeQ = new NativeList<int>(16, Allocator.Temp);
                int resumeQStart = 0;
                ctx.header = new DataStreamWriter(p.headerCapacity, Allocator.Temp);

                var inboundBuffer = default(InboundBufferVec);
                inboundBuffer.buffer1 = payloadBuffer;

                var prevHeader = new DataStreamWriter(p.headerCapacity, Allocator.Temp);

                while (true)
                {
                    int internalBufferOffset = p.sendBufferOffset + sizePerConnection[SendSizeOffset] * connectionId;
                    int internalSharedBufferOffset = p.sharedBufferOffset + sizePerConnection[SharedSizeOffset] * connectionId;

                    bool needsUpdate = false;
                    // If this is not the first stage we need to fast forward the buffer offset to the correct place
                    if (startStage > 0)
                    {
                        for (int i = 0; i < startStage; ++i)
                        {
                            internalBufferOffset += m_StageCollection.GetSendCapacity(m_StageList[p.FirstStageIndex + i]);
                            internalSharedBufferOffset += m_StageCollection.GetSharedStateCapacity(m_StageList[p.FirstStageIndex + i]);
                        }
                    }

                    for (int i = startStage; i < p.NumStages; ++i)
                    {
                        var prevInbound = inboundBuffer;
                        ProcessSendStage(i, internalBufferOffset, internalSharedBufferOffset, p, ref resumeQ, ref ctx, ref inboundBuffer, ref needsUpdate);
                        if (inboundBuffer.buffer1 == prevInbound.buffer1 &&
                            inboundBuffer.buffer2 == prevInbound.buffer2)
                        {
                            if (ctx.header.Length > 0)
                            {
                                if (prevHeader.Length > 0)
                                    ctx.header.WriteBytes(prevHeader.GetUnsafeReadOnlyPtr(), prevHeader.Length);
                                prevHeader.Clear();
                                var tempHeader = ctx.header;
                                ctx.header = prevHeader;
                                prevHeader = tempHeader;
                                if (inboundBuffer.buffer2.Length == 0)
                                    inboundBuffer.buffer2 = inboundBuffer.buffer1;
                                inboundBuffer.buffer1 = prevHeader.GetNativeSlice(0, prevHeader.Length);
                            }

                        }
                        else
                        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                            if (inboundBuffer.buffer2.Length > 0)
                                throw new InvalidOperationException("Pipeline send stages must return either the unmodified inbound buffers or a consolidated version with a single buffer");
#endif
                            // Prev header is now part of payload
                            prevHeader.Clear();
                            if (ctx.header.Length > 0)
                            {
                                var tempHeader = ctx.header;
                                ctx.header = prevHeader;
                                prevHeader = tempHeader;
                                inboundBuffer.buffer2 = inboundBuffer.buffer1;
                                inboundBuffer.buffer1 = prevHeader.GetNativeSlice(0, prevHeader.Length);
                            }
                        }
                        if (needsUpdate)
                            AddSendUpdate(connection, i, pipeline, currentUpdates);
                        if (inboundBuffer.buffer1.Length == 0)
                            break;

                        needsUpdate = false;

                        internalBufferOffset += ctx.internalProcessBuffer.Length;
                        internalSharedBufferOffset += ctx.internalSharedProcessBuffer.Length;
                    }

                    if (inboundBuffer.buffer1.Length != 0)
                    {
                        var iov = stackalloc network_iovec[4];
                        var pipelineId = pipeline.Id;
                        iov[0].buf = &pipelineId;
                        iov[0].len = 1;
                        iov[1].buf = ctx.header.GetUnsafePtr();
                        iov[1].len = ctx.header.Length;
                        iov[2].buf = inboundBuffer.buffer1.GetUnsafeReadOnlyPtr();
                        iov[2].len = inboundBuffer.buffer1.Length;
                        if (inboundBuffer.buffer2.Length > 0)
                        {
                            iov[3].buf = inboundBuffer.buffer2.GetUnsafeReadOnlyPtr();
                            iov[3].len = inboundBuffer.buffer2.Length;
                            // FIXME: handle send errors
                            driver.Send(connection, iov, 4);
                        }
                        else
                            driver.Send(connection, iov, 3);
                    }

                    if (resumeQStart >= resumeQ.Length)
                    {
                        break;
                    }

                    startStage = resumeQ[resumeQStart++];

                    prevHeader.Clear();
                    inboundBuffer = default(InboundBufferVec);
                }
            }

            internal static unsafe NativeSlice<byte> Unsafe_GetSliceFromReadOnlyArray(NativeArray<byte> array, int offset, int length)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                var handle = AtomicSafetyHandle.Create();
#endif
                var ptr = (void*)((byte*) array.GetUnsafeReadOnlyPtr() + offset);
                var buffer =
                    NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>(ptr, length, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref buffer, handle);
#endif
                return buffer.Slice();
            }

            private void ProcessSendStage(int startStage, int internalBufferOffset, int internalSharedBufferOffset,
                PipelineImpl p, ref NativeList<int> resumeQ, ref NetworkPipelineContext ctx, ref InboundBufferVec inboundBuffer, ref bool needsUpdate)
            {
                bool needsResume = false;
                ctx.internalProcessBuffer = Unsafe_GetSliceFromReadOnlyArray(sendBuffer, internalBufferOffset,
                    m_StageCollection.GetSendCapacity(m_StageList[p.FirstStageIndex + startStage]));

                ctx.internalSharedProcessBuffer =
                    Unsafe_GetSliceFromReadOnlyArray(sharedBuffer, internalSharedBufferOffset,
                        m_StageCollection.GetSharedStateCapacity(m_StageList[p.FirstStageIndex + startStage]));

                inboundBuffer = m_StageCollection.InvokeSend(m_StageList[p.FirstStageIndex + startStage], ctx,
                    inboundBuffer, ref needsResume, ref needsUpdate);
                if (needsResume)
                    resumeQ.Add(startStage);
            }
        }
        private TNetworkPipelineStageCollection m_StageCollection;
        private NativeList<int> m_StageList;
        private NativeList<PipelineImpl> m_Pipelines;
        private NativeList<byte> m_ReceiveBuffer;
        private NativeList<byte> m_SendBuffer;
        private NativeList<byte> m_SharedBuffer;
        private NativeList<UpdatePipeline> m_ReceiveStageNeedsUpdate;
        private NativeList<UpdatePipeline> m_SendStageNeedsUpdate;
        private NativeQueue<UpdatePipeline> m_SendStageNeedsUpdateRead;

        private NativeArray<int> sizePerConnection;

        private NativeArray<long> m_timestamp;

        private const int SendSizeOffset = 0;
        private const int RecveiveSizeOffset = 1;
        private const int SharedSizeOffset = 2;

        internal struct PipelineImpl
        {
            public int FirstStageIndex;
            public int NumStages;

            public int receiveBufferOffset;
            public int sendBufferOffset;
            public int sharedBufferOffset;
            public int headerCapacity;
        }

        public NetworkPipelineProcessor(params INetworkParameter[] param)
        {
            NetworkPipelineParams config = default(NetworkPipelineParams);
            for (int i = 0; i < param.Length; ++i)
            {
                if (param[i] is NetworkPipelineParams)
                    config = (NetworkPipelineParams)param[i];
            }
            m_StageCollection = new TNetworkPipelineStageCollection();
            m_StageCollection.Initialize(param);
            m_StageList = new NativeList<int>(16, Allocator.Persistent);
            m_Pipelines = new NativeList<PipelineImpl>(16, Allocator.Persistent);
            m_ReceiveBuffer = new NativeList<byte>(config.initialCapacity, Allocator.Persistent);
            m_SendBuffer = new NativeList<byte>(config.initialCapacity, Allocator.Persistent);
            m_SharedBuffer = new NativeList<byte>(config.initialCapacity, Allocator.Persistent);
            sizePerConnection = new NativeArray<int>(3, Allocator.Persistent);
            // Store an int for the spinlock first in each connections send buffer
            sizePerConnection[SendSizeOffset] = 4;
            m_ReceiveStageNeedsUpdate = new NativeList<UpdatePipeline>(128, Allocator.Persistent);
            m_SendStageNeedsUpdate = new NativeList<UpdatePipeline>(128, Allocator.Persistent);
            m_SendStageNeedsUpdateRead = new NativeQueue<UpdatePipeline>(Allocator.Persistent);
            m_timestamp = new NativeArray<long>(1, Allocator.Persistent);
        }

        public void Dispose()
        {
            m_StageList.Dispose();
            m_ReceiveBuffer.Dispose();
            m_SendBuffer.Dispose();
            m_SharedBuffer.Dispose();
            m_Pipelines.Dispose();
            sizePerConnection.Dispose();
            m_ReceiveStageNeedsUpdate.Dispose();
            m_SendStageNeedsUpdate.Dispose();
            m_SendStageNeedsUpdateRead.Dispose();
            m_timestamp.Dispose();
        }

        public long Timestamp
        {
            get { return m_timestamp[0]; }
            internal set { m_timestamp[0] = value; }
        }

        public unsafe void initializeConnection(NetworkConnection con)
        {
            var requiredReceiveSize = (con.m_NetworkId + 1) * sizePerConnection[RecveiveSizeOffset];
            var requiredSendSize = (con.m_NetworkId + 1) * sizePerConnection[SendSizeOffset];
            var requiredSharedSize = (con.m_NetworkId + 1) * sizePerConnection[SharedSizeOffset];
            if (m_ReceiveBuffer.Length < requiredReceiveSize)
                m_ReceiveBuffer.ResizeUninitialized(requiredReceiveSize);
            if (m_SendBuffer.Length < requiredSendSize)
                m_SendBuffer.ResizeUninitialized(requiredSendSize);
            if (m_SharedBuffer.Length < requiredSharedSize)
                m_SharedBuffer.ResizeUninitialized(requiredSharedSize);
            
            UnsafeUtility.MemClear((byte*)m_ReceiveBuffer.GetUnsafePtr() + con.m_NetworkId * sizePerConnection[RecveiveSizeOffset], sizePerConnection[RecveiveSizeOffset]);
            UnsafeUtility.MemClear((byte*)m_SendBuffer.GetUnsafePtr() + con.m_NetworkId * sizePerConnection[SendSizeOffset], sizePerConnection[SendSizeOffset]);
            UnsafeUtility.MemClear((byte*)m_SharedBuffer.GetUnsafePtr() + con.m_NetworkId * sizePerConnection[SharedSizeOffset], sizePerConnection[SharedSizeOffset]);

            InitializeStages(con.m_NetworkId);
        }

        void InitializeStages(int networkId)
        {
            var connectionId = networkId;

            for (int i = 0; i < m_Pipelines.Length; i++)
            {
                var pipeline = m_Pipelines[i];

                int recvBufferOffset = pipeline.receiveBufferOffset + sizePerConnection[RecveiveSizeOffset] * connectionId;
                int sendBufferOffset = pipeline.sendBufferOffset + sizePerConnection[SendSizeOffset] * connectionId;
                int sharedBufferOffset = pipeline.sharedBufferOffset + sizePerConnection[SharedSizeOffset] * connectionId;

                for (int stage = pipeline.FirstStageIndex;
                    stage < pipeline.FirstStageIndex + pipeline.NumStages;
                    stage++)
                {
                    var sendProcessBuffer =
                        new NativeSlice<byte>(m_SendBuffer, sendBufferOffset, m_StageCollection.GetSendCapacity(m_StageList[stage]));
                    var recvProcessBuffer = 
                        new NativeSlice<byte>(m_ReceiveBuffer, recvBufferOffset, m_StageCollection.GetReceiveCapacity(m_StageList[stage]));
                    var sharedProcessBuffer =
                        new NativeSlice<byte>(m_SharedBuffer, sharedBufferOffset, m_StageCollection.GetSharedStateCapacity(m_StageList[stage]));

                    m_StageCollection.InvokeInitialize(m_StageList[stage], sendProcessBuffer, recvProcessBuffer, sharedProcessBuffer);

                    sendBufferOffset += sendProcessBuffer.Length;
                    recvBufferOffset += recvProcessBuffer.Length;
                    sharedBufferOffset += sharedProcessBuffer.Length;
                }
            }
        }

        public NetworkPipeline CreatePipeline(params Type[] stages)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (m_Pipelines.Length > 255)
                throw new InvalidOperationException("Cannot create more than 255 pipelines on a single driver");
#endif
            var receiveCap = 0;
            var sharedCap = 0;
            var sendCap = 0;
            var headerCap = 0;
            var pipeline = new PipelineImpl();
            pipeline.FirstStageIndex = m_StageList.Length;
            pipeline.NumStages = stages.Length;
            for (int i = 0; i < stages.Length; i++)
            {
                var stageId = m_StageCollection.GetStageId(stages[i]);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (stageId < 0)
                    throw new InvalidOperationException("Trying to create pipeline with invalid stage " + stages[i]);
#endif
                m_StageList.Add(stageId);
                receiveCap += m_StageCollection.GetReceiveCapacity(stageId);
                sendCap += m_StageCollection.GetSendCapacity(stageId);
                headerCap += m_StageCollection.GetHeaderCapacity(stageId);
                sharedCap += m_StageCollection.GetSharedStateCapacity(stageId);
            }

            // Make sure all data buffers are 4-byte aligned
            receiveCap = (receiveCap + 3) & (~3);
            sendCap = (sendCap + 3) & (~3);
            sharedCap = (sharedCap + 3) & (~3);

            pipeline.receiveBufferOffset = sizePerConnection[RecveiveSizeOffset];
            sizePerConnection[RecveiveSizeOffset] = sizePerConnection[RecveiveSizeOffset] + receiveCap;

            pipeline.sendBufferOffset = sizePerConnection[SendSizeOffset];
            sizePerConnection[SendSizeOffset] = sizePerConnection[SendSizeOffset] + sendCap;

            pipeline.sharedBufferOffset = sizePerConnection[SharedSizeOffset];
            sizePerConnection[SharedSizeOffset] = sizePerConnection[SharedSizeOffset] + sharedCap;

            pipeline.headerCapacity = headerCap;

            m_Pipelines.Add(pipeline);
            return new NetworkPipeline {Id = m_Pipelines.Length};
        }

        public void GetPipelineBuffers(Type pipelineType, NetworkConnection connection, ref NativeSlice<byte> readProcessingBuffer, ref NativeSlice<byte> writeProcessingBuffer, ref NativeSlice<byte> sharedBuffer)
        {
            var stageId = m_StageCollection.GetStageId(pipelineType);

            int pipelineId = 0;
            int stageIndexInList = 0;
            for (pipelineId = 0; pipelineId < m_Pipelines.Length; pipelineId++)
            {
                var pipelineImpl = m_Pipelines[pipelineId];
                for (stageIndexInList = pipelineImpl.FirstStageIndex;
                    stageIndexInList < pipelineImpl.FirstStageIndex + pipelineImpl.NumStages;
                    stageIndexInList++)
                {
                    if (m_StageList[stageIndexInList] == stageId)
                        break;
                }
            }

            GetPipelineBuffers(new NetworkPipeline{Id = pipelineId}, stageId, connection, ref readProcessingBuffer, ref writeProcessingBuffer, ref sharedBuffer);
        }

        public void GetPipelineBuffers(NetworkPipeline pipelineId, int stageId, NetworkConnection connection,
            ref NativeSlice<byte> readProcessingBuffer, ref NativeSlice<byte> writeProcessingBuffer,
            ref NativeSlice<byte> sharedBuffer)
        {
            var pipeline = m_Pipelines[pipelineId.Id-1];

            int recvBufferOffset = pipeline.receiveBufferOffset + sizePerConnection[RecveiveSizeOffset] * connection.InternalId;
            int sendBufferOffset = pipeline.sendBufferOffset + sizePerConnection[SendSizeOffset] * connection.InternalId;
            int sharedBufferOffset = pipeline.sharedBufferOffset + sizePerConnection[SharedSizeOffset] * connection.InternalId;

            int stageIndexInList;
            bool stageNotFound = true;
            for (stageIndexInList = pipeline.FirstStageIndex;
                stageIndexInList < pipeline.FirstStageIndex + pipeline.NumStages;
                stageIndexInList++)
            {
                if (m_StageList[stageIndexInList] == stageId)
                {
                    stageNotFound = false;
                    break;
                }
                sendBufferOffset += m_StageCollection.GetSendCapacity(m_StageList[stageIndexInList]);
                recvBufferOffset += m_StageCollection.GetReceiveCapacity(m_StageList[stageIndexInList]);
                sharedBufferOffset += m_StageCollection.GetSharedStateCapacity(m_StageList[stageIndexInList]);
            }

            if (stageNotFound)
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                throw new InvalidOperationException("Could not find stage ID " + stageId +
                                " make sure the type for this stage ID is added when the pipeline is created.");
#else
                return;
#endif

            writeProcessingBuffer =
                    new NativeSlice<byte>(m_SendBuffer, sendBufferOffset, m_StageCollection.GetSendCapacity(m_StageList[stageIndexInList]));
            readProcessingBuffer =
                new NativeSlice<byte>(m_ReceiveBuffer, recvBufferOffset, m_StageCollection.GetReceiveCapacity(m_StageList[stageIndexInList]));
            sharedBuffer =
                new NativeSlice<byte>(m_SharedBuffer, sharedBufferOffset, m_StageCollection.GetSharedStateCapacity(m_StageList[stageIndexInList]));
        }

        internal struct UpdatePipeline
        {
            public NetworkPipeline pipeline;
            public int stage;
            public NetworkConnection connection;
        }

        internal void UpdateSend<T>(T driver, out int updateCount) where T : struct, INetworkPipelineSender
        {
            NativeArray<UpdatePipeline> sendUpdates = new NativeArray<UpdatePipeline>(m_SendStageNeedsUpdateRead.Count + m_SendStageNeedsUpdate.Length, Allocator.Temp);

            UpdatePipeline updateItem;
            updateCount = 0;
            while (m_SendStageNeedsUpdateRead.TryDequeue(out updateItem))
            {
                sendUpdates[updateCount++] = updateItem;
            }

            int startLength = updateCount;
            for (int i = 0; i < m_SendStageNeedsUpdate.Length; i++)
            {
                sendUpdates[startLength + i] = m_SendStageNeedsUpdate[i];
                updateCount++;
            }

            NativeList<UpdatePipeline> currentUpdates = new NativeList<UpdatePipeline>(128, Allocator.Temp);
            // Move the updates requested in this iteration to the concurrent queue so it can be read/parsed in update routine
            for (int i = 0; i < updateCount; ++i)
            {
                updateItem = sendUpdates[i];
                var inboundBuffer = default(NativeSlice<byte>);
                ToConcurrent().ProcessPipelineSend(driver, updateItem.stage, updateItem.pipeline, updateItem.connection, inboundBuffer, currentUpdates);
            }
            for (int i = 0; i < currentUpdates.Length; ++i)
                m_SendStageNeedsUpdateRead.Enqueue(currentUpdates[i]);
        }

        private static void AddSendUpdate(NetworkConnection connection, int stageId, NetworkPipeline pipelineId, NativeList<UpdatePipeline> currentUpdates)
        {
            var newUpdate = new UpdatePipeline
                {connection = connection, stage = stageId, pipeline = pipelineId};
            bool uniqueItem = true;
            for (int j = 0; j < currentUpdates.Length; ++j)
            {
                if (currentUpdates[j].stage == newUpdate.stage &&
                    currentUpdates[j].pipeline.Id == newUpdate.pipeline.Id &&
                    currentUpdates[j].connection == newUpdate.connection)
                    uniqueItem = false;
            }
            if (uniqueItem)
                currentUpdates.Add(newUpdate);
        }

        public void UpdateReceive<T>(T driver, out int updateCount) where T: struct, INetworkPipelineReceiver
        {
            updateCount = m_ReceiveStageNeedsUpdate.Length;
            NativeArray<UpdatePipeline> receiveUpdates = new NativeArray<UpdatePipeline>(updateCount, Allocator.Temp);

            // Move current update requests to a new queue
            for (int i = 0; i < updateCount; ++i)
                receiveUpdates[i] = m_ReceiveStageNeedsUpdate[i];
            m_ReceiveStageNeedsUpdate.Clear();

            // Process all current requested updates, new update requests will (possibly) be generated from the pipeline stages
            for (int i = 0; i < receiveUpdates.Length; ++i)
            {
                UpdatePipeline updateItem = receiveUpdates[i];
                ProcessReceiveStagesFrom(driver, updateItem.stage, updateItem.pipeline, updateItem.connection, default(NativeSlice<byte>));
            }
        }

        public void Receive<T>(T driver, NetworkConnection connection, NativeSlice<byte> buffer) where T: struct, INetworkPipelineReceiver
        {
            byte pipelineId = buffer[0];
            var p = m_Pipelines[pipelineId-1];
            int startStage = p.NumStages - 1;

            ProcessReceiveStagesFrom(driver, startStage, new NetworkPipeline{Id = pipelineId}, connection, new NativeSlice<byte>(buffer, 1, buffer.Length -1));
        }

        private void ProcessReceiveStagesFrom<T>(T driver, int startStage, NetworkPipeline pipeline, NetworkConnection connection, NativeSlice<byte> buffer) where T: struct, INetworkPipelineReceiver
        {
            var p = m_Pipelines[pipeline.Id-1];
            var connectionId = connection.m_NetworkId;
            var resumeQ = new NativeList<int>(16, Allocator.Temp);
            int resumeQStart = 0;

            NetworkPipelineContext ctx = default(NetworkPipelineContext);
            ctx.timestamp = Timestamp;
            var inboundBuffer = new NativeSlice<byte>(buffer, 0, buffer.Length);
            ctx.header = default(DataStreamWriter);
            NativeList<UpdatePipeline> sendUpdates = new NativeList<UpdatePipeline>(128, Allocator.Temp);

            while (true)
            {
                bool needsUpdate = false;
                bool needsSendUpdate = false;
                int internalBufferOffset = p.receiveBufferOffset + sizePerConnection[RecveiveSizeOffset] * connectionId;
                int internalSharedBufferOffset = p.sharedBufferOffset + sizePerConnection[SharedSizeOffset] * connectionId;

                // Adjust offset accounting for stages in front of the starting stage, since we're parsing the stages in reverse order
                for (int st = 0; st < startStage; ++st)
                {
                    internalBufferOffset += m_StageCollection.GetReceiveCapacity(m_StageList[p.FirstStageIndex+st]);
                    internalSharedBufferOffset += m_StageCollection.GetSharedStateCapacity(m_StageList[p.FirstStageIndex+st]);
                }

                for (int i = startStage; i >= 0; --i)
                {
                    ProcessReceiveStage(i, pipeline, internalBufferOffset, internalSharedBufferOffset, ref ctx, ref inboundBuffer, ref resumeQ, ref needsUpdate, ref needsSendUpdate);
                    if (needsUpdate)
                    {
                        var newUpdate = new UpdatePipeline
                            {connection = connection, stage = i, pipeline = pipeline};
                        bool uniqueItem = true;
                        for (int j = 0; j < m_ReceiveStageNeedsUpdate.Length; ++j)
                        {
                            if (m_ReceiveStageNeedsUpdate[j].stage == newUpdate.stage &&
                                m_ReceiveStageNeedsUpdate[j].pipeline.Id == newUpdate.pipeline.Id &&
                                m_ReceiveStageNeedsUpdate[j].connection == newUpdate.connection)
                                uniqueItem = false;
                        }
                        if (uniqueItem)
                            m_ReceiveStageNeedsUpdate.Add(newUpdate);
                    }

                    if (needsSendUpdate)
                        AddSendUpdate(connection, i, pipeline, m_SendStageNeedsUpdate);

                    if (inboundBuffer.Length == 0)
                        break;

                    // Offset needs to be adjusted for the next pipeline (the one in front of this one)
                    if (i > 0)
                    {
                        internalBufferOffset -=
                            m_StageCollection.GetReceiveCapacity(m_StageList[p.FirstStageIndex + i - 1]);
                        internalSharedBufferOffset -=
                            m_StageCollection.GetSharedStateCapacity(m_StageList[p.FirstStageIndex + i - 1]);
                    }

                    needsUpdate = false;
                }

                if (inboundBuffer.Length != 0)
                    driver.PushDataEvent(connection, inboundBuffer);

                if (resumeQStart >= resumeQ.Length)
                {
                    return;
                }

                startStage = resumeQ[resumeQStart++];
                inboundBuffer = default(NativeSlice<byte>);
            }
        }

        private void ProcessReceiveStage(int stage, NetworkPipeline pipeline, int internalBufferOffset, int internalSharedBufferOffset, ref NetworkPipelineContext ctx, ref NativeSlice<byte> inboundBuffer, ref NativeList<int> resumeQ, ref bool needsUpdate, ref bool needsSendUpdate)
        {
            bool needsResume = false;
            var p = m_Pipelines[pipeline.Id-1];

            ctx.internalProcessBuffer =
                new NativeSlice<byte>(m_ReceiveBuffer, internalBufferOffset, m_StageCollection.GetReceiveCapacity(m_StageList[p.FirstStageIndex+stage]));
            ctx.internalSharedProcessBuffer =
                new NativeSlice<byte>(m_SharedBuffer, internalSharedBufferOffset, m_StageCollection.GetSharedStateCapacity(m_StageList[p.FirstStageIndex+stage]));
            var stageId = m_StageList[p.FirstStageIndex + stage];
            inboundBuffer = m_StageCollection.InvokeReceive(stageId, ctx, inboundBuffer, ref needsResume, ref needsUpdate, ref needsSendUpdate);

            if (needsResume)
                resumeQ.Add(stage);
        }
    }
}

using System;
using System.Diagnostics;
using Unity.Burst;
using Unity.Networking.Transport.LowLevel.Unsafe;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Networking.Transport.Protocols;
using Unity.Jobs;
using UnityEngine.Assertions;
using Random = System.Random;

namespace Unity.Networking.Transport
{
    /// <summary>
    /// The GenericNetworkDriver is an implementation of Virtual Connections over any transport.
    ///
    /// Basic usage:
    /// <code>
    /// var driver = new GenericNetworkDriver&lt;IPv4UDPSocket&gt;(new INetworkParameter[0]);
    /// </code>
    /// </summary>
    /// <typeparam name="T">Should be of type <see cref="INetworkInterface"/>, the currently available NetworkInterfaces
    /// are <see cref="IPv4UDPSocket"/>, or <see cref="IPCSocket"/>.
    /// </typeparam>
    public struct GenericNetworkDriver<T, TNetworkPipelineStageCollection> : INetworkDriver, INetworkPacketReceiver, INetworkPipelineReceiver where T : struct, INetworkInterface where TNetworkPipelineStageCollection : struct, INetworkPipelineStageCollection
    {
        /// <summary>
        /// Create a Concurrent Copy of the NetworkDriver.
        /// </summary>
        public Concurrent ToConcurrent()
        {
            Concurrent concurrent;
            concurrent.m_EventQueue = m_EventQueue.ToConcurrent();
            concurrent.m_ConnectionList = m_ConnectionList;
            concurrent.m_DataStream = m_DataStream;
            concurrent.m_NetworkInterface = m_NetworkInterface;
            concurrent.m_PipelineProcessor = m_PipelineProcessor.ToConcurrent();
            concurrent.m_DefaultHeaderFlags = m_DefaultHeaderFlags;
            concurrent.m_Logger = m_Logger.ToConcurrent();
            return concurrent;
        }

        private Concurrent ToConcurrentSendOnly()
        {
            Concurrent concurrent;
            concurrent.m_EventQueue = default(NetworkEventQueue.Concurrent);
            concurrent.m_ConnectionList = m_ConnectionList;
            concurrent.m_DataStream = default(DataStreamWriter);
            concurrent.m_NetworkInterface = m_NetworkInterface;
            concurrent.m_PipelineProcessor = m_PipelineProcessor.ToConcurrent();
            concurrent.m_DefaultHeaderFlags = m_DefaultHeaderFlags;
            concurrent.m_Logger = m_Logger.ToConcurrent();
            return concurrent;
        }

        /// <summary>
        /// The Concurrent struct is used to create an Concurrent instance of the GenericNetworkDriver.
        /// </summary>
        public struct Concurrent : INetworkPipelineSender
        {
            public NetworkLogger.Concurrent m_Logger;
            public NetworkEvent.Type PopEventForConnection(NetworkConnection connectionId, out DataStreamReader slice)
            {
                int offset, size;
                slice = default(DataStreamReader);
                if (connectionId.m_NetworkId < 0 || connectionId.m_NetworkId >= m_ConnectionList.Length ||
                    m_ConnectionList[connectionId.m_NetworkId].Version != connectionId.m_NetworkVersion)
                    return (int) NetworkEvent.Type.Empty;
                var type = m_EventQueue.PopEventForConnection(connectionId.m_NetworkId, out offset, out size);
                if (size > 0)
                    slice = new DataStreamReader(m_DataStream, offset, size);
                return type;
            }

            public unsafe int Send(NetworkPipeline pipe, NetworkConnection id, DataStreamWriter strm)
            {
                if (strm.IsCreated && strm.Length > 0)
                {
                    return Send(pipe, id, (IntPtr) strm.GetUnsafeReadOnlyPtr(), strm.Length);
                }
                return 0;
            }

            public unsafe int Send(NetworkPipeline pipe, NetworkConnection id, IntPtr data, int len)
            {
                if (pipe.Id > 0)
                {
                    m_DefaultHeaderFlags = UdpCHeader.HeaderFlags.HasPipeline;
                    var slice = NativeSliceUnsafeUtility.ConvertExistingDataToNativeSlice<byte>((void*) data, 1, len);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    var safety = AtomicSafetyHandle.Create();
                    AtomicSafetyHandle.SetAllowSecondaryVersionWriting(safety, false);
                    AtomicSafetyHandle.UseSecondaryVersion(ref safety);
                    NativeSliceUnsafeUtility.SetAtomicSafetyHandle(ref slice, safety);
#endif
                    var retval = m_PipelineProcessor.Send(this, pipe, id, slice);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    AtomicSafetyHandle.Release(safety);
#endif
                    m_DefaultHeaderFlags = 0;
                    return retval;
                }
                if (id.m_NetworkId < 0 || id.m_NetworkId >= m_ConnectionList.Length)
                    return 0;
                var connection = m_ConnectionList[id.m_NetworkId];
                if (connection.Version != id.m_NetworkVersion)
                    return 0;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (connection.State == NetworkConnection.State.Connecting)
                    throw new InvalidOperationException("Cannot send data while connecting");
#endif

                // update last attempt;
                var header = new UdpCHeader
                {
                    Type = (int) UdpCProtocol.Data,
                    SessionToken = connection.SendToken,
                    Flags = m_DefaultHeaderFlags
                };


                var iov = stackalloc network_iovec[3];
                iov[0].buf = header.Data;
                iov[0].len = UdpCHeader.Length;

                iov[1].buf = (void*)data;
                iov[1].len = len;

                iov[2].buf = &connection.ReceiveToken;
                iov[2].len = 2;

                if (connection.DidReceiveData == 0)
                {
                    header.Flags |= UdpCHeader.HeaderFlags.HasConnectToken;
                    return m_NetworkInterface.SendMessage(iov, 3, ref connection.Address);
                }
                return m_NetworkInterface.SendMessage(iov, 2, ref connection.Address);
            }
            public unsafe int Send(NetworkConnection id, network_iovec* dataIov, int dataIovLen)
            {
                if (id.m_NetworkId < 0 || id.m_NetworkId >= m_ConnectionList.Length)
                    return 0;
                var connection = m_ConnectionList[id.m_NetworkId];
                if (connection.Version != id.m_NetworkVersion)
                    return 0;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (connection.State == NetworkConnection.State.Connecting)
                    throw new InvalidOperationException("Cannot send data while connecting");
#endif

                // update last attempt;
                var header = new UdpCHeader
                {
                    Type = (int) UdpCProtocol.Data,
                    SessionToken = connection.SendToken,
                    Flags = m_DefaultHeaderFlags
                };

                var iov = stackalloc network_iovec[2+dataIovLen];
                iov[0].buf = header.Data;
                iov[0].len = UdpCHeader.Length;

                for (int i = 0; i < dataIovLen; ++i)
                    iov[1 + i] = dataIov[i];

                iov[1 + dataIovLen].buf = &connection.ReceiveToken;
                iov[1 + dataIovLen].len = 2;

                if (connection.DidReceiveData == 0)
                {
                    header.Flags |= UdpCHeader.HeaderFlags.HasConnectToken;
                    return m_NetworkInterface.SendMessage(iov, 2 + dataIovLen, ref connection.Address);
                }
                return m_NetworkInterface.SendMessage(iov, 1 + dataIovLen, ref connection.Address);
            }

            internal NetworkEventQueue.Concurrent m_EventQueue;
            [ReadOnly] internal NativeList<Connection> m_ConnectionList;
            [ReadOnly] internal DataStreamWriter m_DataStream;
            internal T m_NetworkInterface;
            internal NetworkPipelineProcessor<TNetworkPipelineStageCollection>.Concurrent m_PipelineProcessor;
            internal UdpCHeader.HeaderFlags m_DefaultHeaderFlags;
        }

        internal struct Connection
        {
            public NetworkEndPoint Address;
            public long LastAttempt;
            public int Id;
            public int Version;
            public int Attempts;
            public NetworkConnection.State State;
            public ushort ReceiveToken;
            public ushort SendToken;
            public byte DidReceiveData;

            public static bool operator ==(Connection lhs, Connection rhs)
            {
                return lhs.Id == rhs.Id && lhs.Version == rhs.Version && lhs.Address == rhs.Address;
            }

            public static bool operator !=(Connection lhs, Connection rhs)
            {
                return lhs.Id != rhs.Id || lhs.Version != rhs.Version || lhs.Address != rhs.Address;
            }

            public override bool Equals(object compare)
            {
                return this == (Connection) compare;
            }

            public static Connection Null => new Connection() {Id = 0, Version = 0};

            public override int GetHashCode()
            {
                return Id;
            }

            public bool Equals(Connection connection)
            {
                return connection.Id == Id && connection.Version == Version && connection.Address == Address;
            }
        }

        // internal variables :::::::::::::::::::::::::::::::::::::::::::::::::
        T m_NetworkInterface;

        NetworkEventQueue m_EventQueue;

        private NetworkLogger m_Logger;

        NativeQueue<int> m_FreeList;
        NativeQueue<int> m_NetworkAcceptQueue;
        NativeList<Connection> m_ConnectionList;
        NativeArray<int> m_InternalState;
        NativeQueue<int> m_PendingFree;
        NativeArray<ushort> m_SessionIdCounter;
        NativeArray<int> m_ErrorCodes;
        enum ErrorCodeType
        {
            ReceiveError = 0,
            NumErrorCodes
        }

#pragma warning disable 649
        struct Parameters
        {
            public NetworkDataStreamParameter dataStream;
            public NetworkConfigParameter config;

            public Parameters(params INetworkParameter[] param)
            {
                config = new NetworkConfigParameter {
                    maxConnectAttempts = NetworkParameterConstants.MaxConnectAttempts,
                    connectTimeoutMS = NetworkParameterConstants.ConnectTimeoutMS,
                    disconnectTimeoutMS = NetworkParameterConstants.DisconnectTimeoutMS
                };
                dataStream = default(NetworkDataStreamParameter);

                for (int i = 0; i < param.Length; ++i)
                {
                    if (param[i] is NetworkConfigParameter)
                        config = (NetworkConfigParameter)param[i];
                    else if (param[i] is NetworkDataStreamParameter)
                        dataStream = (NetworkDataStreamParameter)param[i];
                }
            }
        }
#pragma warning restore 649

        private Parameters m_NetworkParams;
        private DataStreamWriter m_DataStream;
        private NetworkPipelineProcessor<TNetworkPipelineStageCollection> m_PipelineProcessor;
        private UdpCHeader.HeaderFlags m_DefaultHeaderFlags;

        private long m_updateTime;

        // properties :::::::::::::::::::::::::::::::::::::::::::::::::::::::::
        private const int InternalStateListening = 0;
        private const int InternalStateBound = 1;

        public bool Listening
        {
            get { return (m_InternalState[InternalStateListening] != 0); }
            internal set { m_InternalState[InternalStateListening] = value ? 1 : 0; }
        }

        enum StringType
        {
            ReceiveError,
            ResetErrorCount,
            ResetErrorConnection,
            ResetErrorListening,
            PipelineOverflow,
            ConnectionRequestSendError,
            ConnectionAcceptSendError,
            DisconnectSendError,
            ConnectionRequestWithPipeline,
            ConnectionAcceptWithPipeline,
            DisconnectWithPipeline,
            AcceptWithoutToken,
            ImplicitAcceptWithoutToken,
            NumStrings
        }

        private static readonly string[] StringValue =
        {
            "Error on receive ",
            "Resetting event queue with pending events (Count=",
            ", ConnectionID=",
            ") Listening: ",
            "A lot of pipeline updates have been queued, possibly too many being scheduled in pipeline logic, queue count: ",
            "Failed to send a ConnectionRequest package",
            "Failed to send a ConnectionAccept package",
            "Failed to send a Disconnect package",
            "Received an invalid ConnectionRequest with pipeline",
            "Received an invalid ConnectionAccept with pipeline",
            "Received an invalid Disconnect with pipeline",
            "Received an invalid ConnectionAccept without a token",
            "Received an invalid implicit connection accept without a token",
        };

        [ReadOnly] private NativeArray<NetworkLogString> m_StringDB;
        /// <summary>
        /// Constructor for GenericNetworkDriver.
        /// </summary>
        /// <param name="param">
        /// An array of INetworkParameter. There are currently only two <see cref="INetworkParameter"/>,
        /// the <see cref="NetworkDataStreamParameter"/> and the <see cref="NetworkConfigParameter"/>.
        /// </param>
        public GenericNetworkDriver(params INetworkParameter[] param)
        {
            m_Logger = new NetworkLogger(NetworkLogger.LogLevel.Debug);
            m_StringDB = new NativeArray<NetworkLogString>((int)StringType.NumStrings, Allocator.Persistent);
            if (StringValue.Length != (int)StringType.NumStrings)
                throw new InvalidOperationException("Bad string database");
            for (int i = 0; i < (int) StringType.NumStrings; ++i)
            {
                m_StringDB[i] = new NetworkLogString(StringValue[i]);
            }
            m_updateTime = Stopwatch.GetTimestamp() / TimeSpan.TicksPerMillisecond;
            m_NetworkParams = new Parameters(param);

            int initialStreamSize = m_NetworkParams.dataStream.size;
            if (initialStreamSize == 0)
                initialStreamSize = NetworkParameterConstants.DriverDataStreamSize;

            m_DataStream = new DataStreamWriter(initialStreamSize, Allocator.Persistent);
            m_PipelineProcessor = new NetworkPipelineProcessor<TNetworkPipelineStageCollection>(param); // Initial capacity might need to be bigger than 0
            m_DefaultHeaderFlags = 0;

            m_NetworkInterface = new T();
            m_NetworkInterface.Initialize();

            m_NetworkAcceptQueue = new NativeQueue<int>(Allocator.Persistent);

            m_ConnectionList = new NativeList<Connection>(1, Allocator.Persistent);

            m_FreeList = new NativeQueue<int>(Allocator.Persistent);
            m_EventQueue = new NetworkEventQueue(NetworkParameterConstants.InitialEventQueueSize);

            m_InternalState = new NativeArray<int>(2, Allocator.Persistent);
            m_PendingFree = new NativeQueue<int>(Allocator.Persistent);

            m_ReceiveCount = new NativeArray<int>(1, Allocator.Persistent);
            m_SessionIdCounter = new NativeArray<ushort>(1, Allocator.Persistent);
            m_SessionIdCounter[0] = (ushort)(new Random().Next() & 0xFFFF);
            m_ErrorCodes = new NativeArray<int>((int)ErrorCodeType.NumErrorCodes, Allocator.Persistent);
            ReceiveCount = 0;
            Listening = false;
        }

        // interface implementation :::::::::::::::::::::::::::::::::::::::::::
        public void Dispose()
        {
            m_Logger.Dispose();
            m_StringDB.Dispose();
            m_NetworkInterface.Dispose();
            m_DataStream.Dispose();
            m_PipelineProcessor.Dispose();

            m_EventQueue.Dispose();

            m_NetworkAcceptQueue.Dispose();
            m_ConnectionList.Dispose();
            m_FreeList.Dispose();
            m_InternalState.Dispose();
            m_PendingFree.Dispose();
            m_ReceiveCount.Dispose();
            m_SessionIdCounter.Dispose();
            m_ErrorCodes.Dispose();
        }

        public bool IsCreated => m_InternalState.IsCreated;

        [BurstCompile]
        struct UpdateJob : IJob
        {
            public GenericNetworkDriver<T, TNetworkPipelineStageCollection> driver;

            public void Execute()
            {
                driver.InternalUpdate();
            }
        }
        struct LogJob : IJob
        {
            public NetworkLogger logger;

            public void Execute()
            {
                logger.FlushPending();
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                logger.DumpToConsole();
#else
                logger.Clear();
#endif
            }
        }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        struct MissingClearMessage : INetworkLogMessage
        {
            public NativeArray<NetworkLogString> stringDB;
            public int count;
            public int connection;
            public int listening;
            public void Print(ref NetworkLogString msg)
            {
                var str = stringDB[(int) StringType.ResetErrorCount];
                msg.Append(ref str);
                msg.AppendInt(count);
                str = stringDB[(int) StringType.ResetErrorConnection];
                msg.Append(ref str);
                msg.AppendInt(connection);
                str = stringDB[(int) StringType.ResetErrorListening];
                msg.Append(ref str);
                msg.AppendInt(listening);
            }
        }
#endif
        struct ClearEventQueue : IJob
        {
            public DataStreamWriter dataStream;
            public NetworkEventQueue eventQueue;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            public NetworkLogger logger;
            public NativeArray<NetworkLogString> stringDB;
            [ReadOnly] public NativeList<Connection> connectionList;
            [ReadOnly] public NativeArray<int> internalState;
#endif
            public void Execute()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                for (int i = 0; i < connectionList.Length; ++i)
                {
                    int conCount = eventQueue.GetCountForConnection(i);
                    if (conCount != 0 && connectionList[i].State != NetworkConnection.State.Disconnected)
                    {
                        var msg = new MissingClearMessage { stringDB = stringDB, count = conCount, connection = i, listening = internalState[InternalStateListening]};
                        logger.Log(NetworkLogger.LogLevel.Error, msg);
                    }
                }
#endif
                eventQueue.Clear();
                dataStream.Clear();
            }
        }

        public JobHandle ScheduleUpdate(JobHandle dep = default(JobHandle))
        {
            m_updateTime = Stopwatch.GetTimestamp() / TimeSpan.TicksPerMillisecond;
            var job = new UpdateJob {driver = this};
            JobHandle handle;
            var clearJob = new ClearEventQueue
            {
                dataStream = m_DataStream,
                eventQueue = m_EventQueue,
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                logger = m_Logger,
                stringDB = m_StringDB,
                connectionList = m_ConnectionList,
                internalState = m_InternalState
#endif
            };
            handle = clearJob.Schedule(dep);
            handle = job.Schedule(handle);
            handle = m_NetworkInterface.ScheduleReceive(this, handle);
            var logJob = new LogJob {logger = m_Logger};
            handle = logJob.Schedule(handle);
            return handle;
        }

        struct PipelineOverflowMessage : INetworkLogMessage
        {
            public NativeArray<NetworkLogString> stringDB;
            public int count;
            public void Print(ref NetworkLogString msg)
            {
                var str = stringDB[(int)StringType.PipelineOverflow];
                msg.Append(ref str);
                msg.AppendInt(count);
            }
        }
        void InternalUpdate()
        {
            m_PipelineProcessor.Timestamp = m_updateTime;
            int free;
            while (m_PendingFree.TryDequeue(out free))
            {
                int ver = m_ConnectionList[free].Version + 1;
                if (ver == 0)
                    ver = 1;
                m_ConnectionList[free] = new Connection {Id = free, Version = ver};
                m_FreeList.Enqueue(free);
            }

            CheckTimeouts();

            int updateCount;
            m_PipelineProcessor.UpdateReceive(this, out updateCount);

            // TODO: Find a good way to establish a good limit (connections*pipelines/2?)
            if (updateCount > 64)
            {
                var msg = new PipelineOverflowMessage { stringDB = m_StringDB, count = updateCount };
                m_Logger.Log(NetworkLogger.LogLevel.Warning, msg);
            }

            m_DefaultHeaderFlags = UdpCHeader.HeaderFlags.HasPipeline;
            m_PipelineProcessor.UpdateSend(ToConcurrentSendOnly(), out updateCount);
            if (updateCount > 64)
            {
                var msg = new PipelineOverflowMessage { stringDB = m_StringDB, count = updateCount };
                m_Logger.Log(NetworkLogger.LogLevel.Warning, msg);
            }

            m_DefaultHeaderFlags = 0;
        }

        public NetworkPipeline CreatePipeline(params Type[] stages)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!m_InternalState.IsCreated)
                throw new InvalidOperationException(
                    "Driver must be constructed with a populated or empty INetworkParameter params list");
            if (m_ConnectionList.Length > 0)
                throw new InvalidOperationException(
                    "Pipelines cannot be created after establishing connections");
#endif
            return m_PipelineProcessor.CreatePipeline(stages);
        }

        public int Bind(NetworkEndPoint endpoint)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!m_InternalState.IsCreated)
                throw new InvalidOperationException(
                    "Driver must be constructed with a populated or empty INetworkParameter params list");
            if (m_InternalState[InternalStateBound] != 0)
                throw new InvalidOperationException(
                    "Bind can only be called once per NetworkDriver");
            if (m_ConnectionList.Length > 0)
                throw new InvalidOperationException(
                    "Bind cannot be called after establishing connections");
#endif
            var ret = m_NetworkInterface.Bind(endpoint);
            if (ret == 0)
                m_InternalState[InternalStateBound] = 1;
            return ret;
        }

        public int Listen()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!m_InternalState.IsCreated)
                throw new InvalidOperationException(
                    "Driver must be constructed with a populated or empty INetworkParameter params list");
            if (Listening)
                throw new InvalidOperationException(
                    "Listen can only be called once per NetworkDriver");
            if (m_InternalState[InternalStateBound] == 0)
                throw new InvalidOperationException(
                    "Listen can only be called after a successful call to Bind");
#endif
            if (m_InternalState[InternalStateBound] == 0)
                return -1;
            Listening = true;
            return 0;
        }

        public NetworkConnection Accept()
        {
            if (!Listening)
                return default(NetworkConnection);

            int id;
            if (!m_NetworkAcceptQueue.TryDequeue(out id))
                return default(NetworkConnection);
            return new NetworkConnection {m_NetworkId = id, m_NetworkVersion = m_ConnectionList[id].Version};
        }

        public NetworkConnection Connect(NetworkEndPoint endpoint)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!m_InternalState.IsCreated)
                throw new InvalidOperationException(
                    "Driver must be constructed with a populated or empty INetworkParameter params list");
#endif
            int id;
            if (!m_FreeList.TryDequeue(out id))
            {
                id = m_ConnectionList.Length;
                m_ConnectionList.Add(new Connection{Id = id, Version = 1});
            }

            int ver = m_ConnectionList[id].Version;
            var c = new Connection
            {
                Id = id,
                Version = ver,
                State = NetworkConnection.State.Connecting,
                Address = endpoint,
                Attempts = 1,
                LastAttempt = m_updateTime,
                SendToken = 0,
                ReceiveToken = m_SessionIdCounter[0]
            };
            m_SessionIdCounter[0] = (ushort)(m_SessionIdCounter[0] + 1);

            m_ConnectionList[id] = c;
            var netcon = new NetworkConnection {m_NetworkId = id, m_NetworkVersion = ver};
            SendConnectionRequest(c);
            m_PipelineProcessor.initializeConnection(netcon);

            return netcon;
        }

        void SendConnectionRequest(Connection c)
        {
            var header = new UdpCHeader
            {
                Type = (int) UdpCProtocol.ConnectionRequest,
                SessionToken = c.ReceiveToken,
                Flags = m_DefaultHeaderFlags
            };

            unsafe
            {
                network_iovec iov;
                iov.buf = header.Data;
                iov.len = UdpCHeader.Length;
                if (m_NetworkInterface.SendMessage(&iov, 1, ref c.Address) <= 0)
                {
                    m_Logger.Log(NetworkLogger.LogLevel.Error, m_StringDB[(int)StringType.ConnectionRequestSendError]);
                }
            }
        }

        public int Disconnect(NetworkConnection id)
        {
            Connection connection;
            if ((connection = GetConnection(id)) == Connection.Null)
                return 0;

            if (connection.State == NetworkConnection.State.Connected)
            {
                if (SendPacket(UdpCProtocol.Disconnect, id) <= 0)
                {
                    m_Logger.Log(NetworkLogger.LogLevel.Error, m_StringDB[(int)StringType.DisconnectSendError]);
                }
            }
            RemoveConnection(connection);

            return 0;
        }

        public void GetPipelineBuffers(Type pipelineType, NetworkConnection connection, ref NativeSlice<byte> readProcessingBuffer, ref NativeSlice<byte> writeProcessingBuffer, ref NativeSlice<byte> sharedBuffer)
        {
            m_PipelineProcessor.GetPipelineBuffers(pipelineType, connection, ref readProcessingBuffer, ref writeProcessingBuffer, ref sharedBuffer);
        }

        public void GetPipelineBuffers(NetworkPipeline pipeline, int stageId, NetworkConnection connection, ref NativeSlice<byte> readProcessingBuffer, ref NativeSlice<byte> writeProcessingBuffer, ref NativeSlice<byte> sharedBuffer)
        {
            m_PipelineProcessor.GetPipelineBuffers(pipeline, stageId, connection, ref readProcessingBuffer, ref writeProcessingBuffer, ref sharedBuffer);
        }

        public NetworkConnection.State GetConnectionState(NetworkConnection con)
        {
            Connection connection;
            if ((connection = GetConnection(con)) == Connection.Null)
                return NetworkConnection.State.Disconnected;
            return connection.State;
        }

        public NetworkEndPoint RemoteEndPoint(NetworkConnection id)
        {
            if (id == default(NetworkConnection))
                return m_NetworkInterface.RemoteEndPoint;

            Connection connection;
            if ((connection = GetConnection(id)) == Connection.Null)
                return default(NetworkEndPoint);
            return connection.Address;
        }

        public NetworkEndPoint LocalEndPoint()
        {
            return m_NetworkInterface.LocalEndPoint;
        }

        public int Send(NetworkPipeline pipe, NetworkConnection id, DataStreamWriter strm)
        {
            unsafe
            {
                return Send(pipe, id, (IntPtr) strm.GetUnsafeReadOnlyPtr(), strm.Length);
            }
        }

        public unsafe int Send(NetworkPipeline pipe, NetworkConnection id, IntPtr data, int len)
        {
            return ToConcurrentSendOnly().Send(pipe, id, data, len);
        }

        public NetworkEvent.Type PopEvent(out NetworkConnection con, out DataStreamReader slice)
        {
            int offset, size;
            slice = default(DataStreamReader);
            int id;
            var type = m_EventQueue.PopEvent(out id, out offset, out size);
            if (size > 0)
                slice = new DataStreamReader(m_DataStream, offset, size);
            con = id < 0
                ? default(NetworkConnection)
                : new NetworkConnection {m_NetworkId = id, m_NetworkVersion = m_ConnectionList[id].Version};
            return type;
        }

        public NetworkEvent.Type PopEventForConnection(NetworkConnection connectionId, out DataStreamReader slice)
        {
            int offset, size;
            slice = default(DataStreamReader);
            if (connectionId.m_NetworkId < 0 || connectionId.m_NetworkId >= m_ConnectionList.Length ||
                 m_ConnectionList[connectionId.m_NetworkId].Version != connectionId.m_NetworkVersion)
                return (int) NetworkEvent.Type.Empty;
            var type = m_EventQueue.PopEventForConnection(connectionId.m_NetworkId, out offset, out size);
            if (size > 0)
                slice = new DataStreamReader(m_DataStream, offset, size);
            return type;
        }

        // internal helper functions ::::::::::::::::::::::::::::::::::::::::::
        void AddConnection(int id)
        {
            m_EventQueue.PushEvent(new NetworkEvent {connectionId = id, type = NetworkEvent.Type.Connect});
        }

        void AddDisconnection(int id)
        {
            m_EventQueue.PushEvent(new NetworkEvent {connectionId = id, type = NetworkEvent.Type.Disconnect});
        }

        Connection GetConnection(NetworkConnection id)
        {
            var con = m_ConnectionList[id.m_NetworkId];
            if (con.Version != id.m_NetworkVersion)
                return Connection.Null;
            return con;
        }

        Connection GetConnection(NetworkEndPoint address, ushort sessionId)
        {
            for (int i = 0; i < m_ConnectionList.Length; i++)
            {
                if (address == m_ConnectionList[i].Address && m_ConnectionList[i].ReceiveToken == sessionId )
                    return m_ConnectionList[i];
            }

            return Connection.Null;
        }

        Connection GetNewConnection(NetworkEndPoint address, ushort sessionId)
        {
            for (int i = 0; i < m_ConnectionList.Length; i++)
            {
                if (address == m_ConnectionList[i].Address && m_ConnectionList[i].SendToken == sessionId )
                    return m_ConnectionList[i];
            }

            return Connection.Null;
        }

        void SetConnection(Connection connection)
        {
            m_ConnectionList[connection.Id] = connection;
        }

        bool RemoveConnection(Connection connection)
        {
            if (connection.State != NetworkConnection.State.Disconnected && connection == m_ConnectionList[connection.Id])
            {
                connection.State = NetworkConnection.State.Disconnected;
                m_ConnectionList[connection.Id] = connection;
                m_PendingFree.Enqueue(connection.Id);

                return true;
            }

            return false;
        }

        bool UpdateConnection(Connection connection)
        {
            if (connection == m_ConnectionList[connection.Id])
            {
                SetConnection(connection);
                return true;
            }

            return false;
        }

        unsafe int SendPacket(UdpCProtocol type, Connection connection)
        {
            var header = new UdpCHeader
            {
                Type = (byte) type,
                SessionToken = connection.SendToken,
                Flags = m_DefaultHeaderFlags
            };

            var iov = stackalloc network_iovec[2];
            iov[0].buf = header.Data;
            iov[0].len = UdpCHeader.Length;
            iov[1].buf = &connection.ReceiveToken;
            iov[1].len = 2;
            if (connection.DidReceiveData == 0)
            {
                header.Flags |= UdpCHeader.HeaderFlags.HasConnectToken;
                return m_NetworkInterface.SendMessage(iov, 2, ref connection.Address);
            }
            return m_NetworkInterface.SendMessage(iov, 1, ref connection.Address);
        }

        int SendPacket(UdpCProtocol type, NetworkConnection id)
        {
            Connection connection;
            if ((connection = GetConnection(id)) == Connection.Null)
                return 0;

            return SendPacket(type, connection);
        }

        void CheckTimeouts()
        {
            for (int i = 0; i < m_ConnectionList.Length; ++i)
            {
                var connection = m_ConnectionList[i];
                if (connection == Connection.Null)
                    continue;

                long now = m_updateTime;

                var netcon = new NetworkConnection {m_NetworkId = connection.Id, m_NetworkVersion = connection.Version};
                if ((connection.State == NetworkConnection.State.Connecting ||
                     connection.State == NetworkConnection.State.AwaitingResponse) &&
                    now - connection.LastAttempt > m_NetworkParams.config.connectTimeoutMS)
                {
                    if (connection.Attempts >= m_NetworkParams.config.maxConnectAttempts)
                    {
                        RemoveConnection(connection);
                        AddDisconnection(connection.Id);
                        continue;
                    }

                    connection.Attempts = ++connection.Attempts;
                    connection.LastAttempt = now;
                    SetConnection(connection);

                    if (connection.State == NetworkConnection.State.Connecting)
                        SendConnectionRequest(connection);
                    else
                    {
                        if (SendPacket(UdpCProtocol.ConnectionAccept, netcon) <= 0)
                        {
                            m_Logger.Log(NetworkLogger.LogLevel.Error, m_StringDB[(int)StringType.ConnectionAcceptSendError]);
                        }
                    }
                }

                if (connection.State == NetworkConnection.State.Connected &&
                    now - connection.LastAttempt > m_NetworkParams.config.disconnectTimeoutMS)
                {
                    Disconnect(netcon);
                    AddDisconnection(connection.Id);
                }
            }
        }

        public DataStreamWriter GetDataStream()
        {
            return m_DataStream;
        }

        struct ReceiveErrorMessage : INetworkLogMessage
        {
            public NativeArray<NetworkLogString> stringDB;
            public int errorCode;
            public void Print(ref NetworkLogString msg)
            {
                var str = stringDB[(int) StringType.ReceiveError];
                msg.Append(ref str);
                msg.AppendInt(errorCode);
            }
        }
        public int ReceiveErrorCode
        {
            get { return m_ErrorCodes[(int)ErrorCodeType.ReceiveError]; }
            set
            {
                if (value != 0)
                {
                    var msg = new ReceiveErrorMessage {stringDB = m_StringDB, errorCode = value};
                    m_Logger.Log(NetworkLogger.LogLevel.Error, msg);
                }

                m_ErrorCodes[(int)ErrorCodeType.ReceiveError] = value;
            }
        }

        private NativeArray<int> m_ReceiveCount;
        public int ReceiveCount {
            get { return m_ReceiveCount[0]; }
            set { m_ReceiveCount[0] = value; }
        }

        public bool DynamicDataStreamSize()
        {
            return m_NetworkParams.dataStream.size == 0;
        }

        public unsafe int AppendPacket(NetworkEndPoint address, UdpCHeader header, int dataLen)
        {
            int count = 0;
            switch ((UdpCProtocol) header.Type)
            {
                case UdpCProtocol.ConnectionRequest:
                {
                    if (!Listening)
                        return 0;
                    if ((header.Flags&UdpCHeader.HeaderFlags.HasPipeline) != 0)
                    {
                        m_Logger.Log(NetworkLogger.LogLevel.Error, m_StringDB[(int)StringType.ConnectionRequestWithPipeline]);
                        return 0;
                    }

                    Connection c;
                    if ((c = GetNewConnection(address, header.SessionToken)) == Connection.Null || c.State == NetworkConnection.State.Disconnected)
                    {
                        int id;
                        var sessionId = m_SessionIdCounter[0];
                        m_SessionIdCounter[0] = (ushort) (m_SessionIdCounter[0] + 1);
                        if (!m_FreeList.TryDequeue(out id))
                        {
                            id = m_ConnectionList.Length;
                            m_ConnectionList.Add(new Connection{Id = id, Version = 1});
                        }

                        int ver = m_ConnectionList[id].Version;
                        c = new Connection
                        {
                            Id = id,
                            Version = ver,
                            ReceiveToken = sessionId,
                            SendToken = header.SessionToken,
                            State = NetworkConnection.State.Connected,
                            Address = address,
                            Attempts = 1,
                            LastAttempt = m_updateTime
                        };
                        SetConnection(c);
                        m_PipelineProcessor.initializeConnection(new NetworkConnection{m_NetworkId = id, m_NetworkVersion = c.Version});
                        m_NetworkAcceptQueue.Enqueue(id);
                        count++;
                    }
                    else
                    {
                        c.Attempts++;
                        c.LastAttempt = m_updateTime;
                        SetConnection(c);
                    }

                    if (SendPacket(UdpCProtocol.ConnectionAccept,
                            new NetworkConnection {m_NetworkId = c.Id, m_NetworkVersion = c.Version}) <= 0)
                    {
                        m_Logger.Log(NetworkLogger.LogLevel.Error, m_StringDB[(int)StringType.ConnectionAcceptSendError]);
                    }
                }
                    break;
                case UdpCProtocol.ConnectionReject:
                {
                    // m_EventQ.Enqueue(Id, (int)NetworkEvent.Connect);
                }
                    break;
                case UdpCProtocol.ConnectionAccept:
                {
                    if ((header.Flags&UdpCHeader.HeaderFlags.HasConnectToken) == 0)
                    {
                        m_Logger.Log(NetworkLogger.LogLevel.Error, m_StringDB[(int)StringType.AcceptWithoutToken]);
                        return 0;
                    }
                    if ((header.Flags&UdpCHeader.HeaderFlags.HasPipeline) != 0)
                    {
                        m_Logger.Log(NetworkLogger.LogLevel.Error, m_StringDB[(int)StringType.ConnectionAcceptWithPipeline]);
                        return 0;
                    }

                    Connection c = GetConnection(address, header.SessionToken);
                    if (c != Connection.Null)
                    {
                        c.DidReceiveData = 1;

                        if (c.State == NetworkConnection.State.Connected)
                        {
                            //DebugLog("Dropping connect request for an already connected endpoint [" + address + "]");
                            return 0;
                        }

                        if (c.State == NetworkConnection.State.Connecting)
                        {
                            var sliceOffset = m_DataStream.Length;
                            m_DataStream.WriteBytesWithUnsafePointer(2);
                            var dataStreamReader = new DataStreamReader(m_DataStream, sliceOffset, 2);
                            var context = default(DataStreamReader.Context);
                            c.SendToken = dataStreamReader.ReadUShort(ref context);
                            m_DataStream.WriteBytesWithUnsafePointer(-2);

                            c.State = NetworkConnection.State.Connected;
                            UpdateConnection(c);
                            AddConnection(c.Id);
                            count++;
                        }
                    }
                }
                    break;
                case UdpCProtocol.Disconnect:
                {
                    if ((header.Flags&UdpCHeader.HeaderFlags.HasPipeline) != 0)
                    {
                        m_Logger.Log(NetworkLogger.LogLevel.Error, m_StringDB[(int)StringType.DisconnectWithPipeline]);
                        return 0;
                    }
                    Connection c = GetConnection(address, header.SessionToken);
                    if (c != Connection.Null)
                    {
                        if (RemoveConnection(c))
                            AddDisconnection(c.Id);
                        count++;
                    }
                }
                    break;
                case UdpCProtocol.Data:
                {
                    Connection c = GetConnection(address, header.SessionToken);
                    if (c == Connection.Null)
                        return 0;

                    c.DidReceiveData = 1;
                    c.LastAttempt = m_updateTime;
                    UpdateConnection(c);

                    var length = dataLen - UdpCHeader.Length;

                    if (c.State == NetworkConnection.State.Connecting)
                    {
                        if ((header.Flags&UdpCHeader.HeaderFlags.HasConnectToken) == 0)
                        {
                            m_Logger.Log(NetworkLogger.LogLevel.Error, m_StringDB[(int)StringType.ImplicitAcceptWithoutToken]);
                            return 0;
                        }

                        var tokenOffset = m_DataStream.Length + length - 2;
                        m_DataStream.WriteBytesWithUnsafePointer(length);
                        var dataStreamReader = new DataStreamReader(m_DataStream, tokenOffset, 2);
                        var context = default(DataStreamReader.Context);
                        c.SendToken = dataStreamReader.ReadUShort(ref context);
                        m_DataStream.WriteBytesWithUnsafePointer(-length);

                        c.State = NetworkConnection.State.Connected;
                        UpdateConnection(c);
                        Assert.IsTrue(!Listening);
                        AddConnection(c.Id);
                        count++;
                    }

                    if ((header.Flags&UdpCHeader.HeaderFlags.HasConnectToken) != 0)
                        length -= 2;

                    var sliceOffset = m_DataStream.Length;
                    m_DataStream.WriteBytesWithUnsafePointer(length);

                    if ((header.Flags&UdpCHeader.HeaderFlags.HasPipeline) != 0)
                    {
                        var netCon = new NetworkConnection {m_NetworkId = c.Id, m_NetworkVersion = c.Version};
                        m_PipelineProcessor.Receive(this, netCon, m_DataStream.GetNativeSlice(sliceOffset, length));
                        return 0;
                    }
                    m_EventQueue.PushEvent(new NetworkEvent
                    {
                        connectionId = c.Id,
                        type = NetworkEvent.Type.Data,
                        offset = sliceOffset,
                        size = length
                    });
                    count++;
                } break;
            }

            return count;
        }

        public unsafe void PushDataEvent(NetworkConnection con, NativeSlice<byte> data)
        {
            byte* dataPtr = (byte*)data.GetUnsafeReadOnlyPtr();
            byte* streamBasePtr = m_DataStream.GetUnsafePtr();
            int sliceOffset = 0;
            if (dataPtr >= streamBasePtr && dataPtr + data.Length <= streamBasePtr + m_DataStream.Length)
            {
                // Pointer is a subset of our receive buffer, no need to copy
                sliceOffset = (int)(dataPtr - streamBasePtr);
            }
            else
            {
                if (DynamicDataStreamSize())
                {
                    while (m_DataStream.Length + data.Length >= m_DataStream.Capacity)
                        m_DataStream.Capacity *= 2;
                }
                else if (m_DataStream.Length + data.Length >= m_DataStream.Capacity)
                    return; // FIXME: how do we signal this error?

                sliceOffset = m_DataStream.Length;
                UnsafeUtility.MemCpy(streamBasePtr + sliceOffset, dataPtr, data.Length);
                m_DataStream.WriteBytesWithUnsafePointer(data.Length);
            }

            m_EventQueue.PushEvent(new NetworkEvent
            {
                connectionId = con.m_NetworkId,
                type = NetworkEvent.Type.Data,
                offset = sliceOffset,
                size = data.Length
            });
        }
    }

    public struct UdpNetworkDriver : INetworkDriver
    {
        private GenericNetworkDriver<IPv4UDPSocket, DefaultPipelineStageCollection> m_genericDriver;

        public struct Concurrent
        {
            private GenericNetworkDriver<IPv4UDPSocket, DefaultPipelineStageCollection>.Concurrent m_genericConcurrent;

            internal Concurrent(GenericNetworkDriver<IPv4UDPSocket, DefaultPipelineStageCollection>.Concurrent genericConcurrent)
            {
                m_genericConcurrent = genericConcurrent;
            }
            public NetworkEvent.Type PopEventForConnection(NetworkConnection connectionId, out DataStreamReader slice)
            {
                return m_genericConcurrent.PopEventForConnection(connectionId, out slice);
            }

            public int Send(NetworkPipeline pipe, NetworkConnection id, DataStreamWriter strm)
            {
                return m_genericConcurrent.Send(pipe, id, strm);
            }

            public int Send(NetworkPipeline pipe, NetworkConnection id, IntPtr data, int len)
            {
                return m_genericConcurrent.Send(pipe, id, data, len);
            }
        }

        public Concurrent ToConcurrent()
        {
            return new Concurrent(m_genericDriver.ToConcurrent());
        }

        public UdpNetworkDriver(params INetworkParameter[] param)
        {
            m_genericDriver = new GenericNetworkDriver<IPv4UDPSocket, DefaultPipelineStageCollection>(param);
        }
        public bool IsCreated => m_genericDriver.IsCreated;
        public void Dispose()
        {
            m_genericDriver.Dispose();
        }

        public JobHandle ScheduleUpdate(JobHandle dep = default(JobHandle))
        {
            return m_genericDriver.ScheduleUpdate(dep);
        }

        public int ReceiveErrorCode => m_genericDriver.ReceiveErrorCode;

        public int Bind(NetworkEndPoint endpoint)
        {
            return m_genericDriver.Bind(endpoint);
        }

        public int Listen()
        {
            return m_genericDriver.Listen();
        }

        public bool Listening => m_genericDriver.Listening;

        public NetworkConnection Accept()
        {
            return m_genericDriver.Accept();
        }

        public NetworkConnection Connect(NetworkEndPoint endpoint)
        {
            return m_genericDriver.Connect(endpoint);
        }

        public int Disconnect(NetworkConnection con)
        {
            return m_genericDriver.Disconnect(con);
        }

        public NetworkConnection.State GetConnectionState(NetworkConnection con)
        {
            return m_genericDriver.GetConnectionState(con);
        }

        public void GetPipelineBuffers(Type pipelineType, NetworkConnection connection, ref NativeSlice<byte> readProcessingBuffer, ref NativeSlice<byte> writeProcessingBuffer, ref NativeSlice<byte> sharedBuffer)
        {
            m_genericDriver.GetPipelineBuffers(pipelineType, connection, ref readProcessingBuffer, ref writeProcessingBuffer, ref sharedBuffer);
        }

        public void GetPipelineBuffers(NetworkPipeline pipeline, int stageId, NetworkConnection connection,
            ref NativeSlice<byte> readProcessingBuffer, ref NativeSlice<byte> writeProcessingBuffer,
            ref NativeSlice<byte> sharedBuffer)
        {
            m_genericDriver.GetPipelineBuffers(pipeline, stageId, connection, ref readProcessingBuffer, ref writeProcessingBuffer, ref sharedBuffer);
        }

        public NetworkEndPoint RemoteEndPoint(NetworkConnection con)
        {
            return m_genericDriver.RemoteEndPoint(con);
        }

        public NetworkEndPoint LocalEndPoint()
        {
            return m_genericDriver.LocalEndPoint();
        }

        public NetworkPipeline CreatePipeline(params Type[] stages)
        {
            return m_genericDriver.CreatePipeline(stages);
        }

        public int Send(NetworkPipeline pipe, NetworkConnection con, DataStreamWriter strm)
        {
            return m_genericDriver.Send(pipe, con, strm);
        }

        public int Send(NetworkPipeline pipe, NetworkConnection con, IntPtr data, int len)
        {
            return m_genericDriver.Send(pipe, con, data, len);
        }

        public NetworkEvent.Type PopEvent(out NetworkConnection con, out DataStreamReader bs)
        {
            return m_genericDriver.PopEvent(out con, out bs);
        }

        public NetworkEvent.Type PopEventForConnection(NetworkConnection con, out DataStreamReader bs)
        {
            return m_genericDriver.PopEventForConnection(con, out bs);
        }
    }
    public struct LocalNetworkDriver : INetworkDriver
    {
        private GenericNetworkDriver<IPCSocket, DefaultPipelineStageCollection> m_genericDriver;

        public struct Concurrent
        {
            private GenericNetworkDriver<IPCSocket, DefaultPipelineStageCollection>.Concurrent m_genericConcurrent;

            internal Concurrent(GenericNetworkDriver<IPCSocket, DefaultPipelineStageCollection>.Concurrent genericConcurrent)
            {
                m_genericConcurrent = genericConcurrent;
            }
            public NetworkEvent.Type PopEventForConnection(NetworkConnection connectionId, out DataStreamReader slice)
            {
                return m_genericConcurrent.PopEventForConnection(connectionId, out slice);
            }

            public int Send(NetworkPipeline pipe, NetworkConnection id, DataStreamWriter strm)
            {
                return m_genericConcurrent.Send(pipe, id, strm);
            }

            public int Send(NetworkPipeline pipe, NetworkConnection id, IntPtr data, int len)
            {
                return m_genericConcurrent.Send(pipe, id, data, len);
            }
        }

        public Concurrent ToConcurrent()
        {
            return new Concurrent(m_genericDriver.ToConcurrent());
        }

        public LocalNetworkDriver(params INetworkParameter[] param)
        {
            m_genericDriver = new GenericNetworkDriver<IPCSocket, DefaultPipelineStageCollection>(param);
        }
        public bool IsCreated => m_genericDriver.IsCreated;
        public void Dispose()
        {
            m_genericDriver.Dispose();
        }

        public JobHandle ScheduleUpdate(JobHandle dep = default(JobHandle))
        {
            return m_genericDriver.ScheduleUpdate(dep);
        }

        public int ReceiveErrorCode => m_genericDriver.ReceiveErrorCode;

        public int Bind(NetworkEndPoint endpoint)
        {
            return m_genericDriver.Bind(endpoint);
        }

        public int Listen()
        {
            return m_genericDriver.Listen();
        }

        public bool Listening => m_genericDriver.Listening;

        public NetworkConnection Accept()
        {
            return m_genericDriver.Accept();
        }

        public NetworkConnection Connect(NetworkEndPoint endpoint)
        {
            return m_genericDriver.Connect(endpoint);
        }

        public int Disconnect(NetworkConnection con)
        {
            return m_genericDriver.Disconnect(con);
        }

        public NetworkConnection.State GetConnectionState(NetworkConnection con)
        {
            return m_genericDriver.GetConnectionState(con);
        }

        public void GetPipelineBuffers(Type pipelineType, NetworkConnection connection,
            ref NativeSlice<byte> readProcessingBuffer, ref NativeSlice<byte> writeProcessingBuffer,
            ref NativeSlice<byte> sharedBuffer)
        {
            m_genericDriver.GetPipelineBuffers(pipelineType, connection, ref readProcessingBuffer,
                ref writeProcessingBuffer, ref sharedBuffer);
        }

        public void GetPipelineBuffers(NetworkPipeline pipeline, int stageId, NetworkConnection connection, ref NativeSlice<byte> readProcessingBuffer, ref NativeSlice<byte> writeProcessingBuffer, ref NativeSlice<byte> sharedBuffer)
        {
            m_genericDriver.GetPipelineBuffers(pipeline, stageId, connection, ref readProcessingBuffer, ref writeProcessingBuffer, ref sharedBuffer);
        }

        public NetworkEndPoint RemoteEndPoint(NetworkConnection con)
        {
            return m_genericDriver.RemoteEndPoint(con);
        }

        public NetworkEndPoint LocalEndPoint()
        {
            return m_genericDriver.LocalEndPoint();
        }

        public NetworkPipeline CreatePipeline(params Type[] stages)
        {
            return m_genericDriver.CreatePipeline(stages);
        }

        public int Send(NetworkPipeline pipe, NetworkConnection con, DataStreamWriter strm)
        {
            return m_genericDriver.Send(pipe, con, strm);
        }

        public int Send(NetworkPipeline pipe, NetworkConnection con, IntPtr data, int len)
        {
            return m_genericDriver.Send(pipe, con, data, len);
        }

        public NetworkEvent.Type PopEvent(out NetworkConnection con, out DataStreamReader bs)
        {
            return m_genericDriver.PopEvent(out con, out bs);
        }

        public NetworkEvent.Type PopEventForConnection(NetworkConnection con, out DataStreamReader bs)
        {
            return m_genericDriver.PopEventForConnection(con, out bs);
        }
    }

}

using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Networking.Transport;
using Unity.Networking.Transport.LowLevel.Unsafe;
using Unity.Networking.Transport.Utilities;

public enum NetworkStreamProtocol
{
    Command,
    Snapshot,
    Rpc
}

/** A connection is represented by an entity having a NetworkStreamConnection.
 * If the entity does not have a NetworkIdComponent it is to be considered connecting.
 * It is possible to add more tags to signal the state of the connection, for example
 * adding an InGame component to signal loading being complete.
 *
 * In addition to these components all connections have a set of incoming and outgoing
 * buffers associated with them.
 */
public struct NetworkStreamConnection : IComponentData
{
    public NetworkConnection Value;
}

public struct NetworkSnapshotAck : IComponentData
{
    public void UpdateReceivedByRemote(uint tick, uint mask)
    {
        if (LastReceivedSnapshotByRemote == 0)
        {
            ReceivedSnapshotByRemoteMask0 = mask;
            LastReceivedSnapshotByRemote = tick;
        }
        else if (SequenceHelpers.IsNewer(tick, LastReceivedSnapshotByRemote))
        {
            // TODO: this assumes the delta between acks is less than 64
            int shamt = (int)(tick - LastReceivedSnapshotByRemote);
            ReceivedSnapshotByRemoteMask3 = (ReceivedSnapshotByRemoteMask3 << shamt) |
                                            (ReceivedSnapshotByRemoteMask2 >> (64 - shamt));
            ReceivedSnapshotByRemoteMask2 = (ReceivedSnapshotByRemoteMask2 << shamt) |
                                            (ReceivedSnapshotByRemoteMask1 >> (64 - shamt));
            ReceivedSnapshotByRemoteMask1 = (ReceivedSnapshotByRemoteMask1 << shamt) |
                                            (ReceivedSnapshotByRemoteMask0 >> (64 - shamt));
            ReceivedSnapshotByRemoteMask0 = (ReceivedSnapshotByRemoteMask0 << shamt) |
                                            mask;
            LastReceivedSnapshotByRemote = tick;
        }
    }

    public bool IsReceivedByRemote(uint tick)
    {
        if (tick == 0 || LastReceivedSnapshotByRemote == 0)
            return false;
        if (SequenceHelpers.IsNewer(tick, LastReceivedSnapshotByRemote))
            return false;
        int bit = (int)(LastReceivedSnapshotByRemote - tick);
        if (bit >= 256)
            return false;
        if (bit >= 192)
        {
            bit -= 192;
            return (ReceivedSnapshotByRemoteMask3 & (1ul << bit)) != 0;
        }
        if (bit >= 128)
        {
            bit -= 128;
            return (ReceivedSnapshotByRemoteMask2 & (1ul << bit)) != 0;
        }
        if (bit >= 64)
        {
            bit -= 64;
            return (ReceivedSnapshotByRemoteMask1 & (1ul << bit)) != 0;
        }
        return (ReceivedSnapshotByRemoteMask0 & (1ul << bit)) != 0;
    }
    public uint LastReceivedSnapshotByRemote;
    private ulong ReceivedSnapshotByRemoteMask0;
    private ulong ReceivedSnapshotByRemoteMask1;
    private ulong ReceivedSnapshotByRemoteMask2;
    private ulong ReceivedSnapshotByRemoteMask3;
    public uint LastReceivedSnapshotByLocal;
    public uint ReceivedSnapshotByLocalMask;

    public void UpdateRemoteTime(uint remoteTime, uint localTimeMinusRTT, uint localTime)
    {
        if (remoteTime != 0 && SequenceHelpers.IsNewer(remoteTime, LastReceivedRemoteTime))
        {
            LastReceivedRemoteTime = remoteTime;
            LastReceivedRTT = localTime - localTimeMinusRTT;
            LastReceiveTimestamp = localTime;
        }
    }
    public uint LastReceivedRemoteTime;
    public uint LastReceivedRTT;
    public uint LastReceiveTimestamp;
}

public struct NetworkStreamDisconnected : IComponentData
{
}

public struct NetworkIdComponent : IComponentData
{
    public int Value;
}

public struct RpcSetNetworkId : RpcCommand
{
    public int nid;
    public void Execute(Entity connection, EntityCommandBuffer.Concurrent commandBuffer, int jobIndex)
    {
        commandBuffer.AddComponent(jobIndex, connection, new NetworkIdComponent {Value = nid});
    }

    public void Serialize(DataStreamWriter writer)
    {
        writer.Write(nid);
    }

    public void Deserialize(DataStreamReader reader, ref DataStreamReader.Context ctx)
    {
        nid = reader.ReadInt(ref ctx);
    }
}

public struct OutgoingCommandDataStreamBufferComponent : IBufferElementData
{
    public byte Value;
}
public struct IncomingCommandDataStreamBufferComponent : IBufferElementData
{
    public byte Value;
}

public struct OutgoingSnapshotDataStreamBufferComponent : IBufferElementData
{
    public byte Value;
}
public struct IncomingSnapshotDataStreamBufferComponent : IBufferElementData
{
    public byte Value;
}

[UpdateInGroup(typeof(ClientAndServerSimulationSystemGroup))]
[AlwaysUpdateSystem]
public class NetworkStreamReceiveSystem : JobComponentSystem
{
    public UdpNetworkDriver Driver => m_Driver;
    internal UdpNetworkDriver.Concurrent ConcurrentDriver => m_ConcurrentDriver;

    public NetworkPipeline UnreliablePipeline => m_UnreliablePipeline;
    public NetworkPipeline ReliablePipeline => m_ReliablePipeline;

    private UdpNetworkDriver m_Driver;
    private UdpNetworkDriver.Concurrent m_ConcurrentDriver;
    private NetworkPipeline m_UnreliablePipeline;
    private NetworkPipeline m_ReliablePipeline;
    private bool m_DriverListening;
    private NativeArray<int> numNetworkIds;
    private NativeQueue<int> freeNetworkIds;
    private BeginSimulationEntityCommandBufferSystem m_Barrier;
    private RpcQueue<RpcSetNetworkId> rpcQueue;
    #if UNITY_EDITOR
    private int m_ClientPacketDelay;
    private int m_ClientPacketDrop;
    #endif
    public bool Listen(NetworkEndPoint endpoint)
    {
        if (m_UnreliablePipeline == NetworkPipeline.Null)
            m_UnreliablePipeline = m_Driver.CreatePipeline(typeof(NullPipelineStage));
        if (m_ReliablePipeline == NetworkPipeline.Null)
            m_ReliablePipeline = m_Driver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
        // Switching to server mode
        if (m_Driver.Bind(endpoint) != 0)
            return false;
        if (m_Driver.Listen() != 0)
            return false;
        m_DriverListening = true;
        // FIXME: Bind breaks all copies of the driver nad makes them send to the wrong socket
        m_ConcurrentDriver = m_Driver.ToConcurrent();
        return true;
    }

    public Entity Connect(NetworkEndPoint endpoint)
    {
        if (m_UnreliablePipeline == NetworkPipeline.Null)
        {
            #if UNITY_EDITOR
            if (m_ClientPacketDelay > 0 || m_ClientPacketDrop > 0)
                m_UnreliablePipeline = m_Driver.CreatePipeline(typeof(SimulatorPipelineStage), typeof(SimulatorPipelineStageInSend));
            else
            #endif
            {
                m_UnreliablePipeline = m_Driver.CreatePipeline(typeof(NullPipelineStage));
            }
        }
        if (m_ReliablePipeline == NetworkPipeline.Null)
        {
            #if UNITY_EDITOR
            if (m_ClientPacketDelay > 0 || m_ClientPacketDrop > 0)
                m_ReliablePipeline = m_Driver.CreatePipeline(typeof(SimulatorPipelineStageInSend), typeof(ReliableSequencedPipelineStage), typeof(SimulatorPipelineStage));
            else
            #endif
            {
                m_ReliablePipeline = m_Driver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
            }
        }
        var ent = EntityManager.CreateEntity();
        EntityManager.AddComponentData(ent, new NetworkStreamConnection {Value = m_Driver.Connect(endpoint)});
        EntityManager.AddComponentData(ent, new NetworkSnapshotAck());
        EntityManager.AddBuffer<IncomingRpcDataStreamBufferComponent>(ent);
        EntityManager.AddBuffer<OutgoingRpcDataStreamBufferComponent>(ent);
        EntityManager.AddBuffer<IncomingCommandDataStreamBufferComponent>(ent);
        EntityManager.AddBuffer<OutgoingCommandDataStreamBufferComponent>(ent);
        EntityManager.AddBuffer<IncomingSnapshotDataStreamBufferComponent>(ent);
        EntityManager.AddBuffer<OutgoingSnapshotDataStreamBufferComponent>(ent);
        return ent;
    }

    protected override void OnCreateManager()
    {
        var reliabilityParams = new ReliableUtility.Parameters {WindowSize = 32};

        #if UNITY_EDITOR
        m_ClientPacketDelay = UnityEditor.EditorPrefs.GetInt("MultiplayerPlayMode_" + UnityEngine.Application.productName + "_ClientDelay");
        m_ClientPacketDrop = UnityEditor.EditorPrefs.GetInt("MultiplayerPlayMode_" + UnityEngine.Application.productName + "_ClientDropRate");
        int networkRate = 60; // TODO: read from some better place
        // All 3 packet types every frame stored for maximum delay, doubled for safety margin
        int maxPackets = 2*(networkRate * 3 * m_ClientPacketDelay + 999) / 1000;
        var simulatorParams = new SimulatorUtility.Parameters
            {MaxPacketSize = NetworkParameterConstants.MTU, MaxPacketCount = maxPackets, PacketDelayMs = m_ClientPacketDelay, PacketDropPercentage = m_ClientPacketDrop};
        m_Driver = new UdpNetworkDriver(simulatorParams, reliabilityParams);
        #else
        m_Driver = new UdpNetworkDriver(reliabilityParams);
        #endif

        m_ConcurrentDriver = m_Driver.ToConcurrent();
        m_UnreliablePipeline = NetworkPipeline.Null;
        m_ReliablePipeline = NetworkPipeline.Null;
        m_DriverListening = false;
        m_Barrier = World.GetOrCreateManager<BeginSimulationEntityCommandBufferSystem>();
        numNetworkIds = new NativeArray<int>(1, Allocator.Persistent);
        freeNetworkIds = new NativeQueue<int>(Allocator.Persistent);
        rpcQueue = World.GetOrCreateManager<RpcSystem>().GetRpcQueue<RpcSetNetworkId>();
    }

    protected override void OnDestroyManager()
    {
        numNetworkIds.Dispose();
        freeNetworkIds.Dispose();
        m_Driver.Dispose();
    }

    struct ConnectionAcceptJob : IJob
    {
        public EntityCommandBuffer commandBuffer;
        public UdpNetworkDriver driver;
        public void Execute()
        {
            NetworkConnection con;
            while ((con = driver.Accept()) != default(NetworkConnection))
            {
                // New connection can never have any events, if this one does - just close it
                DataStreamReader reader;
                if (con.PopEvent(driver, out reader) != NetworkEvent.Type.Empty)
                {
                    con.Disconnect(driver);
                    continue;
                }

                // create an entity for the new connection
                var ent = commandBuffer.CreateEntity();
                commandBuffer.AddComponent(ent, new NetworkStreamConnection {Value = con});
                commandBuffer.AddComponent(ent, new NetworkSnapshotAck());
                commandBuffer.AddBuffer<IncomingRpcDataStreamBufferComponent>(ent);
                commandBuffer.AddBuffer<OutgoingRpcDataStreamBufferComponent>(ent);
                commandBuffer.AddBuffer<IncomingCommandDataStreamBufferComponent>(ent);
                commandBuffer.AddBuffer<OutgoingCommandDataStreamBufferComponent>(ent);
                commandBuffer.AddBuffer<IncomingSnapshotDataStreamBufferComponent>(ent);
                commandBuffer.AddBuffer<OutgoingSnapshotDataStreamBufferComponent>(ent);
            }
        }
    }

    [ExcludeComponent(typeof(NetworkIdComponent))]
    struct AssignNetworkIdJob : IJobProcessComponentDataWithEntity<NetworkStreamConnection>
    {
        public EntityCommandBuffer commandBuffer;
        public NativeArray<int> numNetworkId;
        public NativeQueue<int> freeNetworkIds;
        public RpcQueue<RpcSetNetworkId> rpcQueue;
        public BufferFromEntity<OutgoingRpcDataStreamBufferComponent> rpcBuffer;
        public void Execute(Entity entity, int index, [ReadOnly] ref NetworkStreamConnection connection)
        {
            if (!connection.Value.IsCreated)
                return;
            // Send RPC - assign network id
            int nid;
            if (!freeNetworkIds.TryDequeue(out nid))
            {
                // Avoid using 0
                nid = numNetworkId[0] + 1;
                numNetworkId[0] = nid;
            }
            commandBuffer.AddComponent(entity, new NetworkIdComponent {Value = nid});
            rpcQueue.Schedule(rpcBuffer[entity], new RpcSetNetworkId {nid = nid});
        }
    }

    [ExcludeComponent(typeof(NetworkStreamDisconnected))]
    struct ConnectionReceiveJob : IJobProcessComponentDataWithEntity<NetworkStreamConnection, NetworkSnapshotAck>
    {
        public EntityCommandBuffer.Concurrent commandBuffer;
        public UdpNetworkDriver.Concurrent driver;
        public NativeQueue<int>.Concurrent freeNetworkIds;
        [ReadOnly] public ComponentDataFromEntity<NetworkIdComponent> networkId;
        public BufferFromEntity<IncomingRpcDataStreamBufferComponent> rpcBuffer;
        public BufferFromEntity<IncomingCommandDataStreamBufferComponent> cmdBuffer;
        public BufferFromEntity<IncomingSnapshotDataStreamBufferComponent> snapshotBuffer;
        public uint localTime;
        public unsafe void Execute(Entity entity, int index, ref NetworkStreamConnection connection, ref NetworkSnapshotAck snapshotAck)
        {
            if (!connection.Value.IsCreated)
                return;
            DataStreamReader reader;
            NetworkEvent.Type evt;
            cmdBuffer[entity].Clear();
            snapshotBuffer[entity].Clear();
            while ((evt = driver.PopEventForConnection(connection.Value, out reader)) != NetworkEvent.Type.Empty)
            {
                switch (evt)
                {
                case NetworkEvent.Type.Connect:
                    break;
                case NetworkEvent.Type.Disconnect:
                    // Flag the connection as lost, it will be deleted in a separate system, giving user code one frame to detect and respond to lost connection
                    commandBuffer.AddComponent(index, entity, new NetworkStreamDisconnected());
                    rpcBuffer[entity].Clear();
                    cmdBuffer[entity].Clear();
                    connection.Value = default(NetworkConnection);
                    if (networkId.Exists(entity))
                        freeNetworkIds.Enqueue(networkId[entity].Value);
                    return;
                case NetworkEvent.Type.Data:
                    // FIXME: do something with the data
                    var ctx = default(DataStreamReader.Context);
                    switch ((NetworkStreamProtocol)reader.ReadByte(ref ctx))
                    {
                    case NetworkStreamProtocol.Command:
                    {
                        var buffer = cmdBuffer[entity];
                        // FIXME: should be handle by a custom command stream system
                        uint snapshot = reader.ReadUInt(ref ctx);
                        uint snapshotMask = reader.ReadUInt(ref ctx);
                        snapshotAck.UpdateReceivedByRemote(snapshot, snapshotMask);
                        uint remoteTime = reader.ReadUInt(ref ctx);
                        uint localTimeMinusRTT = reader.ReadUInt(ref ctx);
                        snapshotAck.UpdateRemoteTime(remoteTime, localTimeMinusRTT, localTime);

                        int headerSize = 1 + 4 * 4;

                        buffer.ResizeUninitialized(reader.Length - headerSize);
                        UnsafeUtility.MemCpy(buffer.GetUnsafePtr(),
                            reader.GetUnsafeReadOnlyPtr() + headerSize,
                            reader.Length - headerSize);
                        break;
                    }
                    case NetworkStreamProtocol.Snapshot:
                    {
                        uint remoteTime = reader.ReadUInt(ref ctx);
                        uint localTimeMinusRTT = reader.ReadUInt(ref ctx);
                        snapshotAck.UpdateRemoteTime(remoteTime, localTimeMinusRTT, localTime);
                        int headerSize = 1 + 4 * 2;

                        var buffer = snapshotBuffer[entity];
                        buffer.ResizeUninitialized(reader.Length - headerSize);
                        UnsafeUtility.MemCpy(buffer.GetUnsafePtr(),
                            reader.GetUnsafeReadOnlyPtr() + headerSize,
                            reader.Length - headerSize);
                        break;
                    }
                    case NetworkStreamProtocol.Rpc:
                    {
                        var buffer = rpcBuffer[entity];
                        var oldLen = buffer.Length;
                        buffer.ResizeUninitialized(oldLen + reader.Length - 1);
                        UnsafeUtility.MemCpy(((byte*) buffer.GetUnsafePtr()) + oldLen,
                            reader.GetUnsafeReadOnlyPtr() + 1,
                            reader.Length - 1);
                        break;
                    }
                    default:
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                        throw new InvalidOperationException("Received unknown message type");
#else
                        break;
#endif
                    }
                    break;
                default:
                    #if ENABLE_UNITY_COLLECTIONS_CHECKS
                    throw new InvalidOperationException("Received unknown network event " + evt);
                    #else
                    break;
                    #endif
                }
            }
        }
    }

    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        var concurrentFreeQueue = freeNetworkIds.ToConcurrent();
        inputDeps = m_Driver.ScheduleUpdate(inputDeps);
        if (m_DriverListening)
        {
            // Schedule accept job
            var acceptJob = new ConnectionAcceptJob();
            acceptJob.driver = m_Driver;
            acceptJob.commandBuffer = m_Barrier.CreateCommandBuffer();
            inputDeps = acceptJob.Schedule(inputDeps);

            // Schedule job to assign network ids to new connections
            var assignJob = new AssignNetworkIdJob();
            assignJob.commandBuffer = m_Barrier.CreateCommandBuffer();
            assignJob.numNetworkId = numNetworkIds;
            assignJob.freeNetworkIds = freeNetworkIds;
            assignJob.rpcQueue = rpcQueue;
            assignJob.rpcBuffer = GetBufferFromEntity<OutgoingRpcDataStreamBufferComponent>();
            inputDeps = assignJob.ScheduleSingle(this, inputDeps);
        }
        else
        {
            freeNetworkIds.Clear();
        }
        // Schedule parallel update job
        var recvJob = new ConnectionReceiveJob();
        recvJob.commandBuffer = m_Barrier.CreateCommandBuffer().ToConcurrent();
        recvJob.driver = m_ConcurrentDriver;
        recvJob.freeNetworkIds = concurrentFreeQueue;
        recvJob.networkId = GetComponentDataFromEntity<NetworkIdComponent>();
        recvJob.rpcBuffer = GetBufferFromEntity<IncomingRpcDataStreamBufferComponent>();
        recvJob.cmdBuffer = GetBufferFromEntity<IncomingCommandDataStreamBufferComponent>();
        recvJob.snapshotBuffer = GetBufferFromEntity<IncomingSnapshotDataStreamBufferComponent>();
        recvJob.localTime = NetworkTimeSystem.TimestampMS;
        // FIXME: because it uses buffer from entity
        var handle = recvJob.ScheduleSingle(this, inputDeps);
        m_Barrier.AddJobHandleForProducer(handle);
        return handle;
    }
}

[UpdateInGroup(typeof(ClientAndServerSimulationSystemGroup))]
public class NetworkStreamCloseSystem : JobComponentSystem
{
    private BeginSimulationEntityCommandBufferSystem m_Barrier;
    [RequireComponentTag(typeof(NetworkStreamDisconnected))]
    struct CloseJob : IJobProcessComponentDataWithEntity<NetworkStreamConnection>
    {
        public EntityCommandBuffer commandBuffer;
        public void Execute(Entity entity, int index, [ReadOnly] ref NetworkStreamConnection con)
        {
            commandBuffer.DestroyEntity(entity);
        }
    }

    protected override void OnCreateManager()
    {
        m_Barrier = World.GetOrCreateManager<BeginSimulationEntityCommandBufferSystem>();
    }

    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        var job = new CloseJob{commandBuffer = m_Barrier.CreateCommandBuffer()};
        var handle = job.ScheduleSingle(this, inputDeps);
        m_Barrier.AddJobHandleForProducer(handle);
        return handle;
    }
}

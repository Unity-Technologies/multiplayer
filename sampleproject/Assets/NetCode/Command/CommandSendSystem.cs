using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Networking.Transport;
using Unity.Networking.Transport.LowLevel.Unsafe;

[UpdateInGroup(typeof(ClientSimulationSystemGroup))]
// dependency just for acking
[UpdateAfter(typeof(GhostReceiveSystemGroup))]
public class CommandSendSystem<TCommandData> : JobComponentSystem
    where TCommandData : struct, ICommandData<TCommandData>
{
    [ExcludeComponent(typeof(NetworkStreamDisconnected))]
    struct CommandSendJob : IJobForEachWithEntity<CommandTargetComponent>
    {
        public UdpNetworkDriver.Concurrent driver;
        public NetworkPipeline unreliablePipeline;
        public ComponentDataFromEntity<NetworkStreamConnection> connectionFromEntity;
        public ComponentDataFromEntity<NetworkSnapshotAckComponent> ackSnapshot;
        public BufferFromEntity<TCommandData> inputFromEntity;
        public uint localTime;
        public uint inputTargetTick;
        public unsafe void Execute(Entity entity, int index, [ReadOnly] ref CommandTargetComponent state)
        {
            DataStreamWriter writer = new DataStreamWriter(128, Allocator.Temp);
            var ack = ackSnapshot[entity];
            writer.Write((byte)NetworkStreamProtocol.Command);
            writer.Write(ack.LastReceivedSnapshotByLocal);
            writer.Write(ack.ReceivedSnapshotByLocalMask);
            writer.Write(localTime);
            writer.Write(ack.LastReceivedRemoteTime - (localTime - ack.LastReceiveTimestamp));
            if (state.targetEntity != Entity.Null && inputFromEntity.Exists(state.targetEntity))
            {
                var input = inputFromEntity[state.targetEntity];
                TCommandData inputData;
                if (input.GetDataAtTick(inputTargetTick, out inputData) && inputData.Tick == inputTargetTick)
                {
                    writer.Write(inputTargetTick);
                    inputData.Serialize(writer);
                }
            }

            driver.Send(unreliablePipeline, connectionFromEntity[entity].Value, writer);
        }
    }

    private NetworkStreamReceiveSystem m_ReceiveSystem;
    protected override void OnCreateManager()
    {
        m_ReceiveSystem = World.GetOrCreateSystem<NetworkStreamReceiveSystem>();
    }
    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        var sendJob = new CommandSendJob
        {
            driver = m_ReceiveSystem.ConcurrentDriver,
            unreliablePipeline = m_ReceiveSystem.UnreliablePipeline,
            connectionFromEntity = GetComponentDataFromEntity<NetworkStreamConnection>(),
            ackSnapshot = GetComponentDataFromEntity<NetworkSnapshotAckComponent>(),
            inputFromEntity = GetBufferFromEntity<TCommandData>(),
            localTime = NetworkTimeSystem.TimestampMS,
            inputTargetTick = NetworkTimeSystem.predictTargetTick
        };

        return sendJob.ScheduleSingle(this, inputDeps);
    }
}

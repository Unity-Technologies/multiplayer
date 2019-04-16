using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Networking.Transport;
using UnityEngine;

[UpdateInGroup(typeof(ClientAndServerSimulationSystemGroup))]
public class NetworkStreamSendSystem : JobComponentSystem
{
    private NetworkStreamReceiveSystem m_ReceiveSystem;
    protected override void OnCreateManager()
    {
        m_ReceiveSystem = World.GetOrCreateManager<NetworkStreamReceiveSystem>();
    }

    [BurstCompile]
    [ExcludeComponent(typeof(NetworkStreamDisconnected))]
    struct SendJob : IJobProcessComponentDataWithEntity<NetworkStreamConnection>
    {
        public UdpNetworkDriver.Concurrent driver;
        public NetworkPipeline unreliablePipeline;
        public NetworkPipeline reliablePipeline;
        public BufferFromEntity<OutgoingRpcDataStreamBufferComponent> rpcBufferFromEntity;
        public BufferFromEntity<OutgoingCommandDataStreamBufferComponent> cmdBufferFromEntity;
        public BufferFromEntity<OutgoingSnapshotDataStreamBufferComponent> snapshotBufferFromEntity;
        public unsafe void Execute(Entity entity, int index, ref NetworkStreamConnection connection)
        {
            if (!connection.Value.IsCreated)
                return;
            var buffer = rpcBufferFromEntity[entity];
            if (buffer.Length > 0)
            {
                DataStreamWriter tmp = new DataStreamWriter(buffer.Length, Allocator.Temp);
                tmp.WriteBytes((byte*) buffer.GetUnsafePtr(), buffer.Length);
                driver.Send(reliablePipeline, connection.Value, tmp);
                buffer.Clear();
            }

            var cmdBuffer = cmdBufferFromEntity[entity];
            if (cmdBuffer.Length > 0)
            {
                DataStreamWriter tmp = new DataStreamWriter(cmdBuffer.Length, Allocator.Temp);
                tmp.WriteBytes((byte*) cmdBuffer.GetUnsafePtr(), cmdBuffer.Length);
                driver.Send(unreliablePipeline, connection.Value, tmp);
                cmdBuffer.Clear();
            }

            var snapBuffer = snapshotBufferFromEntity[entity];
            if (snapBuffer.Length > 0)
            {
                DataStreamWriter tmp = new DataStreamWriter(snapBuffer.Length, Allocator.Temp);
                tmp.WriteBytes((byte*) snapBuffer.GetUnsafePtr(), snapBuffer.Length);
                driver.Send(unreliablePipeline, connection.Value, tmp);
                snapBuffer.Clear();
            }
        }
    }

    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        var sendJob = new SendJob();
        sendJob.driver = m_ReceiveSystem.ConcurrentDriver;
        sendJob.unreliablePipeline = m_ReceiveSystem.UnreliablePipeline;
        sendJob.reliablePipeline = m_ReceiveSystem.ReliablePipeline;
        sendJob.rpcBufferFromEntity = GetBufferFromEntity<OutgoingRpcDataStreamBufferComponent>();
        sendJob.cmdBufferFromEntity = GetBufferFromEntity<OutgoingCommandDataStreamBufferComponent>();
        sendJob.snapshotBufferFromEntity = GetBufferFromEntity<OutgoingSnapshotDataStreamBufferComponent>();
        // FIXME: because the job gets buffer from entity
        return sendJob.ScheduleSingle(this, inputDeps);
    }
}

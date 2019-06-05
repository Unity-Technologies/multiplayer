using Unity.Entities;
using Unity.Jobs;
using Unity.Networking.Transport;
using Unity.Networking.Transport.LowLevel.Unsafe;
using Unity.Collections;

[UpdateInGroup(typeof(ServerSimulationSystemGroup))]
[UpdateAfter(typeof(NetworkStreamReceiveSystem))]
public class CommandReceiveSystem<TCommandData> : JobComponentSystem
    where TCommandData : struct, ICommandData<TCommandData>
{
    struct ReceiveJob : IJobForEachWithEntity<CommandTargetComponent>
    {
        public BufferFromEntity<TCommandData> commandData;
        public BufferFromEntity<IncomingCommandDataStreamBufferComponent> cmdBuffer;
        public unsafe void Execute(Entity entity, int index, [ReadOnly] ref CommandTargetComponent commandTarget)
        {
            if (commandTarget.targetEntity == Entity.Null)
            {
                return;
            }

            var buffer = cmdBuffer[entity];
            if (buffer.Length == 0)
                return;
            DataStreamReader reader = DataStreamUnsafeUtility.CreateReaderFromExistingData((byte*)buffer.GetUnsafePtr(), buffer.Length);
            var ctx = default(DataStreamReader.Context);
            var tick = reader.ReadUInt(ref ctx);
            var receivedCommand = default(TCommandData);
            receivedCommand.Deserialize(tick, reader, ref ctx);
            buffer.Clear();

            // Store received commands in the network command buffer
            var command = commandData[commandTarget.targetEntity];
            command.AddCommandData(receivedCommand);
        }
    }

    private ServerSimulationSystemGroup serverSimulationSystemGroup;
    protected override void OnCreateManager()
    {
        serverSimulationSystemGroup = World.GetExistingSystem<ServerSimulationSystemGroup>();
    }

    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        var recvJob = new ReceiveJob();
        recvJob.commandData = GetBufferFromEntity<TCommandData>();
        recvJob.cmdBuffer = GetBufferFromEntity<IncomingCommandDataStreamBufferComponent>();
        return recvJob.ScheduleSingle(this, inputDeps);
    }
}

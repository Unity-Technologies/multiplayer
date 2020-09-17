using AOT;
using Unity.Burst;
using Unity.Networking.Transport;
using Unity.NetCode;
using Unity.Entities;

public struct LevelLoadRequest : IRpcCommand
{
    public int width;
    public int height;
    public float playerForce;
    public float bulletVelocity;
}

[BurstCompile]
public struct RpcLevelLoaded : IComponentData, IRpcCommandSerializer<RpcLevelLoaded>
{
    public void Serialize(ref DataStreamWriter writer, in RpcLevelLoaded data)
    {
    }

    public void Deserialize(ref DataStreamReader reader, ref RpcLevelLoaded data)
    {
    }

    [BurstCompile]
    [MonoPInvokeCallback(typeof(RpcExecutor.ExecuteDelegate))]
    private static void InvokeExecute(ref RpcExecutor.Parameters parameters)
    {
        var rpcData = default(RpcLevelLoaded);
        rpcData.Deserialize(ref parameters.Reader, ref rpcData);

        parameters.CommandBuffer.AddComponent(parameters.JobIndex, parameters.Connection, new PlayerStateComponentData());
        parameters.CommandBuffer.AddComponent(parameters.JobIndex, parameters.Connection, default(NetworkStreamInGame));
        parameters.CommandBuffer.AddComponent(parameters.JobIndex, parameters.Connection, default(GhostConnectionPosition));
    }

    static PortableFunctionPointer<RpcExecutor.ExecuteDelegate> InvokeExecuteFunctionPointer =
        new PortableFunctionPointer<RpcExecutor.ExecuteDelegate>(InvokeExecute);
    public PortableFunctionPointer<RpcExecutor.ExecuteDelegate> CompileExecute()
    {
        return InvokeExecuteFunctionPointer;
    }
}
class LevelLoadedRpcCommandRequestSystem : RpcCommandRequestSystem<RpcLevelLoaded, RpcLevelLoaded>
{
    [BurstCompile]
    protected struct SendRpc : IJobEntityBatch
    {
        public SendRpcData data;
        public void Execute(ArchetypeChunk chunk, int orderIndex)
        {
            data.Execute(chunk, orderIndex);
        }
    }
    protected override void OnUpdate()
    {
        var sendJob = new SendRpc{data = InitJobData()};
        ScheduleJobData(sendJob);
    }
}

using Unity.Burst;
using Unity.Networking.Transport;
using Unity.NetCode;

[BurstCompile]
public struct PlayerSpawnRequest : IRpcCommand
{
    public void Serialize(ref DataStreamWriter writer)
    {
    }

    public void Deserialize(ref DataStreamReader reader)
    {
    }

    [BurstCompile]
    private static void InvokeExecute(ref RpcExecutor.Parameters parameters)
    {
        RpcExecutor.ExecuteCreateRequestComponent<PlayerSpawnRequest>(ref parameters);
    }

    static PortableFunctionPointer<RpcExecutor.ExecuteDelegate> InvokeExecuteFunctionPointer =
        new PortableFunctionPointer<RpcExecutor.ExecuteDelegate>(InvokeExecute);
    public PortableFunctionPointer<RpcExecutor.ExecuteDelegate> CompileExecute()
    {
        return InvokeExecuteFunctionPointer;
    }
}
class PlayerSpawnRequestRpcCommandRequestSystem : RpcCommandRequestSystem<PlayerSpawnRequest>
{
}


using Unity.Burst;
using Unity.Networking.Transport;
using Unity.NetCode;

[BurstCompile]
public struct PlayerSpawnRequest : IRpcCommand
{
    public void Serialize(DataStreamWriter writer)
    {
    }

    public void Deserialize(DataStreamReader reader, ref DataStreamReader.Context ctx)
    {
    }

    [BurstCompile]
    private static void InvokeExecute(ref RpcExecutor.Parameters parameters)
    {
        RpcExecutor.ExecuteCreateRequestComponent<PlayerSpawnRequest>(ref parameters);
    }
    
    public PortableFunctionPointer<RpcExecutor.ExecuteDelegate> CompileExecute()
    {
        return new PortableFunctionPointer<RpcExecutor.ExecuteDelegate>(InvokeExecute);
    }
}
class PlayerSpawnRequestRpcCommandRequestSystem : RpcCommandRequestSystem<PlayerSpawnRequest>
{
}


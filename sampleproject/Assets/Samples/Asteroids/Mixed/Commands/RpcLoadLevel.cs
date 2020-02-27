using Unity.Burst;
using Unity.Networking.Transport;
using Unity.NetCode;

[BurstCompile]
public struct LevelLoadRequest : IRpcCommand
{
    public int width;
    public int height;
    public float playerForce;
    public float bulletVelocity;

    public void Serialize(ref DataStreamWriter writer)
    {
        writer.WriteInt(width);
        writer.WriteInt(height);
        writer.WriteFloat(playerForce);
        writer.WriteFloat(bulletVelocity);
    }

    public void Deserialize(ref DataStreamReader reader)
    {
        width = reader.ReadInt();
        height = reader.ReadInt();
        playerForce = reader.ReadFloat();
        bulletVelocity = reader.ReadFloat();
    }
    [BurstCompile]
    private static void InvokeExecute(ref RpcExecutor.Parameters parameters)
    {
        RpcExecutor.ExecuteCreateRequestComponent<LevelLoadRequest>(ref parameters);
    }

    static PortableFunctionPointer<RpcExecutor.ExecuteDelegate> InvokeExecuteFunctionPointer =
        new PortableFunctionPointer<RpcExecutor.ExecuteDelegate>(InvokeExecute);
    public PortableFunctionPointer<RpcExecutor.ExecuteDelegate> CompileExecute()
    {
        return InvokeExecuteFunctionPointer;
    }
}
class LevelLoadRequestRpcCommandRequestSystem : RpcCommandRequestSystem<LevelLoadRequest>
{
}

[BurstCompile]
public struct RpcLevelLoaded : IRpcCommand
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
        var rpcData = default(RpcLevelLoaded);
        rpcData.Deserialize(ref parameters.Reader);

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
class LevelLoadedRpcCommandRequestSystem : RpcCommandRequestSystem<RpcLevelLoaded>
{
}

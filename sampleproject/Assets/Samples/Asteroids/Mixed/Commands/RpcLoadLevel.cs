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

    public void Serialize(DataStreamWriter writer)
    {
        writer.Write(width);
        writer.Write(height);
        writer.Write(playerForce);
        writer.Write(bulletVelocity);
    }

    public void Deserialize(DataStreamReader reader, ref DataStreamReader.Context ctx)
    {
        width = reader.ReadInt(ref ctx);
        height = reader.ReadInt(ref ctx);
        playerForce = reader.ReadFloat(ref ctx);
        bulletVelocity = reader.ReadFloat(ref ctx);
    }
    [BurstCompile]
    private static void InvokeExecute(ref RpcExecutor.Parameters parameters)
    {
        RpcExecutor.ExecuteCreateRequestComponent<LevelLoadRequest>(ref parameters);
    }

    public PortableFunctionPointer<RpcExecutor.ExecuteDelegate> CompileExecute()
    {
        return new PortableFunctionPointer<RpcExecutor.ExecuteDelegate>(InvokeExecute);
    }
}
class LevelLoadRequestRpcCommandRequestSystem : RpcCommandRequestSystem<LevelLoadRequest>
{
}

[BurstCompile]
public struct RpcLevelLoaded : IRpcCommand
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
        var rpcData = default(RpcLevelLoaded);
        rpcData.Deserialize(parameters.Reader, ref parameters.ReaderContext);

        parameters.CommandBuffer.AddComponent(parameters.JobIndex, parameters.Connection, new PlayerStateComponentData());
        parameters.CommandBuffer.AddComponent(parameters.JobIndex, parameters.Connection, default(NetworkStreamInGame));
        parameters.CommandBuffer.AddComponent(parameters.JobIndex, parameters.Connection, default(GhostConnectionPosition));
    }

    public PortableFunctionPointer<RpcExecutor.ExecuteDelegate> CompileExecute()
    {
        return new PortableFunctionPointer<RpcExecutor.ExecuteDelegate>(InvokeExecute);
    }
}
class LevelLoadedRpcCommandRequestSystem : RpcCommandRequestSystem<RpcLevelLoaded>
{
}

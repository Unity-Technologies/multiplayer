using Unity.Entities;
using Unity.Networking.Transport;

public struct LevelLoadRequest : IComponentData
{
    public int width;
    public int height;
    public Entity connection;
}
public struct RpcLoadLevel : RpcCommand
{
    public int width;
    public int height;

    public void Execute(Entity connection, EntityCommandBuffer.Concurrent commandBuffer, int jobIndex)
    {
        var req = commandBuffer.CreateEntity(jobIndex);
        commandBuffer.AddComponent(jobIndex, req, new LevelLoadRequest {width = width, height = height, connection = connection});
    }

    public void Serialize(DataStreamWriter writer)
    {
        writer.Write(width);
        writer.Write(height);
    }

    public void Deserialize(DataStreamReader reader, ref DataStreamReader.Context ctx)
    {
        width = reader.ReadInt(ref ctx);
        height = reader.ReadInt(ref ctx);
    }
}

public struct RpcLevelLoaded : RpcCommand
{
    public void Execute(Entity connection, EntityCommandBuffer.Concurrent commandBuffer, int jobIndex)
    {
        commandBuffer.AddComponent(jobIndex, connection, new PlayerStateComponentData());
    }

    public void Serialize(DataStreamWriter writer)
    {
    }

    public void Deserialize(DataStreamReader reader, ref DataStreamReader.Context ctx)
    {
    }
}

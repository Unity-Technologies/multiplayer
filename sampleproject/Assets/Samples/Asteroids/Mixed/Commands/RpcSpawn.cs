using Unity.Entities;
using Unity.Networking.Transport;

public struct PlayerSpawnRequest : IComponentData
{
    public Entity connection;
}
public struct RpcSpawn : RpcCommand
{
    public void Execute(Entity connection, EntityCommandBuffer.Concurrent commandBuffer, int jobIndex)
    {
        var req = commandBuffer.CreateEntity(jobIndex);
        commandBuffer.AddComponent(jobIndex, req, new PlayerSpawnRequest {connection = connection});
    }

    public void Serialize(DataStreamWriter writer)
    {
    }

    public void Deserialize(DataStreamReader reader, ref DataStreamReader.Context ctx)
    {
    }
}

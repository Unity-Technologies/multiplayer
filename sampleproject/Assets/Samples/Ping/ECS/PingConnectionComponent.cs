using Unity.Entities;
using Unity.Networking.Transport;

struct PingServerConnectionComponentData : IComponentData
{
    public NetworkConnection connection;
}

struct PingClientConnectionComponentData : IComponentData
{
    public NetworkConnection connection;
}

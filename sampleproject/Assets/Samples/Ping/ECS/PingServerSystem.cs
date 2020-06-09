using Unity.Entities;
using Unity.Networking.Transport;

[UpdateInGroup(typeof(SimulationSystemGroup))]
public class PingServerSystem : SystemBase
{
    private PingDriverSystem m_ServerDriverSystem;

    protected override void OnCreate()
    {
        m_ServerDriverSystem = World.GetOrCreateSystem<PingDriverSystem>();
    }

    protected override void OnUpdate()
    {
        if (!m_ServerDriverSystem.ServerDriver.IsCreated)
            return;
        var driver = m_ServerDriverSystem.ConcurrentServerDriver;
        Entities.ForEach((ref PingServerConnectionComponentData connection) =>
        {
            DataStreamReader strm;
            NetworkEvent.Type cmd;
            while ((cmd = driver.PopEventForConnection(connection.connection, out strm)) != NetworkEvent.Type.Empty)
            {
                if (cmd == NetworkEvent.Type.Data)
                {
                    int id = strm.ReadInt();
                    var pongData = driver.BeginSend(connection.connection);
                    pongData.WriteInt(id);
                    driver.EndSend(pongData);
                }
                else if (cmd == NetworkEvent.Type.Disconnect)
                {
                    connection = new PingServerConnectionComponentData {connection = default(NetworkConnection)};
                }
            }
        }).ScheduleParallel();
    }
}

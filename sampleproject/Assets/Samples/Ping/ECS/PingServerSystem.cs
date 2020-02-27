using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Networking.Transport;

[UpdateInGroup(typeof(SimulationSystemGroup))]
public class PingServerSystem : JobComponentSystem
{
    private PingDriverSystem m_ServerDriverSystem;

    [BurstCompile]
    struct PongJob : IJobForEach<PingServerConnectionComponentData>
    {
        public NetworkDriver.Concurrent driver;

        public void Execute(ref PingServerConnectionComponentData connection)
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
        }
    }

    protected override void OnCreate()
    {
        m_ServerDriverSystem = World.GetOrCreateSystem<PingDriverSystem>();
    }

    protected override JobHandle OnUpdate(JobHandle inputDep)
    {
        if (!m_ServerDriverSystem.ServerDriver.IsCreated)
            return inputDep;
        var pongJob = new PongJob
        {
            driver = m_ServerDriverSystem.ConcurrentServerDriver
        };
        inputDep = pongJob.Schedule(this, inputDep);

        return inputDep;
    }
}

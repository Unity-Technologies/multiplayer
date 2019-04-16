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
    struct PongJob : IJobProcessComponentData<PingServerConnectionComponentData>
    {
        public UdpNetworkDriver.Concurrent driver;

        public void Execute(ref PingServerConnectionComponentData connection)
        {
            DataStreamReader strm;
            NetworkEvent.Type cmd;
            while ((cmd = driver.PopEventForConnection(connection.connection, out strm)) != NetworkEvent.Type.Empty)
            {
                if (cmd == NetworkEvent.Type.Data)
                {
                    var readerCtx = default(DataStreamReader.Context);
                    int id = strm.ReadInt(ref readerCtx);
                    var pongData = new DataStreamWriter(4, Allocator.Temp);
                    pongData.Write(id);
                    driver.Send(NetworkPipeline.Null, connection.connection, pongData);
                }
                else if (cmd == NetworkEvent.Type.Disconnect)
                {
                    connection = new PingServerConnectionComponentData {connection = default(NetworkConnection)};
                }
            }
        }
    }

    protected override void OnCreateManager()
    {
        m_ServerDriverSystem = World.GetOrCreateManager<PingDriverSystem>();
    }

    protected override JobHandle OnUpdate(JobHandle inputDep)
    {
        var pongJob = new PongJob
        {
            driver = m_ServerDriverSystem.ConcurrentServerDriver
        };
        inputDep = pongJob.Schedule(this, inputDep);

        return inputDep;
    }
}

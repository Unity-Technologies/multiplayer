using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Networking.Transport;
using UnityEngine;
using UnityEngine.Experimental.PlayerLoop;
using UdpCNetworkDriver = Unity.Networking.Transport.BasicNetworkDriver<Unity.Networking.Transport.IPv4UDPSocket>;

[UpdateAfter(typeof(FixedUpdate))]
[UpdateAfter(typeof(PingBarrierSystem))]
public class PingClientSystem : JobComponentSystem
{
#pragma warning disable 649
    [Inject] private PingBarrierSystem m_Barrier;
    [Inject] private PingDriverSystem m_DriverSystem;

    struct ConnectionList
    {
        public ComponentDataArray<PingClientConnectionComponentData> connections;
        public EntityArray entities;
    }
    [Inject] private ConnectionList connectionList;

    // Inject the driver list to make sure we get automatic dependency tracking for accessing the driver
    struct DriverList
    {
        public ComponentDataArray<PingDriverComponentData> drivers;
    }
    [Inject] private DriverList driverList;
#pragma warning restore 649

    struct PendingPing
    {
        public int id;
        public float time;
    }
    struct PingJob : IJob
    {
        public UdpCNetworkDriver driver;
        public ComponentDataArray<PingClientConnectionComponentData> connections;
        public EntityArray connectionEntities;
        public NetworkEndPoint serverEP;
        public NativeArray<PendingPing> pendingPings;
        public NativeArray<int> pingStats;
        public float fixedTime;
        public EntityCommandBuffer commandBuffer;

        public void Execute()
        {
            if (serverEP.IsValid && connections.Length == 0)
            {
                commandBuffer.CreateEntity();
                commandBuffer.AddComponent(new PingClientConnectionComponentData{connection = driver.Connect(serverEP)});
            }
            if (!serverEP.IsValid && connections.Length > 0)
            {
                for (int con = 0; con < connections.Length; ++con)
                {
                    connections[con].connection.Disconnect(driver);
                    commandBuffer.DestroyEntity(connectionEntities[con]);
                }

                return;
            }
            for (int con = 0; con < connections.Length; ++con)
            {
                DataStreamReader strm;
                NetworkEvent.Type cmd;
                while ((cmd = connections[con].connection.PopEvent(driver, out strm)) != NetworkEvent.Type.Empty)
                {
                    if (cmd == NetworkEvent.Type.Connect)
                    {
                        pendingPings[0] = new PendingPing {id = pingStats[0], time = fixedTime};
                        var pingData = new DataStreamWriter(4, Allocator.Temp);
                        pingData.Write(pingStats[0]);
                        connections[con].connection.Send(driver, pingData);
                        pingData.Dispose();
                        pingStats[0] = pingStats[0] + 1;
                    }
                    else if (cmd == NetworkEvent.Type.Data)
                    {
                        pingStats[1] = (int) ((fixedTime - pendingPings[0].time) * 1000);
                        connections[con].connection.Disconnect(driver);
                        commandBuffer.DestroyEntity(connectionEntities[con]);
                    }
                    else if (cmd == NetworkEvent.Type.Disconnect)
                    {
                        commandBuffer.DestroyEntity(connectionEntities[con]);
                    }
                }
            }
        }
    }

    private NativeArray<PendingPing> m_pendingPings;
    private NativeArray<int> m_pingStats;

    protected override void OnCreateManager()
    {
        m_pendingPings = new NativeArray<PendingPing>(64, Allocator.Persistent);
        m_pingStats = new NativeArray<int>(2, Allocator.Persistent);
    }

    protected override void OnDestroyManager()
    {
        m_pendingPings.Dispose();
        m_pingStats.Dispose();
    }

    protected override JobHandle OnUpdate(JobHandle inputDep)
    {
        if (!m_DriverSystem.ClientDriver.IsCreated)
            return inputDep;
        PingClientUIBehaviour.UpdateStats(m_pingStats[0], m_pingStats[1]);
        var pingJob = new PingJob
        {
            driver = m_DriverSystem.ClientDriver,
            connections = connectionList.connections,
            connectionEntities = connectionList.entities,
            serverEP = PingClientUIBehaviour.ServerEndPoint,
            pendingPings = m_pendingPings,
            pingStats = m_pingStats,
            fixedTime = Time.fixedTime,
            commandBuffer = m_Barrier.CreateCommandBuffer()
        };
        return pingJob.Schedule(inputDep);
    }

}

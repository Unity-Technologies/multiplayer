using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Networking.Transport;
using UnityEngine;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[AlwaysUpdateSystem]
public class PingClientSystem : JobComponentSystem
{
    private BeginSimulationEntityCommandBufferSystem m_Barrier;
    private PingDriverSystem m_DriverSystem;
    private ComponentGroup m_ConnectionGroup;

    struct PendingPing
    {
        public int id;
        public float time;
    }
    // Adding and removing components with EntityCommandBuffer is not burst compatible
    //[BurstCompile]
    struct PingJob : IJobProcessComponentDataWithEntity<PingClientConnectionComponentData>
    {
        public UdpNetworkDriver driver;
        public NetworkEndPoint serverEP;
        public NativeArray<PendingPing> pendingPings;
        public NativeArray<int> pingStats;
        public float fixedTime;
        public EntityCommandBuffer commandBuffer;

        public void Execute(Entity entity, int index, ref PingClientConnectionComponentData connection)
        {
            if (!serverEP.IsValid)
            {
                connection.connection.Disconnect(driver);
                commandBuffer.DestroyEntity(entity);
                return;
            }
            DataStreamReader strm;
            NetworkEvent.Type cmd;
            while ((cmd = connection.connection.PopEvent(driver, out strm)) != NetworkEvent.Type.Empty)
            {
                if (cmd == NetworkEvent.Type.Connect)
                {
                    pendingPings[0] = new PendingPing {id = pingStats[0], time = fixedTime};
                    var pingData = new DataStreamWriter(4, Allocator.Temp);
                    pingData.Write(pingStats[0]);
                    connection.connection.Send(driver, pingData);
                    pingStats[0] = pingStats[0] + 1;
                }
                else if (cmd == NetworkEvent.Type.Data)
                {
                    pingStats[1] = (int) ((fixedTime - pendingPings[0].time) * 1000);
                    connection.connection.Disconnect(driver);
                    commandBuffer.DestroyEntity(entity);
                }
                else if (cmd == NetworkEvent.Type.Disconnect)
                {
                    commandBuffer.DestroyEntity(entity);
                }
            }
        }
    }

    struct ConnectJob : IJob
    {
        public UdpNetworkDriver driver;
        public NetworkEndPoint serverEP;
        public EntityCommandBuffer commandBuffer;
        public void Execute()
        {
            var ent = commandBuffer.CreateEntity();
            commandBuffer.AddComponent(ent, new PingClientConnectionComponentData{connection = driver.Connect(serverEP)});
        }
    }

    private NativeArray<PendingPing> m_pendingPings;
    private NativeArray<int> m_pingStats;

    protected override void OnCreateManager()
    {
        m_pendingPings = new NativeArray<PendingPing>(64, Allocator.Persistent);
        m_pingStats = new NativeArray<int>(2, Allocator.Persistent);
        m_Barrier = World.GetOrCreateManager<BeginSimulationEntityCommandBufferSystem>();
        m_DriverSystem = World.GetOrCreateManager<PingDriverSystem>();
        m_ConnectionGroup = GetComponentGroup(ComponentType.ReadWrite<PingClientConnectionComponentData>());
        // Group used only to get dependency tracking for the driver
        GetComponentGroup(ComponentType.ReadWrite<PingServerConnectionComponentData>());
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
        if (PingClientUIBehaviour.ServerEndPoint.IsValid && m_ConnectionGroup.IsEmptyIgnoreFilter)
        {
            var conJob = new ConnectJob
            {
                driver = m_DriverSystem.ClientDriver,
                serverEP = PingClientUIBehaviour.ServerEndPoint,
                commandBuffer = m_Barrier.CreateCommandBuffer()
            };
            inputDep = conJob.Schedule(inputDep);
            m_Barrier.AddJobHandleForProducer(inputDep);
            return inputDep;
        }
        var pingJob = new PingJob
        {
            driver = m_DriverSystem.ClientDriver,
            serverEP = PingClientUIBehaviour.ServerEndPoint,
            pendingPings = m_pendingPings,
            pingStats = m_pingStats,
            fixedTime = Time.fixedTime,
            commandBuffer = m_Barrier.CreateCommandBuffer()
        };
        var handle = pingJob.ScheduleSingle(this, inputDep);
        m_Barrier.AddJobHandleForProducer(handle);
        return handle;
    }

}

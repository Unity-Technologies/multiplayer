using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Networking.Transport;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[AlwaysUpdateSystem]
public class PingClientSystem : JobComponentSystem
{
    private BeginSimulationEntityCommandBufferSystem m_Barrier;
    private PingDriverSystem m_DriverSystem;
    private EntityQuery m_ConnectionGroup;

    struct PendingPing
    {
        public int id;
        public double time;
    }

    [BurstCompile]
    struct PingJob : IJobForEachWithEntity<PingClientConnectionComponentData>
    {
        public NetworkDriver driver;
        public NetworkEndPoint serverEP;
        public NativeArray<PendingPing> pendingPings;
        public NativeArray<int> pingStats;
        public double frameTime;
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
                    pendingPings[0] = new PendingPing {id = pingStats[0], time = frameTime};
                    var pingData = driver.BeginSend(connection.connection);
                    pingData.WriteInt(pingStats[0]);
                    driver.EndSend(pingData);
                    pingStats[0] = pingStats[0] + 1;
                }
                else if (cmd == NetworkEvent.Type.Data)
                {
                    pingStats[1] = (int) ((frameTime - pendingPings[0].time) * 1000);
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

    private NativeArray<PendingPing> m_pendingPings;
    private NativeArray<int> m_pingStats;

    protected override void OnCreate()
    {
        m_pendingPings = new NativeArray<PendingPing>(64, Allocator.Persistent);
        m_pingStats = new NativeArray<int>(2, Allocator.Persistent);
        m_Barrier = World.GetOrCreateSystem<BeginSimulationEntityCommandBufferSystem>();
        m_DriverSystem = World.GetOrCreateSystem<PingDriverSystem>();
        m_ConnectionGroup = GetEntityQuery(ComponentType.ReadWrite<PingClientConnectionComponentData>());
        // Group used only to get dependency tracking for the driver
        GetEntityQuery(ComponentType.ReadWrite<PingServerConnectionComponentData>());
    }

    protected override void OnDestroy()
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
            inputDep.Complete();
            var ent = EntityManager.CreateEntity();
            EntityManager.AddComponentData(ent, new PingClientConnectionComponentData{connection = m_DriverSystem.ClientDriver.Connect(PingClientUIBehaviour.ServerEndPoint)});
            return default;
        }
        var pingJob = new PingJob
        {
            driver = m_DriverSystem.ClientDriver,
            serverEP = PingClientUIBehaviour.ServerEndPoint,
            pendingPings = m_pendingPings,
            pingStats = m_pingStats,
            frameTime = Time.ElapsedTime,
            commandBuffer = m_Barrier.CreateCommandBuffer()
        };
        var handle = pingJob.ScheduleSingle(this, inputDep);
        m_Barrier.AddJobHandleForProducer(handle);
        return handle;
    }

}

using Unity.Collections;
using Unity.Entities;
using Unity.Networking.Transport;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[AlwaysUpdateSystem]
public class PingClientSystem : SystemBase
{
    private BeginSimulationEntityCommandBufferSystem m_Barrier;
    private PingDriverSystem m_DriverSystem;
    private EntityQuery m_ConnectionGroup;
    private EntityQuery m_ServerConnectionGroup;
    private NativeArray<PendingPing> m_PendingPings;
    private NativeArray<int> m_PingStats;

    struct PendingPing
    {
        public int id;
        public double time;
    }

    protected override void OnCreate()
    {
        m_PendingPings = new NativeArray<PendingPing>(64, Allocator.Persistent);
        m_PingStats = new NativeArray<int>(2, Allocator.Persistent);
        m_Barrier = World.GetOrCreateSystem<BeginSimulationEntityCommandBufferSystem>();
        m_DriverSystem = World.GetOrCreateSystem<PingDriverSystem>();
        m_ConnectionGroup = GetEntityQuery(ComponentType.ReadWrite<PingClientConnectionComponentData>());
        // Group used only to get dependency tracking for the driver
        m_ServerConnectionGroup = GetEntityQuery(ComponentType.ReadWrite<PingServerConnectionComponentData>());
    }

    protected override void OnDestroy()
    {
        m_PendingPings.Dispose();
        m_PingStats.Dispose();
    }

    protected override void OnUpdate()
    {
        if (!m_DriverSystem.ClientDriver.IsCreated)
            return;
        PingClientUIBehaviour.UpdateStats(m_PingStats[0], m_PingStats[1]);
        if (PingClientUIBehaviour.ServerEndPoint.IsValid && m_ConnectionGroup.IsEmptyIgnoreFilter)
        {
            Dependency.Complete();
            var ent = EntityManager.CreateEntity();
            EntityManager.AddComponentData(ent, new PingClientConnectionComponentData{connection = m_DriverSystem.ClientDriver.Connect(PingClientUIBehaviour.ServerEndPoint)});
            return;
        }

        var driver = m_DriverSystem.ClientDriver;
        var serverEP = PingClientUIBehaviour.ServerEndPoint;
        var pendingPings = m_PendingPings;
        var pingStats = m_PingStats;
        var frameTime = Time.ElapsedTime;
        var commandBuffer = m_Barrier.CreateCommandBuffer();
        Entities.ForEach((Entity entity, ref PingClientConnectionComponentData connection) =>
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
        }).Schedule();
        m_Barrier.AddJobHandleForProducer(Dependency);
        m_ServerConnectionGroup.AddDependency(Dependency);
    }
}

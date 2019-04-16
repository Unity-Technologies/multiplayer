using System;
using Unity.Burst;
using Unity.Entities;
using Unity.Jobs;
using Unity.Collections;
using Unity.Networking.Transport;
using UnityEngine;

// SystemStateComponent to track which drivers have been created and destroyed
public struct PingDriverStateComponent : ISystemStateComponentData
{
    public int isServer;
}

// The PingDriverSystem runs at the beginning of FixedUpdate. It updates the NetworkDriver(s) and Accepts new connections
// creating entities to track the connections
[UpdateInGroup(typeof(SimulationSystemGroup))]
[AlwaysUpdateSystem]
public class PingDriverSystem : JobComponentSystem
{
    public UdpNetworkDriver ServerDriver { get; private set; }
    public UdpNetworkDriver ClientDriver { get; private set; }
    public UdpNetworkDriver.Concurrent ConcurrentServerDriver { get; private set; }
    public UdpNetworkDriver.Concurrent ConcurrentClientDriver { get; private set; }

    private BeginSimulationEntityCommandBufferSystem m_Barrier;
    private ComponentGroup m_NewDriverGroup;
    private ComponentGroup m_DestroyedDriverGroup;
    private ComponentGroup m_ServerConnectionGroup;

    protected override void OnCreateManager()
    {
        m_Barrier = World.GetOrCreateManager<BeginSimulationEntityCommandBufferSystem>();
        m_NewDriverGroup = GetComponentGroup(ComponentType.ReadOnly<PingDriverComponentData>(),
            ComponentType.Exclude<PingDriverStateComponent>());
        m_DestroyedDriverGroup = GetComponentGroup(ComponentType.Exclude<PingDriverComponentData>(),
            ComponentType.ReadOnly<PingDriverStateComponent>());
        m_ServerConnectionGroup = GetComponentGroup(ComponentType.ReadWrite<PingServerConnectionComponentData>());
    }

    protected override void OnDestroyManager()
    {
        // Destroy NetworkDrivers if the manager is destroyed with live entities
        if (ServerDriver.IsCreated)
            ServerDriver.Dispose();
        if (ClientDriver.IsCreated)
            ClientDriver.Dispose();
    }

    // Adding and removing components with EntityCommandBuffer is not burst compatible
    //[BurstCompile]
    struct DriverAcceptJob : IJob
    {
        public UdpNetworkDriver driver;
        public EntityCommandBuffer commandBuffer;

        public void Execute()
        {
            // Accept all connections and create entities for the new connections using an entity command buffer
            while (true)
            {
                var con = driver.Accept();
                if (!con.IsCreated)
                    break;
                var ent = commandBuffer.CreateEntity();
                commandBuffer.AddComponent(ent, new PingServerConnectionComponentData{connection = con});
            }
        }
    }
    [BurstCompile]
    struct DriverCleanupJob : IJobProcessComponentDataWithEntity<PingServerConnectionComponentData>
    {
        public EntityCommandBuffer.Concurrent commandBuffer;

        public void Execute(Entity entity, int index, [ReadOnly] ref PingServerConnectionComponentData connection)
        {
            // Cleanup old connections
            if (!connection.connection.IsCreated)
            {
                commandBuffer.DestroyEntity(index, entity);
            }
        }
    }
    protected override JobHandle OnUpdate(JobHandle inputDep)
    {
        var commandBuffer = m_Barrier.CreateCommandBuffer();
        // Destroy drivers if the PingDriverComponents were removed
        if (!m_DestroyedDriverGroup.IsEmptyIgnoreFilter)
        {
            inputDep.Complete();
            var destroyedDriverEntity = m_DestroyedDriverGroup.ToEntityArray(Allocator.TempJob);
            var destroyedDriverList = m_DestroyedDriverGroup.ToComponentDataArray<PingDriverComponentData>(Allocator.TempJob);
            for (int i = 0; i < destroyedDriverList.Length; ++i)
            {
                if (destroyedDriverList[i].isServer != 0)
                {
                    var serverConnectionList = m_ServerConnectionGroup.ToEntityArray(Allocator.TempJob);
                    // Also destroy all active connections when the driver dies
                    for (int con = 0; con < serverConnectionList.Length; ++con)
                        commandBuffer.DestroyEntity(serverConnectionList[con]);
                    serverConnectionList.Dispose();
                    ServerDriver.Dispose();
                }
                else
                    ClientDriver.Dispose();

                commandBuffer.RemoveComponent<PingDriverStateComponent>(destroyedDriverEntity[i]);
            }

            destroyedDriverList.Dispose();
            destroyedDriverEntity.Dispose();
        }

        // Create drivers if new PingDriverComponents were added
        if (!m_NewDriverGroup.IsEmptyIgnoreFilter)
        {
            inputDep.Complete();
            var newDriverEntity = m_NewDriverGroup.ToEntityArray(Allocator.TempJob);
            var newDriverList = m_NewDriverGroup.ToComponentDataArray<PingDriverComponentData>(Allocator.TempJob);
            for (int i = 0; i < newDriverList.Length; ++i)
            {
                if (newDriverList[i].isServer != 0)
                {
                    if (ServerDriver.IsCreated)
                        throw new InvalidOperationException("Cannot create multiple server drivers");
                    var drv = new UdpNetworkDriver(new INetworkParameter[0]);
                    var addr = NetworkEndPoint.AnyIpv4;
                    addr.Port = 9000;
                    if (drv.Bind(addr) != 0)
                        throw new Exception("Failed to bind to port 9000");
                    else
                        drv.Listen();
                    ServerDriver = drv;
                    ConcurrentServerDriver = ServerDriver.ToConcurrent();
                }
                else
                {
                    if (ClientDriver.IsCreated)
                        throw new InvalidOperationException("Cannot create multiple client drivers");
                    ClientDriver = new UdpNetworkDriver(new INetworkParameter[0]);
                    ConcurrentClientDriver = ClientDriver.ToConcurrent();
                }

                commandBuffer.AddComponent(newDriverEntity[i],
                    new PingDriverStateComponent {isServer = newDriverList[i].isServer});
            }
            newDriverList.Dispose();
            newDriverEntity.Dispose();
        }

        JobHandle clientDep = default(JobHandle);
        JobHandle serverDep = default(JobHandle);

        // Go through and update all drivers, also accept all incoming connections for server drivers
        if (ServerDriver.IsCreated)
        {
            // Schedule a chain with driver update, a job to accept all connections and finally a job to delete all invalid connections
            serverDep = ServerDriver.ScheduleUpdate(inputDep);
            var acceptJob = new DriverAcceptJob
                {driver = ServerDriver, commandBuffer = commandBuffer};
            serverDep = acceptJob.Schedule(serverDep);
            var cleanupJob = new DriverCleanupJob
            {
                commandBuffer = m_Barrier.CreateCommandBuffer().ToConcurrent()
            };
            serverDep = cleanupJob.Schedule(this, serverDep);
            m_Barrier.AddJobHandleForProducer(serverDep);
        }

        if (ClientDriver.IsCreated)
        {
            clientDep = ClientDriver.ScheduleUpdate(inputDep);
        }

        return JobHandle.CombineDependencies(clientDep, serverDep);
    }
}

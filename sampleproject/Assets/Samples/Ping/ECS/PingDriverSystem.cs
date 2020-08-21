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
public class PingDriverSystem : SystemBase
{
    public NetworkDriver ServerDriver { get; private set; }
    public NetworkDriver ClientDriver { get; private set; }
    public NetworkDriver.Concurrent ConcurrentServerDriver { get; private set; }
    public NetworkDriver.Concurrent ConcurrentClientDriver { get; private set; }

    private BeginSimulationEntityCommandBufferSystem m_Barrier;
    private EntityQuery m_NewDriverGroup;
    private EntityQuery m_DestroyedDriverGroup;
    private EntityQuery m_ServerConnectionGroup;

    protected override void OnCreate()
    {
        m_Barrier = World.GetOrCreateSystem<BeginSimulationEntityCommandBufferSystem>();
        m_NewDriverGroup = GetEntityQuery(ComponentType.ReadOnly<PingDriverComponentData>(),
            ComponentType.Exclude<PingDriverStateComponent>());
        m_DestroyedDriverGroup = GetEntityQuery(ComponentType.Exclude<PingDriverComponentData>(),
            ComponentType.ReadOnly<PingDriverStateComponent>());
        m_ServerConnectionGroup = GetEntityQuery(ComponentType.ReadWrite<PingServerConnectionComponentData>());
    }

    protected override void OnDestroy()
    {
        // Destroy NetworkDrivers if the manager is destroyed with live entities
        if (ServerDriver.IsCreated)
            ServerDriver.Dispose();
        if (ClientDriver.IsCreated)
            ClientDriver.Dispose();
    }

    // Adding and removing components with EntityCommandBuffer is not burst compatible
    [BurstCompile]
    struct DriverAcceptJob : IJob
    {
        public NetworkDriver driver;
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

    protected override void OnUpdate()
    {
        var commandBuffer = m_Barrier.CreateCommandBuffer();
        // Destroy drivers if the PingDriverComponents were removed
        if (!m_DestroyedDriverGroup.IsEmptyIgnoreFilter)
        {
            Dependency.Complete();
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
            Dependency.Complete();
            var newDriverEntity = m_NewDriverGroup.ToEntityArray(Allocator.TempJob);
            var newDriverList = m_NewDriverGroup.ToComponentDataArray<PingDriverComponentData>(Allocator.TempJob);
            for (int i = 0; i < newDriverList.Length; ++i)
            {
                if (newDriverList[i].isServer != 0)
                {
                    if (ServerDriver.IsCreated)
                        throw new InvalidOperationException("Cannot create multiple server drivers");
                    var drv = NetworkDriver.Create();
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
                    ClientDriver = NetworkDriver.Create();
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
            serverDep = ServerDriver.ScheduleUpdate(Dependency);
            var acceptJob = new DriverAcceptJob
                {driver = ServerDriver, commandBuffer = commandBuffer};
            serverDep = acceptJob.Schedule(serverDep);
            var cleanupCommandBuffer = m_Barrier.CreateCommandBuffer().AsParallelWriter();
            serverDep = Entities.ForEach((Entity entity, int nativeThreadIndex, in PingServerConnectionComponentData connection) =>
            {
                // Cleanup old connections
                if (!connection.connection.IsCreated)
                {
                    cleanupCommandBuffer.DestroyEntity(nativeThreadIndex, entity);
                }
            }).ScheduleParallel(serverDep);
            Dependency = serverDep;
            m_Barrier.AddJobHandleForProducer(Dependency);
        }

        if (ClientDriver.IsCreated)
        {
            clientDep = ClientDriver.ScheduleUpdate(Dependency);
            Dependency = clientDep;
        }

        JobHandle.CombineDependencies(clientDep, serverDep);
    }
}

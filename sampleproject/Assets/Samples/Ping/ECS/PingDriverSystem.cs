using System;
using System.Net;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Networking.Transport;
using UnityEngine.Experimental.PlayerLoop;
using UdpCNetworkDriver = Unity.Networking.Transport.BasicNetworkDriver<Unity.Networking.Transport.IPv4UDPSocket>;

// Barrier to execute the creation and destruction of connection entities
// This runs at the end of fixed update but before the PingServerSystem
[UpdateAfter(typeof(FixedUpdate))]
public class PingBarrierSystem : BarrierSystem
{
}

// SystemStateComponent to track which drivers have been created and destroyed
public struct PingDriverStateComponent : ISystemStateComponentData
{
    public int isServer;
}

// The PingDriverSystem runs at the beginning of FixedUpdate. It updates the NetworkDriver(s) and Accepts new connections
// creating entities to track the connections
[UpdateBefore(typeof(FixedUpdate))]
[UpdateBefore(typeof(PingBarrierSystem))]
public class PingDriverSystem : JobComponentSystem
{
    public UdpCNetworkDriver ServerDriver { get; private set; }
    public UdpCNetworkDriver ClientDriver { get; private set; }

#pragma warning disable 649
    [Inject] private PingBarrierSystem m_Barrier;

    struct ServerConnectionList
    {
        public ComponentDataArray<PingServerConnectionComponentData> connections;
        public EntityArray entities;
    }

    struct DriverList
    {
        public ComponentDataArray<PingDriverComponentData> drivers;
        public ComponentDataArray<PingDriverStateComponent> state;
    }
    struct NewDriverList
    {
        public ComponentDataArray<PingDriverComponentData> drivers;
        public SubtractiveComponent<PingDriverStateComponent> state;
        public EntityArray entities;
    }
    struct DestroyedDriverList
    {
        public SubtractiveComponent<PingDriverComponentData> drivers;
        public ComponentDataArray<PingDriverStateComponent> state;
        public EntityArray entities;
    }

    // List of all active server connections
    [Inject] private ServerConnectionList serverConnectionList;
    // List of all drivers which are currently in use
    [Inject] private DriverList driverList;
    // List of all drivers which needs to be created
    [Inject] private NewDriverList newDriverList;
    // List of all drivers which should be destroyed
    [Inject] private DestroyedDriverList destroyedDriverList;
#pragma warning restore 649

    protected override void OnDestroyManager()
    {
        // Destroy NetworkDrivers if the manager is destroyed with live entities
        if (ServerDriver.IsCreated)
            ServerDriver.Dispose();
        if (ClientDriver.IsCreated)
            ClientDriver.Dispose();
    }

    struct DriverAcceptJob : IJob
    {
        public UdpCNetworkDriver driver;
        public EntityCommandBuffer commandBuffer;

        public void Execute()
        {
            // Accept all connections and create entities for the new connections using an entity command buffer
            while (true)
            {
                var con = driver.Accept();
                if (!con.IsCreated)
                    break;
                commandBuffer.CreateEntity();
                commandBuffer.AddComponent(new PingServerConnectionComponentData{connection = con});
            }
        }
    }
    struct DriverCleanupJob : IJob
    {
        public ComponentDataArray<PingServerConnectionComponentData> serverConnections;
        public EntityArray serverConnectionEntities;
        public EntityCommandBuffer commandBuffer;

        public void Execute()
        {
            // Cleanup old connections
            for (int i = 0; i < serverConnections.Length; ++i)
            {
                if (!serverConnections[i].connection.IsCreated)
                {
                    commandBuffer.DestroyEntity(serverConnectionEntities[i]);
                }
            }
        }
    }
    protected override JobHandle OnUpdate(JobHandle inputDep)
    {
        inputDep.Complete();
        var commandBuffer = m_Barrier.CreateCommandBuffer();
        // Destroy drivers if the PingDriverComponents were removed
        for (int i = 0; i < destroyedDriverList.state.Length; ++i)
        {
            if (destroyedDriverList.state[i].isServer != 0)
            {
                // Allso destroy all active connections when the driver dies
                for (int con = 0; con < serverConnectionList.connections.Length; ++con)
                    commandBuffer.DestroyEntity(serverConnectionList.entities[con]);
                ServerDriver.Dispose();
            }
            else
                ClientDriver.Dispose();
            commandBuffer.RemoveComponent<PingDriverStateComponent>(destroyedDriverList.entities[i]);
        }
        // Create drivers if new PingDriverComponents were added
        for (int i = 0; i < newDriverList.drivers.Length; ++i)
        {
            if (newDriverList.drivers[i].isServer != 0)
            {
                if (ServerDriver.IsCreated)
                    throw new InvalidOperationException("Cannot create multiple server drivers");
                var drv = new UdpCNetworkDriver(new INetworkParameter[0]);
                if (drv.Bind(new IPEndPoint(IPAddress.Any, 9000)) != 0)
                    throw new Exception("Failed to bind to port 9000");
                else
                    drv.Listen();
                ServerDriver = drv;
            }
            else
            {
                if (ClientDriver.IsCreated)
                    throw new InvalidOperationException("Cannot create multiple client drivers");
                ClientDriver = new UdpCNetworkDriver(new INetworkParameter[0]);
            }
            commandBuffer.AddComponent(newDriverList.entities[i], new PingDriverStateComponent{isServer = newDriverList.drivers[i].isServer});
        }

        JobHandle clientDep = default(JobHandle);
        JobHandle serverDep = default(JobHandle);

        // Go through and update all drivers, also accept all incoming connections for server drivers
        for (int i = 0; i < driverList.drivers.Length; ++i)
        {
            if (driverList.drivers[i].isServer != 0)
            {
                // Schedule a chain with driver update, a job to accept all connections and finally a job to delete all invalid connections
                serverDep = ServerDriver.ScheduleUpdate();
                var acceptJob = new DriverAcceptJob
                    {driver = ServerDriver, commandBuffer = commandBuffer};
                serverDep = acceptJob.Schedule(serverDep);
                var cleanupJob = new DriverCleanupJob
                {
                    serverConnections = serverConnectionList.connections,
                    serverConnectionEntities = serverConnectionList.entities,
                    commandBuffer = commandBuffer
                };
                serverDep = cleanupJob.Schedule(serverDep);
            }
            else
                clientDep = ClientDriver.ScheduleUpdate();
        }

        return JobHandle.CombineDependencies(clientDep, serverDep);
    }
}

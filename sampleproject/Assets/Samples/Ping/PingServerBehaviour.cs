using Unity.Burst;
using UnityEngine;
using Unity.Networking.Transport;
using Unity.Collections;
using Unity.Jobs;

public class PingServerBehaviour : MonoBehaviour
{
    public UdpNetworkDriver m_ServerDriver;
    private NativeList<NetworkConnection> m_connections;

    private JobHandle m_updateHandle;

    void Start()
    {
        ushort serverPort = 9000;
        ushort newPort = 0;
        if (CommandLine.TryGetCommandLineArgValue("-port", out newPort))
            serverPort = newPort;
        // Create the server driver, bind it to a port and start listening for incoming connections
        m_ServerDriver = new UdpNetworkDriver(new INetworkParameter[0]);
        var addr = NetworkEndPoint.AnyIpv4;
        addr.Port = serverPort;
        if (m_ServerDriver.Bind(addr) != 0)
            Debug.Log($"Failed to bind to port {serverPort}");
        else
            m_ServerDriver.Listen();

        m_connections = new NativeList<NetworkConnection>(16, Allocator.Persistent);

        SQPDriver.ServerPort = serverPort;
    }

    void OnDestroy()
    {
        // All jobs must be completed before we can dispose the data they use
        m_updateHandle.Complete();
        m_ServerDriver.Dispose();
        m_connections.Dispose();
    }

    [BurstCompile]
    struct DriverUpdateJob : IJob
    {
        public UdpNetworkDriver driver;
        public NativeList<NetworkConnection> connections;

        public void Execute()
        {
            // Remove connections which have been destroyed from the list of active connections
            for (int i = 0; i < connections.Length; ++i)
            {
                if (!connections[i].IsCreated)
                {
                    connections.RemoveAtSwapBack(i);
                    // Index i is a new connection since we did a swap back, check it again
                    --i;
                }
            }

            // Accept all new connections
            while (true)
            {
                var con = driver.Accept();
                // "Nothing more to accept" is signaled by returning an invalid connection from accept
                if (!con.IsCreated)
                    break;
                connections.Add(con);
            }
        }
    }

    static NetworkConnection ProcessSingleConnection(UdpNetworkDriver.Concurrent driver, NetworkConnection connection)
    {
        DataStreamReader strm;
        NetworkEvent.Type cmd;
        // Pop all events for the connection
        while ((cmd = driver.PopEventForConnection(connection, out strm)) != NetworkEvent.Type.Empty)
        {
            if (cmd == NetworkEvent.Type.Data)
            {
                // For ping requests we reply with a pong message
                // A DataStreamReader.Context is required to keep track of current read position since
                // DataStreamReader is immutable
                var readerCtx = default(DataStreamReader.Context);
                int id = strm.ReadInt(ref readerCtx);
                // Create a temporary DataStreamWriter to keep our serialized pong message
                var pongData = new DataStreamWriter(4, Allocator.Temp);
                pongData.Write(id);
                // Send the pong message with the same id as the ping
                driver.Send(NetworkPipeline.Null, connection, pongData);
            }
            else if (cmd == NetworkEvent.Type.Disconnect)
            {
                // When disconnected we make sure the connection return false to IsCreated so the next frames
                // DriverUpdateJob will remove it
                return default(NetworkConnection);
            }
        }

        return connection;
    }
#if ENABLE_IL2CPP
    [BurstCompile]
    struct PongJob : IJob
    {
        public UdpCNetworkDriver.Concurrent driver;
        public NativeList<NetworkConnection> connections;

        public void Execute()
        {
            for (int i = 0; i < connections.Length; ++i)
                connections[i] = ProcessSingleConnection(driver, connections[i]);
        }
    }
#else
    [BurstCompile]
    struct PongJob : IJobParallelFor
    {
        public UdpNetworkDriver.Concurrent driver;
        public NativeArray<NetworkConnection> connections;

        public void Execute(int i)
        {
            connections[i] = ProcessSingleConnection(driver, connections[i]);
        }
    }
#endif

    void LateUpdate()
    {
        // On fast clients we can get more than 4 frames per fixed update, this call prevents warnings about TempJob
        // allocation longer than 4 frames in those cases
        m_updateHandle.Complete();
    }

    void FixedUpdate()
    {
        // Wait for the previous frames ping to complete before starting a new one, the Complete in LateUpdate is not
        // enough since we can get multiple FixedUpdate per frame on slow clients
        m_updateHandle.Complete();
        // If there is at least one client connected update the activity so the server is not shutdown
        if (m_connections.Length > 0)
            DedicatedServerConfig.UpdateLastActivity();
        var updateJob = new DriverUpdateJob {driver = m_ServerDriver, connections = m_connections};
        var pongJob = new PongJob
        {
            // PongJob is a ParallelFor job, it must use the concurrent NetworkDriver
            driver = m_ServerDriver.ToConcurrent(),
            // PongJob uses IJobParallelForDeferExtensions, we *must* use AsDeferredJobArray in order to access the
            // list from the job
#if ENABLE_IL2CPP
            // IJobParallelForDeferExtensions is not working correctly with IL2CPP
            connections = m_connections
#else
            connections = m_connections.AsDeferredJobArray()
#endif
        };
        // Update the driver should be the first job in the chain
        m_updateHandle = m_ServerDriver.ScheduleUpdate();
        // The DriverUpdateJob which accepts new connections should be the second job in the chain, it needs to depend
        // on the driver update job
        m_updateHandle = updateJob.Schedule(m_updateHandle);
        // PongJob uses IJobParallelForDeferExtensions, we *must* schedule with a list as first parameter rather than
        // an int since the job needs to pick up new connections from DriverUpdateJob
        // The PongJob is the last job in the chain and it must depends on the DriverUpdateJob
#if ENABLE_IL2CPP
        m_updateHandle = pongJob.Schedule(m_updateHandle);
#else
        m_updateHandle = pongJob.Schedule(m_connections, 1, m_updateHandle);
#endif
    }
}

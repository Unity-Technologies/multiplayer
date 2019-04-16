using Unity.Burst;
using UnityEngine;
using Unity.Networking.Transport;
using Unity.Collections;
using Unity.Jobs;

public class PingClientBehaviour : MonoBehaviour
{
    struct PendingPing
    {
        public int id;
        public float time;
    }

    private UdpNetworkDriver m_ClientDriver;
    private NativeArray<NetworkConnection> m_clientToServerConnection;
    // pendingPings is an array of pings sent to the server which have not yet received a response.
    // Currently we only support one ping in-flight
    private NativeArray<PendingPing> m_pendingPings;
    // The ping stats are two integers, time for last ping and number of pings
    private NativeArray<int> m_pingStats;

    private JobHandle m_updateHandle;

    void Start()
    {
        // Create a NetworkDriver for the client. We could bind to a specific address but in this case we rely on the
        // implicit bind since we do not need to bing to anything special
        m_ClientDriver = new UdpNetworkDriver(new INetworkParameter[0]);

        m_pendingPings = new NativeArray<PendingPing>(64, Allocator.Persistent);
        m_pingStats = new NativeArray<int>(2, Allocator.Persistent);
        m_clientToServerConnection = new NativeArray<NetworkConnection>(1, Allocator.Persistent);
    }

    void OnDestroy()
    {
        // All jobs must be completed before we can dispose the data they use
        m_updateHandle.Complete();
        m_ClientDriver.Dispose();
        m_pendingPings.Dispose();
        m_pingStats.Dispose();
        m_clientToServerConnection.Dispose();
    }

    [BurstCompile]
    struct PingJob : IJob
    {
        public UdpNetworkDriver driver;
        public NativeArray<NetworkConnection> connection;
        public NetworkEndPoint serverEP;
        public NativeArray<PendingPing> pendingPings;
        public NativeArray<int> pingStats;
        public float fixedTime;

        public void Execute()
        {
            // If the client ui indicates we should be sending pings but we do not have an active connection we create one
            if (serverEP.IsValid && !connection[0].IsCreated)
                connection[0] = driver.Connect(serverEP);
            // If the client ui indicates we should not be sending pings but we do have a connection we close that connection
            if (!serverEP.IsValid && connection[0].IsCreated)
            {
                connection[0].Disconnect(driver);
                connection[0] = default(NetworkConnection);
            }

            DataStreamReader strm;
            NetworkEvent.Type cmd;
            // Process all events on the connection. If the connection is invalid it will return Empty immediately
            while ((cmd = connection[0].PopEvent(driver, out strm)) != NetworkEvent.Type.Empty)
            {
                if (cmd == NetworkEvent.Type.Connect)
                {
                    // When we get the connect message we can start sending data to the server
                    // Set the ping id to a sequence number for the new ping we are about to send
                    pendingPings[0] = new PendingPing {id = pingStats[0], time = fixedTime};
                    // Create a 4 byte data stream which we can store our ping sequence number in
                    var pingData = new DataStreamWriter(4, Allocator.Temp);
                    pingData.Write(pingStats[0]);
                    connection[0].Send(driver, pingData);
                    // Update the number of sent pings
                    pingStats[0] = pingStats[0] + 1;
                }
                else if (cmd == NetworkEvent.Type.Data)
                {
                    // When the pong message is received we calculate the ping time and disconnect
                    pingStats[1] = (int) ((fixedTime - pendingPings[0].time) * 1000);
                    connection[0].Disconnect(driver);
                    connection[0] = default(NetworkConnection);
                }
                else if (cmd == NetworkEvent.Type.Disconnect)
                {
                    // If the server disconnected us we clear out connection
                    connection[0] = default(NetworkConnection);
                }
            }
        }
    }

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

        // Update the ping client UI with the ping statistics computed by teh job scheduled previous frame since that
        // is now guaranteed to have completed
        PingClientUIBehaviour.UpdateStats(m_pingStats[0], m_pingStats[1]);
        var pingJob = new PingJob
        {
            driver = m_ClientDriver,
            connection = m_clientToServerConnection,
            serverEP = PingClientUIBehaviour.ServerEndPoint,
            pendingPings = m_pendingPings,
            pingStats = m_pingStats,
            fixedTime = Time.fixedTime
        };
        // Schedule a chain with the driver update followed by the ping job
        m_updateHandle = m_ClientDriver.ScheduleUpdate();
        m_updateHandle = pingJob.Schedule(m_updateHandle);
    }
}

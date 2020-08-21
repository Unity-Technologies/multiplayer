using Unity.Burst;
using UnityEngine;
using Unity.Networking.Transport;
using Unity.Collections;
using Unity.Jobs;

public class CustomNetworkInterfaceConnect : MonoBehaviour
{
    const int ServerPort = 9000;
    struct PendingPing
    {
        public int id;
        public float time;
    }

    private NetworkDriver m_ClientDriver;
    private NetworkConnection m_clientToServerConnection;
    public NetworkDriver m_ServerDriver;
    private NativeList<NetworkConnection> m_serverConnections;
    // pendingPing is a ping sent to the server which have not yet received a response.
    private PendingPing m_pendingPing;
    // The ping stats are two integers, time for last ping and number of pings
    private int m_lastPingTime;
    private int m_numPingsSent;

    void Start()
    {
        // Create a NetworkDriver for the client. We could bind to a specific address but in this case we rely on the
        // implicit bind since we do not need to bing to anything special
        m_ClientDriver = new NetworkDriver(new UDPNetworkInterface());

        // Create the server driver, bind it to a port and start listening for incoming connections
        m_ServerDriver = new NetworkDriver(new UDPNetworkInterface());
        var addr = NetworkEndPoint.AnyIpv4;
        addr.Port = ServerPort;
        if (m_ServerDriver.Bind(addr) != 0)
            Debug.Log($"Failed to bind to port {ServerPort}");
        else
            m_ServerDriver.Listen();

        m_serverConnections = new NativeList<NetworkConnection>(16, Allocator.Persistent);
    }

    void OnDestroy()
    {
        m_ClientDriver.Dispose();

        m_ServerDriver.Dispose();
        m_serverConnections.Dispose();
    }

    void FixedUpdate()
    {
        ClientUpdate();
        ServerUpdate();
    }

    void ClientUpdate()
    {
        // Update the NetworkDriver. It schedules a job so we must wait for that job with Complete
        m_ClientDriver.ScheduleUpdate().Complete();

        // If the client ui indicates we should be sending pings but we do not have an active connection we create one
        if (!m_clientToServerConnection.IsCreated)
        {
            var serverEP = NetworkEndPoint.LoopbackIpv4;
            serverEP.Port = ServerPort;
            m_clientToServerConnection = m_ClientDriver.Connect(serverEP);
        }

        DataStreamReader strm;
        NetworkEvent.Type cmd;
        // Process all events on the connection. If the connection is invalid it will return Empty immediately
        while ((cmd = m_clientToServerConnection.PopEvent(m_ClientDriver, out strm)) != NetworkEvent.Type.Empty)
        {
            if (cmd == NetworkEvent.Type.Connect)
            {
                // When we get the connect message we can start sending data to the server
                // Set the ping id to a sequence number for the new ping we are about to send
                m_pendingPing = new PendingPing {id = m_numPingsSent, time = Time.fixedTime};
                // Create a 4 byte data stream which we can store our ping sequence number in
                var pingData = m_ClientDriver.BeginSend(m_clientToServerConnection);
                pingData.WriteInt(m_numPingsSent);
                m_ClientDriver.EndSend(pingData);
                // Update the number of sent pings
                ++m_numPingsSent;
            }
            else if (cmd == NetworkEvent.Type.Data)
            {
                // When the pong message is received we calculate the ping time and disconnect
                m_lastPingTime = (int) ((Time.fixedTime - m_pendingPing.time) * 1000);
                m_clientToServerConnection.Disconnect(m_ClientDriver);
                m_clientToServerConnection = default(NetworkConnection);
                UnityEngine.Debug.Log($"Ping: {m_lastPingTime}");
            }
            else if (cmd == NetworkEvent.Type.Disconnect)
            {
                // If the server disconnected us we clear out connection
                m_clientToServerConnection = default(NetworkConnection);
            }
        }
    }

    void ServerUpdate()
    {
        // Update the NetworkDriver. It schedules a job so we must wait for that job with Complete
        m_ServerDriver.ScheduleUpdate().Complete();

        // Accept all new connections
        while (true)
        {
            var con = m_ServerDriver.Accept();
            // "Nothing more to accept" is signaled by returning an invalid connection from accept
            if (!con.IsCreated)
                break;
            m_serverConnections.Add(con);
        }

        for (int i = 0; i < m_serverConnections.Length; ++i)
        {
            DataStreamReader strm;
            NetworkEvent.Type cmd;
            // Pop all events for the connection
            while ((cmd = m_ServerDriver.PopEventForConnection(m_serverConnections[i], out strm)) != NetworkEvent.Type.Empty)
            {
                if (cmd == NetworkEvent.Type.Data)
                {
                    // For ping requests we reply with a pong message
                    int id = strm.ReadInt();
                    // Create a temporary DataStreamWriter to keep our serialized pong message
                    var pongData = m_ServerDriver.BeginSend(m_serverConnections[i]);
                    pongData.WriteInt(id);
                    // Send the pong message with the same id as the ping
                    m_ServerDriver.EndSend(pongData);
                }
                else if (cmd == NetworkEvent.Type.Disconnect)
                {
                    // This connection no longer exist, remove it from the list
                    // The next iteration will operate on the new connection we swapped in so as long as it exist the
                    // loop can continue
                    m_serverConnections.RemoveAtSwapBack(i);
                    if (i >= m_serverConnections.Length)
                        break;
                }
            }
        }
    }
}

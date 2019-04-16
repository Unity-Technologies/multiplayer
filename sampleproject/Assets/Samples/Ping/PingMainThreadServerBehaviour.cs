using UnityEngine;
using Unity.Networking.Transport;
using Unity.Collections;
using Unity.Jobs;

public class PingMainThreadServerBehaviour : MonoBehaviour
{
    public UdpNetworkDriver m_ServerDriver;
    private NativeList<NetworkConnection> m_connections;

    private JobHandle m_updateHandle;

    void Start()
    {
        // Create the server driver, bind it to a port and start listening for incoming connections
        m_ServerDriver = new UdpNetworkDriver(new INetworkParameter[0]);
        var addr = NetworkEndPoint.AnyIpv4;
        addr.Port = 9000;
        if (m_ServerDriver.Bind(addr) != 0)
            Debug.Log("Failed to bind to port 9000");
        else
            m_ServerDriver.Listen();

        m_connections = new NativeList<NetworkConnection>(16, Allocator.Persistent);
    }

    void OnDestroy()
    {
        // All jobs must be completed before we can dispose the data they use
        m_updateHandle.Complete();
        m_ServerDriver.Dispose();
        m_connections.Dispose();
    }

    void FixedUpdate()
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
            m_connections.Add(con);
        }

        for (int i = 0; i < m_connections.Length; ++i)
        {
            DataStreamReader strm;
            NetworkEvent.Type cmd;
            // Pop all events for the connection
            while ((cmd = m_ServerDriver.PopEventForConnection(m_connections[i], out strm)) != NetworkEvent.Type.Empty)
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
                    m_ServerDriver.Send(NetworkPipeline.Null, m_connections[i], pongData);
                }
                else if (cmd == NetworkEvent.Type.Disconnect)
                {
                    // This connection no longer exist, remove it from the list
                    // The next iteration will operate on the new connection we swapped in so as long as it exist the
                    // loop can continue
                    m_connections.RemoveAtSwapBack(i);
                    if (i >= m_connections.Length)
                        break;
                }
            }
        }
    }
}

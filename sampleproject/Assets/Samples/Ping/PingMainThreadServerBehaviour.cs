using UnityEngine;
using Unity.Networking.Transport;
using Unity.Collections;
using Unity.Jobs;

public class PingMainThreadServerBehaviour : MonoBehaviour
{
    public NetworkDriver m_ServerDriver;
    private NativeList<NetworkConnection> m_connections;

    void Start()
    {
        // Create the server driver, bind it to a port and start listening for incoming connections
        m_ServerDriver = NetworkDriver.Create();
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
                    int id = strm.ReadInt();
                    // Create a temporary DataStreamWriter to keep our serialized pong message
                    var pongData = m_ServerDriver.BeginSend(m_connections[i]);
                    pongData.WriteInt(id);
                    // Send the pong message with the same id as the ping
                    m_ServerDriver.EndSend(pongData);
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

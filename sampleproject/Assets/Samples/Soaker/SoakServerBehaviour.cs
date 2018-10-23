using Unity.Collections;
using UnityEngine;

using UdpCNetworkDriver = Unity.Networking.Transport.BasicNetworkDriver<Unity.Networking.Transport.IPv4UDPSocket>;

public class SoakServerBehaviour : MonoBehaviour
{
    private SoakServer m_Server;

    void Start()
    {
        m_Server = new SoakServer();
        m_Server.Start();
    }

    void OnDestroy()
    {
        m_Server.Dispose();
    }

    void FixedUpdate()
    {
        m_Server.Update();
    }
}

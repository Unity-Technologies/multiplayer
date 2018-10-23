using System;
using System.Net;
using UnityEngine;
using Unity.Networking.Transport;
using Unity.Collections;
using Unity.Jobs;

using NetworkConnection = Unity.Networking.Transport.NetworkConnection;
using UdpCNetworkDriver = Unity.Networking.Transport.BasicNetworkDriver<Unity.Networking.Transport.IPv4UDPSocket>;

public class SoakServer : IDisposable
{
    private UdpCNetworkDriver m_ServerDriver;
    private int m_Tick;

    private NativeList<SoakClientCtx> m_Connections;
    private JobHandle m_UpdateHandle;

    private NativeArray<int> m_PendingDisconnects;

    public void Start()
    {
        m_Connections = new NativeList<SoakClientCtx>(1, Allocator.Persistent);
        m_ServerDriver = new UdpCNetworkDriver(new INetworkParameter[0]);
        if (m_ServerDriver.Bind(new IPEndPoint(IPAddress.Any, 9000)) != 0)
            Debug.Log("Failed to bind to port 9000");
        else
            m_ServerDriver.Listen();
    }

    public void Update()
    {
        m_Tick++;
        m_UpdateHandle.Complete();

        if (m_PendingDisconnects.IsCreated)
        {
            m_PendingDisconnects.Dispose();
        }

        var acceptJob = new SoakServerAcceptJob
        {
            now = m_Tick,
            driver = m_ServerDriver,
            connections = m_Connections
        };
        var soakJob = new SoakServerUpdateClientsJob
        {
            driver = m_ServerDriver.ToConcurrent(),
            connections = m_Connections.ToDeferredJobArray()
        };

        m_UpdateHandle = m_ServerDriver.ScheduleUpdate();
        m_UpdateHandle = acceptJob.Schedule(m_UpdateHandle);
        m_UpdateHandle = soakJob.Schedule(m_Connections, 1, m_UpdateHandle);
    }

    public void Dispose()
    {
        m_UpdateHandle.Complete();
        m_ServerDriver.Dispose();
        m_Connections.Dispose();
    }
}

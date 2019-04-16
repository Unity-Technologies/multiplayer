using System;
using UnityEngine;
using Unity.Networking.Transport;
using Unity.Collections;
using Unity.Jobs;
using Unity.Networking.Transport.Utilities;

public class SoakServer : IDisposable
{
    private UdpNetworkDriver m_ServerDriver;
    private NetworkPipeline m_Pipeline;
    private int m_Tick;
    private double m_NextStatsPrint;

    private NativeList<SoakClientCtx> m_Connections;
    private JobHandle m_UpdateHandle;

    private NativeArray<int> m_PendingDisconnects;

    public void Start()
    {
        m_Connections = new NativeList<SoakClientCtx>(1, Allocator.Persistent);
        m_ServerDriver = new UdpNetworkDriver(new ReliableUtility.Parameters { WindowSize = 32 });
        //m_Pipeline = m_ServerDriver.CreatePipeline(typeof(UnreliableSequencedPipelineStage));
        m_Pipeline = m_ServerDriver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
        var addr = NetworkEndPoint.AnyIpv4;
        addr.Port = 9000;
        if (m_ServerDriver.Bind(addr) != 0)
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
            pipeline = m_Pipeline,
            connections = m_Connections.AsDeferredJobArray()
        };

        /*var time = Time.fixedTime;
        if (time > m_NextStatsPrint)
        {
            PrintStatistics();
            m_NextStatsPrint = time + 10;
        }*/

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

    void PrintStatistics()
    {
        for (int i = 0; i < m_Connections.Length; i++)
        {
            Debug.Log("Server dumping stats for client " + i);
            Util.DumpReliabilityStatistics(m_ServerDriver, m_Pipeline, m_Connections[i].Connection);
        }
    }
}

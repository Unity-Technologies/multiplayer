using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using Unity.Jobs;
using Unity.Networking.Transport;
using UnityEngine.Assertions;

public class SoakClientJobManager : IDisposable
{
    private List<SoakClient> m_SoakClients;
    private NativeArray<SoakStatisticsPoint> m_Samples;
    private int m_FrameId;
    private bool m_Started;


    public SoakClientJobManager(int clientCount, int packetsPerSecond, int packetSize, int duration)
    {
        m_SoakClients = new List<SoakClient>(clientCount);
        m_Samples = new NativeArray<SoakStatisticsPoint>(clientCount, Allocator.Persistent);

        for (int i = 0; i < clientCount; ++i)
        {
            m_SoakClients.Add(new SoakClient(packetsPerSecond > 0 ? 1.0 / packetsPerSecond : Time.fixedDeltaTime, packetSize, duration));
        }
    }

    public string[] ClientInfos()
    {
        var infos = new string[m_SoakClients.Count];

        for (int i = 0; i < infos.Length; i++)
        {
            infos[i] = "Client " + i;
        }
        return infos;
    }

    public void Start()
    {
        Debug.Log("Soak test initiated");

        var endpoint = NetworkEndPoint.LoopbackIpv4;
        endpoint.Port = 9000;
        if (!m_Started)
        {
            foreach (var client in m_SoakClients)
            {
                client.Start(endpoint);
            }

            m_Started = true;
        }
    }

    public void Stop()
    {
        m_Started = false;
    }

    public bool Done()
    {
        int done = 0;
        foreach (var client in m_SoakClients)
        {
            done |= client.SoakJobContextsHandle[0].Done;
        }
        return done == 1;
    }

    public void Update()
    {
        m_FrameId++;
        if (!m_Started)
            return;

        foreach (var client in m_SoakClients)
        {
            var soakJob = new SoakClientJob
            {
                driver = client.DriverHandle,
                pipeline = client.Pipeline,
                connection = client.ConnectionHandle,
                streamWriter = client.SoakClientStreamWriter,
                serverEP = client.ServerEndPoint,
                pendingSoaks = client.PendingSoakMessages,
                fixedTime = Time.fixedTime,

                deltaTime = Time.fixedDeltaTime,
                frameId = m_FrameId,
                timestamp = System.Diagnostics.Stopwatch.GetTimestamp() / TimeSpan.TicksPerMillisecond,

                packetData = client.SoakJobDataPacket,
                jobContext = client.SoakJobContextsHandle,
                jobStatistics = client.SoakStatisticsHandle
            };

            client.UpdateHandle = client.DriverHandle.ScheduleUpdate();
            client.UpdateHandle = soakJob.Schedule(client.UpdateHandle);
        }
    }

    public NativeSlice<SoakStatisticsPoint> Sample()
    {
        Assert.AreEqual(m_Samples.Length, m_SoakClients.Count);

        for (int i = 0; i < m_Samples.Length; i++)
        {
            m_Samples[i] = m_SoakClients[i].Sample();
        }

        return m_Samples;
    }

    public void Dispose()
    {
        foreach (var client in m_SoakClients)
        {
            client.Dispose();
        }
        m_SoakClients = null;
        m_Samples.Dispose();
    }

    public void Sync()
    {
        foreach (var client in m_SoakClients)
        {
            client.UpdateHandle.Complete();
            client.Update();
        }
    }

}

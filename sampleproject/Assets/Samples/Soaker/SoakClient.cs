using System;
using System.Net;
using UnityEngine;
using Unity.Networking.Transport;
using Unity.Collections;
using Unity.Jobs;

using Random = System.Random;
using NetworkConnection = Unity.Networking.Transport.NetworkConnection;
using UdpCNetworkDriver = Unity.Networking.Transport.BasicNetworkDriver<Unity.Networking.Transport.IPv4UDPSocket>;

public class SoakClient : IDisposable
{
    public UdpCNetworkDriver DriverHandle;
    public NetworkEndPoint ServerEndPoint;
    public string CustomIp = "";

    public NativeArray<NetworkConnection> ConnectionHandle;
    public NativeArray<SoakMessage> PendingSoakMessages;
    public DataStreamWriter SoakClientStreamWriter;
    public JobHandle UpdateHandle;

    public NativeArray<byte> SoakJobDataPacket;
    public NativeArray<SoakJobContext> SoakJobContextsHandle;
    public NativeArray<SoakStatisticsPoint> SoakStatisticsHandle;

    public SoakClient(double sendInterval, int packetSize, int duration)
    {
        DriverHandle = new UdpCNetworkDriver(new INetworkParameter[0]);
        if (packetSize > NetworkParameterConstants.MTU)
        {
            Debug.LogWarning("Trunkating packet size to MTU");
            packetSize = NetworkParameterConstants.MTU;
        }
        else if (packetSize < SoakMessage.HeaderLength)
        {
            Debug.LogWarning("Packet size was to small resizing to at least SoakMessage HeaderSize");
            packetSize = SoakMessage.HeaderLength;
        }
        var payloadSize = packetSize - SoakMessage.HeaderLength;
        SoakClientStreamWriter = new DataStreamWriter(packetSize, Allocator.Persistent);

        PendingSoakMessages = new NativeArray<SoakMessage>(64, Allocator.Persistent);
        ConnectionHandle = new NativeArray<NetworkConnection>(1, Allocator.Persistent);

        SoakJobDataPacket = new NativeArray<byte>(payloadSize, Allocator.Persistent);
        var random = new byte[payloadSize];
        Random r = new Random();
        r.NextBytes(random);
        SoakJobDataPacket.CopyFrom(random);

        SoakJobContextsHandle = new NativeArray<SoakJobContext>(1, Allocator.Persistent);
        var context = new SoakJobContext
        {
            Duration = duration,
            PacketSize = packetSize,
            SendInterval = sendInterval
        };
        SoakJobContextsHandle[0] = context;

        SoakStatisticsHandle = new NativeArray<SoakStatisticsPoint>(1, Allocator.Persistent);
        SoakStatisticsHandle[0] = new SoakStatisticsPoint();
    }

    public void Start(EndPoint endpoint)
    {
        ServerEndPoint = endpoint;
    }

    public SoakStatisticsPoint Sample()
    {
        var sample = SoakStatisticsHandle[0];
        SoakStatisticsHandle[0] = new SoakStatisticsPoint();
        sample.PingTimeMean = sample.PingTimeMean / sample.PingTimeMeanCount;
        return sample;
    }

    public void Dispose()
    {
        UpdateHandle.Complete();
        DriverHandle.Dispose();
        ConnectionHandle.Dispose();
        PendingSoakMessages.Dispose();
        SoakClientStreamWriter.Dispose();
        SoakJobDataPacket.Dispose();
        SoakJobContextsHandle.Dispose();
        SoakStatisticsHandle.Dispose();
    }

    public void Update()
    {
    }
}

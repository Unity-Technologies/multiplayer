using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using Unity.Networking.Transport;
using Unity.Collections;
using UnityEngine;
using UdpCNetworkDriver = Unity.Networking.Transport.BasicNetworkDriver<Unity.Networking.Transport.IPv4UDPSocket>;

[StructLayout(LayoutKind.Explicit)]
public unsafe struct SoakMessage
{
    public const int Capacity = NetworkParameterConstants.MTU;
    public const int HeaderLength = 6 * sizeof(int) + sizeof(float);

    [FieldOffset(0)] public fixed byte data[Capacity];
    [FieldOffset(0)] public int type;

    [FieldOffset(4)] public int sequence;
    [FieldOffset(8)] public int ack;
    [FieldOffset(12)] public int ackBuffer;

    [FieldOffset(16)] public int id;
    [FieldOffset(20)] public float time;
    [FieldOffset(24)] public int length;

    [FieldOffset(28)] public fixed byte payload[Capacity - HeaderLength];
}

public struct SoakJobContext
{
    public int FrameId;
    public double StartedAt;
    public double Accumulator;
    public double TimeStep;

    public int Connected;

    public int NextSequenceNumber;
    public int LatestReceivedSequenceNumber;

    public int PacketSize;
    public double SendInterval;
    public int Duration;
    public int Done;
}

public struct NetworkDriverStatisticsPoint
{
    public float Timestamp;

    public float SentPackets;
    public float ReceivedPackets;
    public float DroppedOrStalePackets;
    public float SentBytes;
    public float ReceivedBytes;
}

public struct SoakStatisticsPoint
{
    public int PingTimeMeanCount;
    public float Timestamp;
    public float PingTimeMean;
    public float SentPackets;
    public float ReceivedPackets;
    public float DroppedOrStalePackets;
    public float SentBytes;
    public float ReceivedBytes;
}

public class StatisticsReport : IDisposable
{
    public NativeList<SoakStatisticsPoint> Samples;
    public int BucketSize;

    public void AddSample(SoakStatisticsPoint point, float now)
    {
        point.Timestamp = now;
        Samples.Add(point);
    }

    public StatisticsReport(int bucketSize)
    {
        BucketSize = bucketSize;
        Samples = new NativeList<SoakStatisticsPoint>(Allocator.Persistent);
    }

    public void Dispose()
    {
        if (Samples.IsCreated)
            Samples.Dispose();
    }
}

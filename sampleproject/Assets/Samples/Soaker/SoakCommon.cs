using System;
using System.Runtime.InteropServices;
using Unity.Networking.Transport;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Networking.Transport.Utilities;

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
    public double NextStatsPrint;

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

    // Reliability pipeline
    public float ReliableDropped;
    public float ReliableSent;
    public float ReliableReceived;
    public float ReliableResent;
    public float ReliableDuplicate;
    public float ReliableRTT;
    public float ReliableSRTT;
    public float ReliableMaxRTT;
    public float ReliableMaxProcessingTime;
    public float ReliableResendQueue;
    public float ReliableOldestResendPacketAge;
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

public static class Util
{
    public static unsafe void DumpReliabilityStatistics(UdpNetworkDriver driver, NetworkPipeline pipeline, NetworkConnection con)
    {
        NativeSlice<byte> receiveBuffer = default;
        NativeSlice<byte> sendBuffer = default;
        NativeSlice<byte> sharedBuffer = default;
        driver.GetPipelineBuffers(pipeline, 4, con, ref receiveBuffer, ref sendBuffer, ref sharedBuffer);
        /*var relCtx = (ReliableUtility.SharedContext*)sharedBuffer.GetUnsafeReadOnlyPtr();
        var sendCtx = (ReliableUtility.Context*)sendBuffer.GetUnsafeReadOnlyPtr();
        UnityEngine.Debug.Log("Reliability stats\nPacketsDropped: " + relCtx->stats.PacketsDropped + "\n" +
                  "PacketsDuplicated: " + relCtx->stats.PacketsDuplicated + "\n" +
                  "PacketsOutOfOrder: " + relCtx->stats.PacketsOutOfOrder + "\n" +
                  "PacketsReceived: " + relCtx->stats.PacketsReceived + "\n" +
                  "PacketsResent: " + relCtx->stats.PacketsResent + "\n" +
                  "PacketsSent: " + relCtx->stats.PacketsSent + "\n" +
                  "PacketsStale: " + relCtx->stats.PacketsStale + "\n" +
                  "Last received remote seqId: " + relCtx->ReceivedPackets.Sequence + "\n" +
                  "Last received remote ackMask: " + SequenceHelpers.BitMaskToString(relCtx->ReceivedPackets.AckMask) + "\n" +
                  "Last sent seqId: " + (relCtx->SentPackets.Sequence - 1)+ "\n" +
                  "Last acked seqId: " + relCtx->SentPackets.Acked + "\n" +
                  "Last ackmask: " + SequenceHelpers.BitMaskToString(relCtx->SentPackets.AckMask));*/
    }

    public static unsafe void GatherReliabilityStats(ref SoakStatisticsPoint stats, ref SoakStatisticsPoint lastStats, UdpNetworkDriver driver,
        NetworkPipeline pipeline, NetworkConnection con, long timestamp)
    {
        NativeSlice<byte> receiveBuffer = default;
        NativeSlice<byte> sendBuffer = default;
        NativeSlice<byte> sharedBuffer = default;
        driver.GetPipelineBuffers(pipeline, 4, con, ref receiveBuffer, ref sendBuffer, ref sharedBuffer);

        var sharedCtx = (ReliableUtility.SharedContext*)sharedBuffer.GetUnsafeReadOnlyPtr();
        stats.ReliableSent += sharedCtx->stats.PacketsSent - lastStats.ReliableSent;
        //Console.WriteLine("sharedCtx->stats.PacketsSent=" + sharedCtx->stats.PacketsSent + " lastStats.ReliableSent=" + lastStats.ReliableSent + " stats.ReliableSent=" + stats.ReliableSent);
        stats.ReliableResent += sharedCtx->stats.PacketsResent - lastStats.ReliableResent;
        stats.ReliableDropped += sharedCtx->stats.PacketsDropped - lastStats.ReliableDropped;
        stats.ReliableReceived += sharedCtx->stats.PacketsReceived - lastStats.ReliableReceived;
        stats.ReliableDuplicate += sharedCtx->stats.PacketsDuplicated - lastStats.ReliableDuplicate;
        stats.ReliableRTT = sharedCtx->RttInfo.LastRtt;
        stats.ReliableSRTT = sharedCtx->RttInfo.SmoothedRtt;
        int resendQueueSize = 0;
        int oldestResendPacketAge = 0;
        int maxRtt = 0;
        int maxProcessingTime = 0;
        GatherExtraStats(sendBuffer, sharedBuffer, timestamp, ref resendQueueSize, ref oldestResendPacketAge, ref maxRtt, ref maxProcessingTime);
        stats.ReliableResendQueue = resendQueueSize;
        stats.ReliableOldestResendPacketAge = oldestResendPacketAge;
        stats.ReliableMaxRTT = maxRtt;
        stats.ReliableMaxProcessingTime = maxProcessingTime;

        lastStats.ReliableSent = sharedCtx->stats.PacketsSent;
        lastStats.ReliableResent = sharedCtx->stats.PacketsResent;
        lastStats.ReliableDropped = sharedCtx->stats.PacketsDropped;
        lastStats.ReliableReceived = sharedCtx->stats.PacketsReceived;
        lastStats.ReliableDuplicate = sharedCtx->stats.PacketsDuplicated;
    }

    static unsafe void GatherExtraStats(NativeSlice<byte> sendBuffer, NativeSlice<byte> sharedBuffer, long timestamp, ref int usedCount, ref int oldestAge, ref int maxRtt, ref int maxProcessingTime)
    {
        var ptr = (byte*)sendBuffer.GetUnsafePtr();
        var ctx = (ReliableUtility.Context*) ptr;
        var shared = (ReliableUtility.SharedContext*) sharedBuffer.GetUnsafePtr();

        for (int i = 0; i < shared->WindowSize; i++)
        {
            var seqId = (int*) (ptr + ctx->IndexPtrOffset + i * ctx->IndexStride);
            if (*seqId != -1)
            {
                usedCount++;
                var packetInfo = ReliableUtility.GetPacketInformation(sendBuffer, *seqId);
                oldestAge = Math.Max(oldestAge, (int)(timestamp - packetInfo->SendTime));
            }

            var timingData = ReliableUtility.GetLocalPacketTimer(sharedBuffer, (ushort)i);
            if (timingData->SentTime > 0 && timingData->ReceiveTime > 0)
                maxRtt = Math.Max(maxRtt, (int)(timingData->ReceiveTime - timingData->SentTime - timingData->ProcessingTime));

            maxProcessingTime = Math.Max(maxProcessingTime, timingData->ProcessingTime);
        }
    }
}

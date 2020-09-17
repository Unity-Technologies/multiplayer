using Unity.Burst;
using Unity.Networking.Transport;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

[BurstCompile]
public struct SoakClientJob : IJob
{
    public NetworkDriver driver;
    public NetworkPipeline pipeline;
    public NetworkPipelineStageId reliableStageId;
    public NetworkPipelineStageId simulatorStageId;
    public NativeArray<NetworkConnection> connection;

    [ReadOnly] public NativeArray<byte> packetData;

    public NativeArray<SoakJobContext> jobContext;
    public NativeArray<SoakStatisticsPoint> jobStatistics;
    public NativeArray<SoakMessage> pendingSoaks;

    public float fixedTime;
    public double deltaTime;
    public int frameId;
    public long timestamp;

    public unsafe void SendPacket(ref SoakStatisticsPoint stats, ref SoakJobContext ctx)
    {
        var message = new SoakMessage
        {
            id = ctx.FrameId,
            time = fixedTime,

            sequence = ctx.NextSequenceNumber++,
            length = packetData.Length
        };
        var streamWriter = driver.BeginSend(pipeline, connection[0]);

        streamWriter.WriteBytes(message.data, SoakMessage.HeaderLength);
        streamWriter.WriteBytes((byte*)packetData.GetUnsafeReadOnlyPtr(), packetData.Length);

        stats.SentBytes += driver.EndSend(streamWriter);
        stats.SentPackets++;

        pendingSoaks[message.id % pendingSoaks.Length] = message;
    }




    public unsafe void Execute()
    {
        var ctx = jobContext[0];
        var stats = jobStatistics[0];
        var lastStats = jobStatistics[1];

        if (ctx.Done == 1 || !connection[0].IsCreated)
            return;

        DataStreamReader strm;
        NetworkEvent.Type cmd;

        SoakMessage inbound = default(SoakMessage);
        while ((cmd = connection[0].PopEvent(driver, out strm)) != NetworkEvent.Type.Empty)
        {
            if (cmd == NetworkEvent.Type.Connect)
            {
                if (ctx.StartedAt == default(float))
                {
                    ctx.StartedAt = fixedTime;
                    ctx.Connected = 1;
                }
            }
            else if (cmd == NetworkEvent.Type.Data)
            {
                stats.ReceivedBytes += strm.Length;
                strm.ReadBytes(inbound.data, strm.Length);

                if (inbound.sequence > ctx.LatestReceivedSequenceNumber)
                {
                    ctx.LatestReceivedSequenceNumber = inbound.sequence;
                    stats.ReceivedPackets += 1;
                }
                else
                    stats.DroppedOrStalePackets += 1;

                stats.PingTimeMean += (fixedTime - pendingSoaks[inbound.id % pendingSoaks.Length].time) * 1000;
                stats.PingTimeMeanCount++;
            }
            else if (cmd == NetworkEvent.Type.Disconnect)
            {
                connection[0] = default(NetworkConnection);
                break;
            }
        }

        if (ctx.Connected == 0)
            return;

        if (fixedTime - ctx.StartedAt > ctx.Duration)
        {
            ctx.Done = 1;
        }

        if (fixedTime > ctx.NextStatsPrint)
        {
            ctx.NextStatsPrint = fixedTime + 10;
            //DumpSimulatorStatistics();
            //Util.DumpReliabilityStatistics(driver, pipeline, connection[0]);
        }

        ctx.Accumulator += deltaTime;

        while (ctx.Connected == 1 && ctx.Done == 0 && ctx.Accumulator >= ctx.SendInterval)
        {
            ctx.FrameId++;
            ctx.Accumulator -= ctx.SendInterval;
            SendPacket(ref stats, ref ctx);
        }

        Util.GatherReliabilityStats(ref stats, ref lastStats, driver, pipeline, reliableStageId, connection[0], timestamp);

        jobContext[0] = ctx;
        jobStatistics[0] = stats;
        jobStatistics[1] = lastStats;
    }
}

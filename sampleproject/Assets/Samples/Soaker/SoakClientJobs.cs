using Unity.Burst;
using Unity.Networking.Transport;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

[BurstCompile]
public struct SoakClientJob : IJob
{
    public UdpNetworkDriver driver;
    public NetworkPipeline pipeline;
    public NativeArray<NetworkConnection> connection;
    public DataStreamWriter streamWriter;
    public NetworkEndPoint serverEP;

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
        streamWriter.Clear();

        streamWriter.WriteBytes(message.data, SoakMessage.HeaderLength);
        streamWriter.WriteBytes((byte*)packetData.GetUnsafeReadOnlyPtr(), packetData.Length);

        stats.SentBytes += connection[0].Send(driver, pipeline, streamWriter);
        stats.SentPackets++;

        pendingSoaks[message.id % pendingSoaks.Length] = message;
    }

    public unsafe void DumpSimulatorStatistics()
    {
        NativeSlice<byte> receiveBuffer = default;
        NativeSlice<byte> sendBuffer = default;
        NativeSlice<byte> sharedBuffer = default;
        driver.GetPipelineBuffers(pipeline, 0, connection[0], ref receiveBuffer, ref sendBuffer, ref sharedBuffer);
        /*var simCtx = (Simulator.Context*)sharedBuffer.GetUnsafeReadOnlyPtr();
        Debug.Log("Simulator stats\n" +
            "PacketCount: " + simCtx->PacketCount + "\n" +
            "PacketDropCount: " + simCtx->PacketDropCount + "\n" +
            "MaxPacketCount: " + simCtx->MaxPacketCount + "\n" +
            "ReadyPackets: " + simCtx->ReadyPackets + "\n" +
            "WaitingPackets: " + simCtx->WaitingPackets + "\n" +
            "NextPacketTime: " + simCtx->NextPacketTime + "\n" +
            "StatsTime: " + simCtx->StatsTime);*/
    }



    public unsafe void Execute()
    {
        if (serverEP.IsValid && !connection[0].IsCreated)
            connection[0] = driver.Connect(serverEP);
        else if (!serverEP.IsValid && connection[0].IsCreated)
            connection[0].Disconnect(driver);

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
                /*var state =*/ connection[0].GetState(driver);

                stats.ReceivedBytes += strm.Length;
                var readerCtx = default(DataStreamReader.Context);
                strm.ReadBytes(ref readerCtx, inbound.data, strm.Length);

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

        if (fixedTime - ctx.StartedAt > ctx.Duration)
        {
            ctx.Done = 1;
            connection[0].Disconnect(driver);
            Util.DumpReliabilityStatistics(driver, pipeline, connection[0]);
            DumpSimulatorStatistics();
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

        Util.GatherReliabilityStats(ref stats, ref lastStats, driver, pipeline, connection[0], timestamp);

        jobContext[0] = ctx;
        jobStatistics[0] = stats;
        jobStatistics[1] = lastStats;
    }
}

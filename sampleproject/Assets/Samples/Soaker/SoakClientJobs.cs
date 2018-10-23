using Unity.Networking.Transport;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Assertions;
using NetworkConnection = Unity.Networking.Transport.NetworkConnection;
using UdpCNetworkDriver = Unity.Networking.Transport.BasicNetworkDriver<Unity.Networking.Transport.IPv4UDPSocket>;

public struct SoakClientJob : IJob
{
    public UdpCNetworkDriver driver;
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

        stats.SentBytes += connection[0].Send(driver, streamWriter);
        stats.SentPackets++;

        pendingSoaks[message.id % pendingSoaks.Length] = message;
    }

    public unsafe void Execute()
    {
        if (serverEP.IsValid && !connection[0].IsCreated)
            connection[0] = driver.Connect(serverEP);
        else if (!serverEP.IsValid && connection[0].IsCreated)
            connection[0].Disconnect(driver);

        var ctx = jobContext[0];
        var stats = jobStatistics[0];

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

                if (!strm.IsCreated)
                {
                    Debug.Log("stream failed?");
                    return;
                }

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
        }

        ctx.Accumulator += deltaTime;

        while (ctx.Connected == 1 && ctx.Done == 0 && ctx.Accumulator >= ctx.SendInterval)
        {
            ctx.FrameId++;
            ctx.Accumulator -= ctx.SendInterval;
            SendPacket(ref stats, ref ctx);
        }

        jobContext[0] = ctx;
        jobStatistics[0] = stats;
    }
}

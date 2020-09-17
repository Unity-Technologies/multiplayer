using System;
using UnityEngine;
using Unity.Networking.Transport;
using Unity.Collections;
using Unity.Jobs;
using Random = System.Random;
using Unity.Networking.Transport.Utilities;

public class SoakClient : IDisposable
{
    public NetworkDriver DriverHandle;
    public NetworkPipeline Pipeline;
    public NetworkPipelineStageId ReliableStageId;
    public NetworkPipelineStageId SimulatorStageId;
    public NetworkEndPoint ServerEndPoint;
    public string CustomIp = "";

    public NativeArray<NetworkConnection> ConnectionHandle;
    public NativeArray<SoakMessage> PendingSoakMessages;
    public JobHandle UpdateHandle;

    public NativeArray<byte> SoakJobDataPacket;
    public NativeArray<SoakJobContext> SoakJobContextsHandle;
    public NativeArray<SoakStatisticsPoint> SoakStatisticsHandle;

    public SoakClient(double sendInterval, int packetSize, int duration)
    {
        DriverHandle = NetworkDriver.Create(
            new SimulatorUtility.Parameters
            {
                MaxPacketSize = packetSize, MaxPacketCount = 30, PacketDelayMs = 25,
                PacketDropPercentage = 10 /*PacketDropInterval = 100*/
            }, new ReliableUtility.Parameters {WindowSize = 32});
        //Pipeline = DriverHandle.CreatePipeline(typeof(UnreliableSequencedPipelineStage), typeof(SimulatorPipelineStage));
        Pipeline = DriverHandle.CreatePipeline(typeof(ReliableSequencedPipelineStage), typeof(SimulatorPipelineStage));
        ReliableStageId = NetworkPipelineStageCollection.GetStageId(typeof(ReliableSequencedPipelineStage));
        SimulatorStageId = NetworkPipelineStageCollection.GetStageId(typeof(SimulatorPipelineStage));
        if (packetSize > NetworkParameterConstants.MTU)
        {
            Debug.LogWarning("Truncating packet size to MTU");
            packetSize = NetworkParameterConstants.MTU;
        }
        else if (packetSize < SoakMessage.HeaderLength)
        {
            Debug.LogWarning("Packet size was to small resizing to at least SoakMessage HeaderSize");
            packetSize = SoakMessage.HeaderLength;
        }

        var payloadSize = packetSize - SoakMessage.HeaderLength;

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

        SoakStatisticsHandle = new NativeArray<SoakStatisticsPoint>(2, Allocator.Persistent);
        SoakStatisticsHandle[0] = new SoakStatisticsPoint();
        SoakStatisticsHandle[1] = new SoakStatisticsPoint();
    }

    public void Start(NetworkEndPoint endpoint)
    {
        ServerEndPoint = endpoint;
        //Reset the context
        var ctx = SoakJobContextsHandle[0];
        SoakJobContextsHandle[0] = new SoakJobContext
        {
            Duration = ctx.Duration,
            PacketSize = ctx.PacketSize,
            SendInterval = ctx.SendInterval
        };
        ConnectionHandle[0] = default(NetworkConnection);
    }

    public void Stop()
    {
        UpdateHandle.Complete();
        ConnectionHandle[0].Disconnect(DriverHandle);
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
        SoakJobDataPacket.Dispose();
        SoakJobContextsHandle.Dispose();
        SoakStatisticsHandle.Dispose();
    }

    public void PreUpdate()
    {
        if(SoakJobContextsHandle[0].Done == 1)
        {
            ConnectionHandle[0].Disconnect(DriverHandle);
            Util.DumpReliabilityStatistics(DriverHandle, Pipeline, ReliableStageId, ConnectionHandle[0]);
            DumpSimulatorStatistics();
        }

        if (ServerEndPoint.IsValid && !ConnectionHandle[0].IsCreated)
            ConnectionHandle[0] = DriverHandle.Connect(ServerEndPoint);
        else if (!ServerEndPoint.IsValid && ConnectionHandle[0].IsCreated)
            ConnectionHandle[0].Disconnect(DriverHandle);
    }

    public unsafe void DumpSimulatorStatistics()
    {
        DriverHandle.GetPipelineBuffers(Pipeline, SimulatorStageId, ConnectionHandle[0], out var receiveBuffer, out var sendBuffer, out var sharedBuffer);
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
    public void Update()
    {
        PreUpdate();
    }
}

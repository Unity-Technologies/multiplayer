using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[UpdateInGroup(typeof(ClientSimulationSystemGroup))]
[UpdateBefore(typeof(GhostReceiveSystemGroup))]
[UpdateAfter(typeof(NetworkStreamReceiveSystem))]
public class NetworkTimeSystem : ComponentSystem
{
    public static uint TimestampMS => (uint)(System.Diagnostics.Stopwatch.GetTimestamp() / System.TimeSpan.TicksPerMillisecond);
    public static uint interpolateTargetTick;
    public static uint predictTargetTick;

    private int interpolateDelta;


    private ComponentGroup connectionGroup;
    private NativeArray<uint> receiveHistory;
    private NativeArray<uint> rttHistory;
    private int receiveHistoryPos;
    private int rttHistoryPos;
    private bool resetHistory;

    private const int KSimTickRate = 60;
    private const int KNetTickRate = 60;
    private const int KInterpolationTimeNetTicks = 2;
    private const int KInterpolationTimeMS = 0;

    private const int KSnapshotHistorySize = 32;
    private const int KRTTHistorySize = 8;
    private const int KSnapshotHistoryMedianDiscard = 4;
    private const int KRTTHistoryMedianDiscard = 2;
    protected override void OnCreateManager()
    {
        connectionGroup = GetComponentGroup(ComponentType.ReadOnly<NetworkSnapshotAck>());
        receiveHistory = new NativeArray<uint>(KSnapshotHistorySize, Allocator.Persistent);
        rttHistory = new NativeArray<uint>(KRTTHistorySize, Allocator.Persistent);
        resetHistory = true;
    }

    protected override void OnDestroyManager()
    {
        receiveHistory.Dispose();
        rttHistory.Dispose();
    }

    protected override void OnUpdate()
    {
        if (connectionGroup.IsEmptyIgnoreFilter)
            return;
        var connections = connectionGroup.ToComponentDataArray<NetworkSnapshotAck>(Allocator.TempJob);
        var ack = connections[0];
        connections.Dispose();
        // What we expect to have this frame based on what was the most recent received previous frames
        if (resetHistory)
        {
            if (ack.LastReceivedSnapshotByLocal == 0)
                return;
            for (int i = 0; i < receiveHistory.Length; ++i)
                receiveHistory[i]  = ack.LastReceivedSnapshotByLocal;

            for (int i = 0; i < rttHistory.Length; ++i)
                rttHistory[i]  = ack.LastReceivedRTT;
        }
        else
        {
            for (int i = 0; i < receiveHistory.Length; ++i)
                receiveHistory[i] = receiveHistory[i] + 1;
            if (receiveHistory[receiveHistoryPos] != ack.LastReceivedSnapshotByLocal)
            {
                receiveHistoryPos = (receiveHistoryPos + 1) % receiveHistory.Length;
                receiveHistory[receiveHistoryPos] = ack.LastReceivedSnapshotByLocal;
                
                rttHistoryPos = (rttHistoryPos + 1) % rttHistory.Length;
                rttHistory[rttHistoryPos] = ack.LastReceivedRTT;
            }
        }

        uint averageRTT = AverageWithoutExtremes(rttHistory, KRTTHistoryMedianDiscard);
        uint expected = AverageWithoutExtremes(receiveHistory, KSnapshotHistoryMedianDiscard);
        // Interpolation time is network tick rate times 2, round up to even number of sim ticks
        uint interpolationTimeMS = KInterpolationTimeMS;
        if (interpolationTimeMS == 0)
            interpolationTimeMS = (1000*KInterpolationTimeNetTicks + KNetTickRate - 1) / KNetTickRate;
        uint interpolationFrames = (interpolationTimeMS * KSimTickRate + 999) / 1000;
        var curInterpolateTargetTick = expected - interpolationFrames;
        predictTargetTick = expected + 1 + (averageRTT * KSimTickRate + 999) / 1000;

        ++interpolateTargetTick;
        interpolateDelta += (int)(curInterpolateTargetTick - interpolateTargetTick);

        int absDelta = math.abs(interpolateDelta);
        if (absDelta > 10)
        {
            // Drifted too far away, do a force sync
            interpolateTargetTick = curInterpolateTargetTick;
            interpolateDelta = 0;
        }
        else if (absDelta > 3)
        {
            // Starting to drift a bit, adjust to keep up
            interpolateTargetTick += (uint)(interpolateDelta / absDelta);
            interpolateDelta = 0;
        }

    }

    uint AverageWithoutExtremes(NativeArray<uint> history, int medianDiscard)
    {
        var expectedList = new NativeArray<uint>(history.Length, Allocator.Temp);
        for (int i = 0; i < history.Length; ++i)
            expectedList[i]  = history[i];
        expectedList.Sort();
        uint sum = 0;
        // Skip top and bottom two, average the rest
        for (int i = medianDiscard; i < history.Length-medianDiscard; ++i)
            sum += expectedList[i];
        return sum / (uint)(history.Length - 2*medianDiscard);        
    }
}

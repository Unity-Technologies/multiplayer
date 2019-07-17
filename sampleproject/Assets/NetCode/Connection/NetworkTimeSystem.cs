using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[UpdateInGroup(typeof(ClientSimulationSystemGroup))]
[UpdateBefore(typeof(GhostReceiveSystemGroup))]
[UpdateAfter(typeof(NetworkStreamReceiveSystem))]
public class NetworkTimeSystem : ComponentSystem
{
    public static uint TimestampMS => (uint)(System.Diagnostics.Stopwatch.GetTimestamp() / System.TimeSpan.TicksPerMillisecond);
    public uint interpolateTargetTick;
    public uint predictTargetTick;

    private EntityQuery connectionGroup;
    private uint latestSnapshot;
    private uint latestSnapshotEstimate;
    private int latestSnapshotAge;

    private float subInterpolateTargetTick;
    private float subPredictTargetTick;

    private const int KSimTickRate = 60;
    private const int KNetTickRate = 60;
    private const int KInterpolationTimeNetTicks = 2;
    private const int KInterpolationTimeMS = 0;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
    private GhostStatsCollectionSystem m_StatsCollection;
#endif
    protected override void OnCreateManager()
    {
        connectionGroup = GetEntityQuery(ComponentType.ReadOnly<NetworkSnapshotAckComponent>());
        latestSnapshotEstimate = 0;
        latestSnapshot = 0;
        latestSnapshotAge = 0;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        m_StatsCollection = World.GetOrCreateSystem<GhostStatsCollectionSystem>();
#endif
    }

    protected override void OnUpdate()
    {
        if (connectionGroup.IsEmptyIgnoreFilter)
            return;
        var connections = connectionGroup.ToComponentDataArray<NetworkSnapshotAckComponent>(Allocator.TempJob);
        var ack = connections[0];
        connections.Dispose();

        // What we expect to have this frame based on what was the most recent received previous frames
        if (latestSnapshotEstimate == 0)
        {
            latestSnapshot = ack.LastReceivedSnapshotByLocal;
            latestSnapshotEstimate = ack.LastReceivedSnapshotByLocal;
            latestSnapshotAge = 0;
        }
        else
        {
            ++latestSnapshotEstimate;
            if (latestSnapshot != ack.LastReceivedSnapshotByLocal)
            {
                latestSnapshot = ack.LastReceivedSnapshotByLocal;
                int snapshotAge = (int) (latestSnapshotEstimate - ack.LastReceivedSnapshotByLocal);
                latestSnapshotAge = (latestSnapshotAge * 7 + (snapshotAge << 8)) / 8;

                int delta = latestSnapshotAge >> 8;
                if (delta < 0)
                    ++delta;
                if (delta != 0)
                {
                    latestSnapshotEstimate -= (uint) delta;
                    latestSnapshotAge -= delta << 8;
                }
            }
        }

        // Interpolation time is network tick rate times 2, round up to even number of sim ticks
        uint interpolationTimeMS = KInterpolationTimeMS;
        if (interpolationTimeMS == 0)
            interpolationTimeMS = (1000*KInterpolationTimeNetTicks + KNetTickRate - 1) / KNetTickRate;
        uint interpolationFrames = (interpolationTimeMS * KSimTickRate + 999) / 1000;

        uint curInterpol = latestSnapshotEstimate - interpolationFrames;
        int interpolDelta = (int)(curInterpol - interpolateTargetTick - 1);
        if (math.abs(interpolDelta) > 10)
        {
            interpolateTargetTick = curInterpol;
            subInterpolateTargetTick = 0;
        }
        else
        {
            subInterpolateTargetTick += 1.0f + math.clamp(0.1f*interpolDelta, -.2f, .2f);
            uint idiff = (uint) subInterpolateTargetTick;
            subInterpolateTargetTick -= idiff;
            interpolateTargetTick += idiff;
        }

        uint curPredict = latestSnapshotEstimate + 1 + ((uint)ack.EstimatedRTT * KSimTickRate + 999) / 1000;
        int predictDelta = (int)(curPredict - predictTargetTick - 1);
        if (math.abs(predictDelta) > 10)
        {
            predictTargetTick = curPredict;
            subPredictTargetTick = 0;
        }
        else
        {
            subPredictTargetTick += 1.0f + math.clamp(0.1f*predictDelta, -.2f, .2f);
            uint pdiff = (uint) subPredictTargetTick;
            subPredictTargetTick -= pdiff;
            predictTargetTick += pdiff;
        }
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        m_StatsCollection.SetTargetTime(interpolateTargetTick, predictTargetTick);
#endif
    }
}

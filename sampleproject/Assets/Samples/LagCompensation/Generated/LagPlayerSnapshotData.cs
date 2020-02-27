using Unity.Networking.Transport;
using Unity.NetCode;
using Unity.Mathematics;

public struct LagPlayerSnapshotData : ISnapshotData<LagPlayerSnapshotData>
{
    public uint tick;
    private int LagPlayerplayerId;
    uint changeMask0;

    public uint Tick => tick;
    public int GetLagPlayerplayerId(GhostDeserializerState deserializerState)
    {
        return (int)LagPlayerplayerId;
    }
    public int GetLagPlayerplayerId()
    {
        return (int)LagPlayerplayerId;
    }
    public void SetLagPlayerplayerId(int val, GhostSerializerState serializerState)
    {
        LagPlayerplayerId = (int)val;
    }
    public void SetLagPlayerplayerId(int val)
    {
        LagPlayerplayerId = (int)val;
    }

    public void PredictDelta(uint tick, ref LagPlayerSnapshotData baseline1, ref LagPlayerSnapshotData baseline2)
    {
        var predictor = new GhostDeltaPredictor(tick, this.tick, baseline1.tick, baseline2.tick);
        LagPlayerplayerId = predictor.PredictInt(LagPlayerplayerId, baseline1.LagPlayerplayerId, baseline2.LagPlayerplayerId);
    }

    public void Serialize(int networkId, ref LagPlayerSnapshotData baseline, ref DataStreamWriter writer, NetworkCompressionModel compressionModel)
    {
        changeMask0 = (LagPlayerplayerId != baseline.LagPlayerplayerId) ? 1u : 0;
        writer.WritePackedUIntDelta(changeMask0, baseline.changeMask0, compressionModel);
        if ((changeMask0 & (1 << 0)) != 0)
            writer.WritePackedIntDelta(LagPlayerplayerId, baseline.LagPlayerplayerId, compressionModel);
    }

    public void Deserialize(uint tick, ref LagPlayerSnapshotData baseline, ref DataStreamReader reader,
        NetworkCompressionModel compressionModel)
    {
        this.tick = tick;
        changeMask0 = reader.ReadPackedUIntDelta(baseline.changeMask0, compressionModel);
        if ((changeMask0 & (1 << 0)) != 0)
            LagPlayerplayerId = reader.ReadPackedIntDelta(baseline.LagPlayerplayerId, compressionModel);
        else
            LagPlayerplayerId = baseline.LagPlayerplayerId;
    }
    public void Interpolate(ref LagPlayerSnapshotData target, float factor)
    {
    }
}

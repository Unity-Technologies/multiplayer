using Unity.Networking.Transport;
using Unity.NetCode;
using Unity.Mathematics;

public struct BulletSnapshotData : ISnapshotData<BulletSnapshotData>
{
    public uint tick;
    private int PlayerIdComponentDataPlayerId;
    private int RotationValue;
    private int TranslationValueX;
    private int TranslationValueY;
    uint changeMask0;

    public uint Tick => tick;
    public int GetPlayerIdComponentDataPlayerId(GhostDeserializerState deserializerState)
    {
        return (int)PlayerIdComponentDataPlayerId;
    }
    public int GetPlayerIdComponentDataPlayerId()
    {
        return (int)PlayerIdComponentDataPlayerId;
    }
    public void SetPlayerIdComponentDataPlayerId(int val, GhostSerializerState serializerState)
    {
        PlayerIdComponentDataPlayerId = (int)val;
    }
    public void SetPlayerIdComponentDataPlayerId(int val)
    {
        PlayerIdComponentDataPlayerId = (int)val;
    }
    public quaternion GetRotationValue(GhostDeserializerState deserializerState)
    {
        return GetRotationValue();
    }
    public quaternion GetRotationValue()
    {
        var qw = RotationValue * 0.001f;
        return new quaternion(0, 0, math.abs(qw) > 1-1e-9?0:math.sqrt(1-qw*qw), qw);
    }
    public void SetRotationValue(quaternion q, GhostSerializerState serializerState)
    {
        SetRotationValue(q);
    }
    public void SetRotationValue(quaternion q)
    {
        RotationValue = (int) ((q.value.z >= 0 ? q.value.w : -q.value.w) * 1000);
    }
    public float3 GetTranslationValue(GhostDeserializerState deserializerState)
    {
        return GetTranslationValue();
    }
    public float3 GetTranslationValue()
    {
        return new float3(TranslationValueX * 0.01f, TranslationValueY * 0.01f, 0);
    }
    public void SetTranslationValue(float3 val, GhostSerializerState serializerState)
    {
        SetTranslationValue(val);
    }
    public void SetTranslationValue(float3 val)
    {
        TranslationValueX = (int)(val.x * 100);
        TranslationValueY = (int)(val.y * 100);
    }

    public void PredictDelta(uint tick, ref BulletSnapshotData baseline1, ref BulletSnapshotData baseline2)
    {
        var predictor = new GhostDeltaPredictor(tick, this.tick, baseline1.tick, baseline2.tick);
        PlayerIdComponentDataPlayerId = predictor.PredictInt(PlayerIdComponentDataPlayerId, baseline1.PlayerIdComponentDataPlayerId, baseline2.PlayerIdComponentDataPlayerId);
        RotationValue = predictor.PredictInt(RotationValue, baseline1.RotationValue, baseline2.RotationValue);
        TranslationValueX = predictor.PredictInt(TranslationValueX, baseline1.TranslationValueX, baseline2.TranslationValueX);
        TranslationValueY = predictor.PredictInt(TranslationValueY, baseline1.TranslationValueY, baseline2.TranslationValueY);
    }

    public void Serialize(int networkId, ref BulletSnapshotData baseline, DataStreamWriter writer, NetworkCompressionModel compressionModel)
    {
        changeMask0 = (PlayerIdComponentDataPlayerId != baseline.PlayerIdComponentDataPlayerId) ? 1u : 0;
        changeMask0 |= (RotationValue != baseline.RotationValue) ? (1u<<1) : 0;
        changeMask0 |= (TranslationValueX != baseline.TranslationValueX ||
                                           TranslationValueY != baseline.TranslationValueY) ? (1u<<2) : 0;
        writer.WritePackedUIntDelta(changeMask0, baseline.changeMask0, compressionModel);
        if ((changeMask0 & (1 << 0)) != 0)
            writer.WritePackedIntDelta(PlayerIdComponentDataPlayerId, baseline.PlayerIdComponentDataPlayerId, compressionModel);
        if ((changeMask0 & (1 << 1)) != 0)
            writer.WritePackedIntDelta(RotationValue, baseline.RotationValue, compressionModel);
        if ((changeMask0 & (1 << 2)) != 0)
        {
            writer.WritePackedIntDelta(TranslationValueX, baseline.TranslationValueX, compressionModel);
            writer.WritePackedIntDelta(TranslationValueY, baseline.TranslationValueY, compressionModel);
        }
    }

    public void Deserialize(uint tick, ref BulletSnapshotData baseline, DataStreamReader reader, ref DataStreamReader.Context ctx,
        NetworkCompressionModel compressionModel)
    {
        this.tick = tick;
        changeMask0 = reader.ReadPackedUIntDelta(ref ctx, baseline.changeMask0, compressionModel);
        if ((changeMask0 & (1 << 0)) != 0)
            PlayerIdComponentDataPlayerId = reader.ReadPackedIntDelta(ref ctx, baseline.PlayerIdComponentDataPlayerId, compressionModel);
        else
            PlayerIdComponentDataPlayerId = baseline.PlayerIdComponentDataPlayerId;
        if ((changeMask0 & (1 << 1)) != 0)
            RotationValue = reader.ReadPackedIntDelta(ref ctx, baseline.RotationValue, compressionModel);
        else
            RotationValue = baseline.RotationValue;
        if ((changeMask0 & (1 << 2)) != 0)
        {
            TranslationValueX = reader.ReadPackedIntDelta(ref ctx, baseline.TranslationValueX, compressionModel);
            TranslationValueY = reader.ReadPackedIntDelta(ref ctx, baseline.TranslationValueY, compressionModel);
        }
        else
        {
            TranslationValueX = baseline.TranslationValueX;
            TranslationValueY = baseline.TranslationValueY;
        }
    }
    public void Interpolate(ref BulletSnapshotData target, float factor)
    {
        SetRotationValue(math.slerp(GetRotationValue(), target.GetRotationValue(), factor));
        SetTranslationValue(math.lerp(GetTranslationValue(), target.GetTranslationValue(), factor));
    }
}

using Unity.Mathematics;
using Unity.Networking.Transport;

public struct BulletSnapshotData : ISnapshotData<BulletSnapshotData>
{
    public uint tick;
    int PlayerIdComponentDataPlayerId;
    int RotationValue;
    int TranslationValueX;
    int TranslationValueY;


    public uint Tick => tick;
    public int GetPlayerIdComponentDataPlayerId()
    {
        return PlayerIdComponentDataPlayerId;
    }
    public void SetPlayerIdComponentDataPlayerId(int val)
    {
        PlayerIdComponentDataPlayerId = val;
    }
    public quaternion GetRotationValue()
    {
        var qw = RotationValue * 0.001f;
        return new quaternion(0, 0, math.abs(qw) > 1-1e-9?0:math.sqrt(1-qw*qw), qw);
    }
    public void SetRotationValue(quaternion q)
    {
        RotationValue = (int) ((q.value.z >= 0 ? q.value.w : -q.value.w) * 1000);
    }
    public float3 GetTranslationValue()
    {
        return new float3(TranslationValueX, TranslationValueY, 0) * 0.1f;
    }
    public void SetTranslationValue(float3 val)
    {
        TranslationValueX = (int)(val.x * 10);
        TranslationValueY = (int)(val.y * 10);
    }


    public void PredictDelta(uint tick, ref BulletSnapshotData baseline1, ref BulletSnapshotData baseline2)
    {
        var predictor = new GhostDeltaPredictor(tick, this.tick, baseline1.tick, baseline2.tick);
        PlayerIdComponentDataPlayerId = predictor.PredictInt(PlayerIdComponentDataPlayerId, baseline1.PlayerIdComponentDataPlayerId, baseline2.PlayerIdComponentDataPlayerId);
        RotationValue = predictor.PredictInt(RotationValue, baseline1.RotationValue, baseline2.RotationValue);
        TranslationValueX = predictor.PredictInt(TranslationValueX, baseline1.TranslationValueX, baseline2.TranslationValueX);
        TranslationValueY = predictor.PredictInt(TranslationValueY, baseline1.TranslationValueY, baseline2.TranslationValueY);

    }

    public void Serialize(ref BulletSnapshotData baseline, DataStreamWriter writer, NetworkCompressionModel compressionModel)
    {
        writer.WritePackedIntDelta(PlayerIdComponentDataPlayerId, baseline.PlayerIdComponentDataPlayerId, compressionModel);
        writer.WritePackedIntDelta(RotationValue, baseline.RotationValue, compressionModel);
        writer.WritePackedIntDelta(TranslationValueX, baseline.TranslationValueX, compressionModel);
        writer.WritePackedIntDelta(TranslationValueY, baseline.TranslationValueY, compressionModel);

    }

    public void Deserialize(uint tick, ref BulletSnapshotData baseline, DataStreamReader reader, ref DataStreamReader.Context ctx,
        NetworkCompressionModel compressionModel)
    {
        this.tick = tick;
        PlayerIdComponentDataPlayerId = reader.ReadPackedIntDelta(ref ctx, baseline.PlayerIdComponentDataPlayerId, compressionModel);
        RotationValue = reader.ReadPackedIntDelta(ref ctx, baseline.RotationValue, compressionModel);
        TranslationValueX = reader.ReadPackedIntDelta(ref ctx, baseline.TranslationValueX, compressionModel);
        TranslationValueY = reader.ReadPackedIntDelta(ref ctx, baseline.TranslationValueY, compressionModel);

    }
    public void Interpolate(ref BulletSnapshotData target, float factor)
    {
        SetRotationValue(math.slerp(GetRotationValue(), target.GetRotationValue(), factor));
        SetTranslationValue(math.lerp(GetTranslationValue(), target.GetTranslationValue(), factor));

    }
}
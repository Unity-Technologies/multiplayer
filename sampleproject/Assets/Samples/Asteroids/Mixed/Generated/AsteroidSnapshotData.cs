using Unity.Mathematics;
using Unity.Networking.Transport;

public struct AsteroidSnapshotData : ISnapshotData<AsteroidSnapshotData>
{
    public uint tick;
    int TranslationValueX;
    int TranslationValueY;
    int RotationValue;


    public uint Tick => tick;
    public float3 GetTranslationValue()
    {
        return new float3(TranslationValueX, TranslationValueY, 0) * 0.1f;
    }
    public void SetTranslationValue(float3 val)
    {
        TranslationValueX = (int)(val.x * 10);
        TranslationValueY = (int)(val.y * 10);
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


    public void PredictDelta(uint tick, ref AsteroidSnapshotData baseline1, ref AsteroidSnapshotData baseline2)
    {
        var predictor = new GhostDeltaPredictor(tick, this.tick, baseline1.tick, baseline2.tick);
        TranslationValueX = predictor.PredictInt(TranslationValueX, baseline1.TranslationValueX, baseline2.TranslationValueX);
        TranslationValueY = predictor.PredictInt(TranslationValueY, baseline1.TranslationValueY, baseline2.TranslationValueY);
        RotationValue = predictor.PredictInt(RotationValue, baseline1.RotationValue, baseline2.RotationValue);

    }

    public void Serialize(ref AsteroidSnapshotData baseline, DataStreamWriter writer, NetworkCompressionModel compressionModel)
    {
        writer.WritePackedIntDelta(TranslationValueX, baseline.TranslationValueX, compressionModel);
        writer.WritePackedIntDelta(TranslationValueY, baseline.TranslationValueY, compressionModel);
        writer.WritePackedIntDelta(RotationValue, baseline.RotationValue, compressionModel);

    }

    public void Deserialize(uint tick, ref AsteroidSnapshotData baseline, DataStreamReader reader, ref DataStreamReader.Context ctx,
        NetworkCompressionModel compressionModel)
    {
        this.tick = tick;
        TranslationValueX = reader.ReadPackedIntDelta(ref ctx, baseline.TranslationValueX, compressionModel);
        TranslationValueY = reader.ReadPackedIntDelta(ref ctx, baseline.TranslationValueY, compressionModel);
        RotationValue = reader.ReadPackedIntDelta(ref ctx, baseline.RotationValue, compressionModel);

    }
    public void Interpolate(ref AsteroidSnapshotData target, float factor)
    {
        SetTranslationValue(math.lerp(GetTranslationValue(), target.GetTranslationValue(), factor));
        SetRotationValue(math.slerp(GetRotationValue(), target.GetRotationValue(), factor));

    }
}
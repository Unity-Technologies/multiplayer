using Unity.Mathematics;
using Unity.Networking.Transport;

public struct ShipSnapshotData : ISnapshotData<ShipSnapshotData>
{
    public uint tick;
    int PlayerIdComponentDataPlayerId;
    int RotationValue;
    int ShipStateComponentDataState;
    int TranslationValueX;
    int TranslationValueY;
    int VelocityValueX;
    int VelocityValueY;


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
    public int GetShipStateComponentDataState()
    {
        return ShipStateComponentDataState;
    }
    public void SetShipStateComponentDataState(int val)
    {
        ShipStateComponentDataState = val;
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
    public float2 GetVelocityValue()
    {
        return new float2(VelocityValueX, VelocityValueY) * 0.1f;
    }
    public void SetVelocityValue(float2 val)
    {
        VelocityValueX = (int)(val.x * 10);
        VelocityValueY = (int)(val.y * 10);
    }


    public void PredictDelta(uint tick, ref ShipSnapshotData baseline1, ref ShipSnapshotData baseline2)
    {
        var predictor = new GhostDeltaPredictor(tick, this.tick, baseline1.tick, baseline2.tick);
        PlayerIdComponentDataPlayerId = predictor.PredictInt(PlayerIdComponentDataPlayerId, baseline1.PlayerIdComponentDataPlayerId, baseline2.PlayerIdComponentDataPlayerId);
        RotationValue = predictor.PredictInt(RotationValue, baseline1.RotationValue, baseline2.RotationValue);
        ShipStateComponentDataState = predictor.PredictInt(ShipStateComponentDataState, baseline1.ShipStateComponentDataState, baseline2.ShipStateComponentDataState);
        TranslationValueX = predictor.PredictInt(TranslationValueX, baseline1.TranslationValueX, baseline2.TranslationValueX);
        TranslationValueY = predictor.PredictInt(TranslationValueY, baseline1.TranslationValueY, baseline2.TranslationValueY);
        VelocityValueX = predictor.PredictInt(VelocityValueX, baseline1.VelocityValueX, baseline2.VelocityValueX);
        VelocityValueY = predictor.PredictInt(VelocityValueY, baseline1.VelocityValueY, baseline2.VelocityValueY);

    }

    public void Serialize(ref ShipSnapshotData baseline, DataStreamWriter writer, NetworkCompressionModel compressionModel)
    {
        writer.WritePackedIntDelta(PlayerIdComponentDataPlayerId, baseline.PlayerIdComponentDataPlayerId, compressionModel);
        writer.WritePackedIntDelta(RotationValue, baseline.RotationValue, compressionModel);
        writer.WritePackedIntDelta(ShipStateComponentDataState, baseline.ShipStateComponentDataState, compressionModel);
        writer.WritePackedIntDelta(TranslationValueX, baseline.TranslationValueX, compressionModel);
        writer.WritePackedIntDelta(TranslationValueY, baseline.TranslationValueY, compressionModel);
        writer.WritePackedIntDelta(VelocityValueX, baseline.VelocityValueX, compressionModel);
        writer.WritePackedIntDelta(VelocityValueY, baseline.VelocityValueY, compressionModel);

    }

    public void Deserialize(uint tick, ref ShipSnapshotData baseline, DataStreamReader reader, ref DataStreamReader.Context ctx,
        NetworkCompressionModel compressionModel)
    {
        this.tick = tick;
        PlayerIdComponentDataPlayerId = reader.ReadPackedIntDelta(ref ctx, baseline.PlayerIdComponentDataPlayerId, compressionModel);
        RotationValue = reader.ReadPackedIntDelta(ref ctx, baseline.RotationValue, compressionModel);
        ShipStateComponentDataState = reader.ReadPackedIntDelta(ref ctx, baseline.ShipStateComponentDataState, compressionModel);
        TranslationValueX = reader.ReadPackedIntDelta(ref ctx, baseline.TranslationValueX, compressionModel);
        TranslationValueY = reader.ReadPackedIntDelta(ref ctx, baseline.TranslationValueY, compressionModel);
        VelocityValueX = reader.ReadPackedIntDelta(ref ctx, baseline.VelocityValueX, compressionModel);
        VelocityValueY = reader.ReadPackedIntDelta(ref ctx, baseline.VelocityValueY, compressionModel);

    }
    public void Interpolate(ref ShipSnapshotData target, float factor)
    {
        SetRotationValue(math.slerp(GetRotationValue(), target.GetRotationValue(), factor));
        SetTranslationValue(math.lerp(GetTranslationValue(), target.GetTranslationValue(), factor));
        SetVelocityValue(math.lerp(GetVelocityValue(), target.GetVelocityValue(), factor));

    }
}
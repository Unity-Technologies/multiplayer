using Unity.Mathematics;

public struct GhostDeltaPredictor
{
    private int predictFrac;
    private int applyFrac;

    public GhostDeltaPredictor(uint tick, uint baseline0, uint baseline1, uint baseline2)
    {
        predictFrac = 16 * (int)(baseline0 - baseline1) / (int)(baseline1 - baseline2);
        applyFrac = 16 * (int)(tick - baseline0) / (int)(baseline0 - baseline1);
    }

    public int PredictInt(int baseline0, int baseline1, int baseline2)
    {
        int delta = baseline1 - baseline2;
        int predictBaseline = baseline1 + delta * predictFrac / 16;
        delta = baseline0 - baseline1;
        if (math.abs(baseline0-predictBaseline) >= math.abs(delta))
            return baseline0;
        return baseline0 + delta * applyFrac / 16;
    }
}

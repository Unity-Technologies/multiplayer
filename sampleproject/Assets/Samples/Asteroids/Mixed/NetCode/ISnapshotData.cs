using Unity.Entities;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Utilities;

public interface ISnapshotData<T> : IBufferElementData where T: struct, ISnapshotData<T>
{
    uint Tick { get; }
    void PredictDelta(uint tick, ref T baseline1, ref T baseline2);
    void Serialize(ref T baseline, DataStreamWriter writer, NetworkCompressionModel compressionModel);
    void Deserialize(uint tick, ref T baseline, DataStreamReader reader, ref DataStreamReader.Context ctx, NetworkCompressionModel compressionModel);

    void Interpolate(ref T target, float factor);
}

public static class SnapshotDataUtility
{
    public static bool GetDataAtTick<T>(this DynamicBuffer<T> snapshotArray, uint targetTick, out T snapshotData) where T : struct, ISnapshotData<T>
    {
        int beforeIdx = 0;
        uint beforeTick = 0;
        int afterIdx = 0;
        uint afterTick = 0;
        for (int i = 0; i < snapshotArray.Length; ++i)
        {
            uint tick = snapshotArray[i].Tick;
            if (!SequenceHelpers.IsNewer(tick, targetTick) && (beforeTick == 0 || SequenceHelpers.IsNewer(tick, beforeTick)))
            {
                beforeIdx = i;
                beforeTick = tick;
            }
            if (SequenceHelpers.IsNewer(tick, targetTick) && (afterTick == 0 || SequenceHelpers.IsNewer(afterTick, tick)))
            {
                afterIdx = i;
                afterTick = tick;
            }
        }

        if (beforeTick == 0)
        {
            snapshotData = default(T);
            return false;
        }

        snapshotData = snapshotArray[beforeIdx];
        if (afterTick == 0)
            return true;
        var after = snapshotArray[afterIdx];
        float afterWeight = (float)(targetTick - beforeTick) / (float)(afterTick - beforeTick);
        snapshotData.Interpolate(ref after, afterWeight);
        return true;
    }
}

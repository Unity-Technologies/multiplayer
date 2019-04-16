using Unity.Entities;

public interface IGhostSerializer<T> where T: struct, ISnapshotData<T>
{
    int SnapshotSize { get; }
    int CalculateImportance(ArchetypeChunk chunk);
    bool WantsPredictionDelta { get; }
    void BeginSerialize(ComponentSystemBase system);
    bool CanSerialize(EntityArchetype arch);
    void CopyToSnapshot(ArchetypeChunk chunk, int ent, uint tick, ref T snapshot);
}


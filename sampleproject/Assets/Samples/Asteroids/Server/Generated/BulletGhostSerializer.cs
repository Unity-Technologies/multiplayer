using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Transforms;

public struct BulletGhostSerializer : IGhostSerializer<BulletSnapshotData>
{
    // FIXME: These disable safety since all serializers have an instance of the same type - causing aliasing. Should be fixed in a cleaner way
    private ComponentType componentTypeBulletTagComponentData;
    private ComponentType componentTypeTranslation;
    [NativeDisableContainerSafetyRestriction] private ArchetypeChunkComponentType<Translation> ghostTranslationType;
    private ComponentType componentTypeRotation;
    [NativeDisableContainerSafetyRestriction] private ArchetypeChunkComponentType<Rotation> ghostRotationType;
    private ComponentType componentTypePlayerIdComponentData;
    [NativeDisableContainerSafetyRestriction] private ArchetypeChunkComponentType<PlayerIdComponentData> ghostPlayerIdComponentDataType;


    public int CalculateImportance(ArchetypeChunk chunk)
    {
        return 200;
    }

    public bool WantsPredictionDelta => true;

    public int SnapshotSize => UnsafeUtility.SizeOf<BulletSnapshotData>();
    public void BeginSerialize(ComponentSystemBase system)
    {
        componentTypeBulletTagComponentData = ComponentType.ReadWrite<BulletTagComponentData>();
        componentTypeTranslation = ComponentType.ReadWrite<Translation>();
        ghostTranslationType = system.GetArchetypeChunkComponentType<Translation>();
        componentTypeRotation = ComponentType.ReadWrite<Rotation>();
        ghostRotationType = system.GetArchetypeChunkComponentType<Rotation>();
        componentTypePlayerIdComponentData = ComponentType.ReadWrite<PlayerIdComponentData>();
        ghostPlayerIdComponentDataType = system.GetArchetypeChunkComponentType<PlayerIdComponentData>();

    }

    public bool CanSerialize(EntityArchetype arch)
    {
        var components = arch.GetComponentTypes();
        int matches = 0;
        for (int i = 0; i < components.Length; ++i)
        {
            if (components[i] == componentTypeBulletTagComponentData)
                ++matches;
            if (components[i] == componentTypeTranslation)
                ++matches;
            if (components[i] == componentTypeRotation)
                ++matches;
            if (components[i] == componentTypePlayerIdComponentData)
                ++matches;

        }
        return (matches == 4);
    }

    public void CopyToSnapshot(ArchetypeChunk chunk, int ent, uint tick, ref BulletSnapshotData snapshot)
    {
        snapshot.tick = tick;
        var chunkDataTranslation = chunk.GetNativeArray(ghostTranslationType);
        snapshot.SetTranslationValue(chunkDataTranslation[ent].Value);
        var chunkDataRotation = chunk.GetNativeArray(ghostRotationType);
        snapshot.SetRotationValue(chunkDataRotation[ent].Value);
        var chunkDataPlayerIdComponentData = chunk.GetNativeArray(ghostPlayerIdComponentDataType);
        snapshot.SetPlayerIdComponentDataPlayerId(chunkDataPlayerIdComponentData[ent].PlayerId);

    }
}

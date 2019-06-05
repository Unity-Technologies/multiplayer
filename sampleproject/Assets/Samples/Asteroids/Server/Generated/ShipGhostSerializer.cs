using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Transforms;

public struct ShipGhostSerializer : IGhostSerializer<ShipSnapshotData>
{
    // FIXME: These disable safety since all serializers have an instance of the same type - causing aliasing. Should be fixed in a cleaner way
    private ComponentType componentTypeTranslation;
    [NativeDisableContainerSafetyRestriction] private ArchetypeChunkComponentType<Translation> ghostTranslationType;
    private ComponentType componentTypeRotation;
    [NativeDisableContainerSafetyRestriction] private ArchetypeChunkComponentType<Rotation> ghostRotationType;
    private ComponentType componentTypeVelocity;
    [NativeDisableContainerSafetyRestriction] private ArchetypeChunkComponentType<Velocity> ghostVelocityType;
    private ComponentType componentTypeShipStateComponentData;
    [NativeDisableContainerSafetyRestriction] private ArchetypeChunkComponentType<ShipStateComponentData> ghostShipStateComponentDataType;
    private ComponentType componentTypePlayerIdComponentData;
    [NativeDisableContainerSafetyRestriction] private ArchetypeChunkComponentType<PlayerIdComponentData> ghostPlayerIdComponentDataType;
    private ComponentType componentTypeShipTagComponentData;


    public int CalculateImportance(ArchetypeChunk chunk)
    {
        return 200;
    }

    public bool WantsPredictionDelta => true;

    public int SnapshotSize => UnsafeUtility.SizeOf<ShipSnapshotData>();
    public void BeginSerialize(ComponentSystemBase system)
    {
        componentTypeTranslation = ComponentType.ReadWrite<Translation>();
        ghostTranslationType = system.GetArchetypeChunkComponentType<Translation>();
        componentTypeRotation = ComponentType.ReadWrite<Rotation>();
        ghostRotationType = system.GetArchetypeChunkComponentType<Rotation>();
        componentTypeVelocity = ComponentType.ReadWrite<Velocity>();
        ghostVelocityType = system.GetArchetypeChunkComponentType<Velocity>();
        componentTypeShipStateComponentData = ComponentType.ReadWrite<ShipStateComponentData>();
        ghostShipStateComponentDataType = system.GetArchetypeChunkComponentType<ShipStateComponentData>();
        componentTypePlayerIdComponentData = ComponentType.ReadWrite<PlayerIdComponentData>();
        ghostPlayerIdComponentDataType = system.GetArchetypeChunkComponentType<PlayerIdComponentData>();
        componentTypeShipTagComponentData = ComponentType.ReadWrite<ShipTagComponentData>();

    }

    public bool CanSerialize(EntityArchetype arch)
    {
        var components = arch.GetComponentTypes();
        int matches = 0;
        for (int i = 0; i < components.Length; ++i)
        {
            if (components[i] == componentTypeTranslation)
                ++matches;
            if (components[i] == componentTypeRotation)
                ++matches;
            if (components[i] == componentTypeVelocity)
                ++matches;
            if (components[i] == componentTypeShipStateComponentData)
                ++matches;
            if (components[i] == componentTypePlayerIdComponentData)
                ++matches;
            if (components[i] == componentTypeShipTagComponentData)
                ++matches;

        }
        return (matches == 6);
    }

    public void CopyToSnapshot(ArchetypeChunk chunk, int ent, uint tick, ref ShipSnapshotData snapshot)
    {
        snapshot.tick = tick;
        var chunkDataTranslation = chunk.GetNativeArray(ghostTranslationType);
        snapshot.SetTranslationValue(chunkDataTranslation[ent].Value);
        var chunkDataRotation = chunk.GetNativeArray(ghostRotationType);
        snapshot.SetRotationValue(chunkDataRotation[ent].Value);
        var chunkDataVelocity = chunk.GetNativeArray(ghostVelocityType);
        snapshot.SetVelocityValue(chunkDataVelocity[ent].Value);
        var chunkDataShipStateComponentData = chunk.GetNativeArray(ghostShipStateComponentDataType);
        snapshot.SetShipStateComponentDataState(chunkDataShipStateComponentData[ent].State);
        var chunkDataPlayerIdComponentData = chunk.GetNativeArray(ghostPlayerIdComponentDataType);
        snapshot.SetPlayerIdComponentDataPlayerId(chunkDataPlayerIdComponentData[ent].PlayerId);

    }
}

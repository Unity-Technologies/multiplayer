using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Collections;
using Unity.NetCode;
using Unity.Transforms;

public struct ShipGhostSerializer : IGhostSerializer<ShipSnapshotData>
{
    private ComponentType componentTypeCollisionSphereComponent;
    private ComponentType componentTypePlayerIdComponentData;
    private ComponentType componentTypeShipCommandData;
    private ComponentType componentTypeShipStateComponentData;
    private ComponentType componentTypeShipTagComponentData;
    private ComponentType componentTypeRotation;
    private ComponentType componentTypeTranslation;
    private ComponentType componentTypeVelocity;
    // FIXME: These disable safety since all serializers have an instance of the same type - causing aliasing. Should be fixed in a cleaner way
    [NativeDisableContainerSafetyRestriction][ReadOnly] private ArchetypeChunkComponentType<PlayerIdComponentData> ghostPlayerIdComponentDataType;
    [NativeDisableContainerSafetyRestriction][ReadOnly] private ArchetypeChunkComponentType<ShipStateComponentData> ghostShipStateComponentDataType;
    [NativeDisableContainerSafetyRestriction][ReadOnly] private ArchetypeChunkComponentType<Rotation> ghostRotationType;
    [NativeDisableContainerSafetyRestriction][ReadOnly] private ArchetypeChunkComponentType<Translation> ghostTranslationType;
    [NativeDisableContainerSafetyRestriction][ReadOnly] private ArchetypeChunkComponentType<Velocity> ghostVelocityType;


    public int CalculateImportance(ArchetypeChunk chunk)
    {
        return 200;
    }

    public bool WantsPredictionDelta => true;

    public int SnapshotSize => UnsafeUtility.SizeOf<ShipSnapshotData>();
    public void BeginSerialize(ComponentSystemBase system)
    {
        componentTypeCollisionSphereComponent = ComponentType.ReadWrite<CollisionSphereComponent>();
        componentTypePlayerIdComponentData = ComponentType.ReadWrite<PlayerIdComponentData>();
        componentTypeShipCommandData = ComponentType.ReadWrite<ShipCommandData>();
        componentTypeShipStateComponentData = ComponentType.ReadWrite<ShipStateComponentData>();
        componentTypeShipTagComponentData = ComponentType.ReadWrite<ShipTagComponentData>();
        componentTypeRotation = ComponentType.ReadWrite<Rotation>();
        componentTypeTranslation = ComponentType.ReadWrite<Translation>();
        componentTypeVelocity = ComponentType.ReadWrite<Velocity>();
        ghostPlayerIdComponentDataType = system.GetArchetypeChunkComponentType<PlayerIdComponentData>(true);
        ghostShipStateComponentDataType = system.GetArchetypeChunkComponentType<ShipStateComponentData>(true);
        ghostRotationType = system.GetArchetypeChunkComponentType<Rotation>(true);
        ghostTranslationType = system.GetArchetypeChunkComponentType<Translation>(true);
        ghostVelocityType = system.GetArchetypeChunkComponentType<Velocity>(true);
    }

    public bool CanSerialize(EntityArchetype arch)
    {
        var components = arch.GetComponentTypes();
        int matches = 0;
        for (int i = 0; i < components.Length; ++i)
        {
            if (components[i] == componentTypeCollisionSphereComponent)
                ++matches;
            if (components[i] == componentTypePlayerIdComponentData)
                ++matches;
            if (components[i] == componentTypeShipCommandData)
                ++matches;
            if (components[i] == componentTypeShipStateComponentData)
                ++matches;
            if (components[i] == componentTypeShipTagComponentData)
                ++matches;
            if (components[i] == componentTypeRotation)
                ++matches;
            if (components[i] == componentTypeTranslation)
                ++matches;
            if (components[i] == componentTypeVelocity)
                ++matches;
        }
        return (matches == 8);
    }

    public void CopyToSnapshot(ArchetypeChunk chunk, int ent, uint tick, ref ShipSnapshotData snapshot, GhostSerializerState serializerState)
    {
        snapshot.tick = tick;
        var chunkDataPlayerIdComponentData = chunk.GetNativeArray(ghostPlayerIdComponentDataType);
        var chunkDataShipStateComponentData = chunk.GetNativeArray(ghostShipStateComponentDataType);
        var chunkDataRotation = chunk.GetNativeArray(ghostRotationType);
        var chunkDataTranslation = chunk.GetNativeArray(ghostTranslationType);
        var chunkDataVelocity = chunk.GetNativeArray(ghostVelocityType);
        snapshot.SetPlayerIdComponentDataPlayerId(chunkDataPlayerIdComponentData[ent].PlayerId, serializerState);
        snapshot.SetShipStateComponentDataState(chunkDataShipStateComponentData[ent].State, serializerState);
        snapshot.SetRotationValue(chunkDataRotation[ent].Value, serializerState);
        snapshot.SetTranslationValue(chunkDataTranslation[ent].Value, serializerState);
        snapshot.SetVelocityValue(chunkDataVelocity[ent].Value, serializerState);
    }
}

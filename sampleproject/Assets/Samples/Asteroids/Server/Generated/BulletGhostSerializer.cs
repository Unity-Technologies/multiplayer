using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Collections;
using Unity.NetCode;
using Unity.Transforms;

public struct BulletGhostSerializer : IGhostSerializer<BulletSnapshotData>
{
    private ComponentType componentTypeBulletAgeComponent;
    private ComponentType componentTypeBulletTagComponent;
    private ComponentType componentTypeCollisionSphereComponent;
    private ComponentType componentTypePlayerIdComponentData;
    private ComponentType componentTypeRotation;
    private ComponentType componentTypeTranslation;
    private ComponentType componentTypeVelocity;
    // FIXME: These disable safety since all serializers have an instance of the same type - causing aliasing. Should be fixed in a cleaner way
    [NativeDisableContainerSafetyRestriction][ReadOnly] private ArchetypeChunkComponentType<PlayerIdComponentData> ghostPlayerIdComponentDataType;
    [NativeDisableContainerSafetyRestriction][ReadOnly] private ArchetypeChunkComponentType<Rotation> ghostRotationType;
    [NativeDisableContainerSafetyRestriction][ReadOnly] private ArchetypeChunkComponentType<Translation> ghostTranslationType;


    public int CalculateImportance(ArchetypeChunk chunk)
    {
        return 200;
    }

    public int SnapshotSize => UnsafeUtility.SizeOf<BulletSnapshotData>();
    public void BeginSerialize(ComponentSystemBase system)
    {
        componentTypeBulletAgeComponent = ComponentType.ReadWrite<BulletAgeComponent>();
        componentTypeBulletTagComponent = ComponentType.ReadWrite<BulletTagComponent>();
        componentTypeCollisionSphereComponent = ComponentType.ReadWrite<CollisionSphereComponent>();
        componentTypePlayerIdComponentData = ComponentType.ReadWrite<PlayerIdComponentData>();
        componentTypeRotation = ComponentType.ReadWrite<Rotation>();
        componentTypeTranslation = ComponentType.ReadWrite<Translation>();
        componentTypeVelocity = ComponentType.ReadWrite<Velocity>();
        ghostPlayerIdComponentDataType = system.GetArchetypeChunkComponentType<PlayerIdComponentData>(true);
        ghostRotationType = system.GetArchetypeChunkComponentType<Rotation>(true);
        ghostTranslationType = system.GetArchetypeChunkComponentType<Translation>(true);
    }

    public void CopyToSnapshot(ArchetypeChunk chunk, int ent, uint tick, ref BulletSnapshotData snapshot, GhostSerializerState serializerState)
    {
        snapshot.tick = tick;
        var chunkDataPlayerIdComponentData = chunk.GetNativeArray(ghostPlayerIdComponentDataType);
        var chunkDataRotation = chunk.GetNativeArray(ghostRotationType);
        var chunkDataTranslation = chunk.GetNativeArray(ghostTranslationType);
        snapshot.SetPlayerIdComponentDataPlayerId(chunkDataPlayerIdComponentData[ent].PlayerId, serializerState);
        snapshot.SetRotationValue(chunkDataRotation[ent].Value, serializerState);
        snapshot.SetTranslationValue(chunkDataTranslation[ent].Value, serializerState);
    }
}

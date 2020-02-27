using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Collections;
using Unity.NetCode;

public struct LagPlayerGhostSerializer : IGhostSerializer<LagPlayerSnapshotData>
{
    private ComponentType componentTypeLagPlayer;
    private ComponentType componentTypeRayTraceCommand;
    private ComponentType componentTypeCommandDataInterpolationDelay;
    // FIXME: These disable safety since all serializers have an instance of the same type - causing aliasing. Should be fixed in a cleaner way
    [NativeDisableContainerSafetyRestriction][ReadOnly] private ArchetypeChunkComponentType<LagPlayer> ghostLagPlayerType;


    public int CalculateImportance(ArchetypeChunk chunk)
    {
        return 1;
    }

    public int SnapshotSize => UnsafeUtility.SizeOf<LagPlayerSnapshotData>();
    public void BeginSerialize(ComponentSystemBase system)
    {
        componentTypeLagPlayer = ComponentType.ReadWrite<LagPlayer>();
        componentTypeRayTraceCommand = ComponentType.ReadWrite<RayTraceCommand>();
        componentTypeCommandDataInterpolationDelay = ComponentType.ReadWrite<CommandDataInterpolationDelay>();
        ghostLagPlayerType = system.GetArchetypeChunkComponentType<LagPlayer>(true);
    }

    public void CopyToSnapshot(ArchetypeChunk chunk, int ent, uint tick, ref LagPlayerSnapshotData snapshot, GhostSerializerState serializerState)
    {
        snapshot.tick = tick;
        var chunkDataLagPlayer = chunk.GetNativeArray(ghostLagPlayerType);
        snapshot.SetLagPlayerplayerId(chunkDataLagPlayer[ent].playerId, serializerState);
    }
}

using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Networking.Transport;
using Unity.Transforms;

public struct AsteroidSnapshotData : ISnapshotData<AsteroidSnapshotData>
{
    public uint Tick => tick;
    public uint tick;
    private int posX;
    private int posY;
    private int rot;

    public float GetPosX()
    {
        return posX * 0.1f;
    }
    public void SetPosX(float x)
    {
        posX = (int)(x * 10.0f);
    }
    public float GetPosY()
    {
        return posY * 0.1f;
    }
    public void SetPosY(float y)
    {
        posY = (int)(y * 10.0f);
    }
    public quaternion GetRot()
    {
        var qw = rot*0.001f;
        return new quaternion(0, 0, math.abs(qw) > 1-1e-9?0:math.sqrt(1-qw*qw), qw);
    }
    public void SetRot(quaternion q)
    {
        rot = (int) ((q.value.z >= 0 ? q.value.w : -q.value.w) * 1000.0f);
    }
    public void PredictDelta(uint tick, ref AsteroidSnapshotData baseline1, ref AsteroidSnapshotData baseline2)
    {
        throw new NotImplementedException();
    }

    public void Serialize(ref AsteroidSnapshotData baseline, DataStreamWriter writer, NetworkCompressionModel compressionModel)
    {
        writer.WritePackedIntDelta(posX, baseline.posX, compressionModel);
        writer.WritePackedIntDelta(posY, baseline.posY, compressionModel);
        writer.WritePackedIntDelta(rot, baseline.rot, compressionModel);
    }
    public void Deserialize(uint tick, ref AsteroidSnapshotData baseline, DataStreamReader reader, ref DataStreamReader.Context ctx,
        NetworkCompressionModel compressionModel)
    {
        this.tick = tick;
        posX = reader.ReadPackedIntDelta(ref ctx, baseline.posX, compressionModel);
        posY = reader.ReadPackedIntDelta(ref ctx, baseline.posY, compressionModel);
        rot = reader.ReadPackedIntDelta(ref ctx, baseline.rot, compressionModel);
    }

    public void Interpolate(ref AsteroidSnapshotData target, float factor)
    {
        SetPosX(math.lerp(GetPosX(), target.GetPosX(), factor));
        SetPosY(math.lerp(GetPosY(), target.GetPosY(), factor));
        SetRot(math.slerp(GetRot(), target.GetRot(), factor));
    }
}
public struct AsteroidGhostSerializer : IGhostSerializer<AsteroidSnapshotData>
{
    // FIXME: These disable safety since all serializers have an instance of the same type - causing aliasing. Should be fixed in a cleaner way
    [NativeDisableContainerSafetyRestriction] private ArchetypeChunkComponentType<Translation> ghostPositionType;
    [NativeDisableContainerSafetyRestriction] private ArchetypeChunkComponentType<Rotation> ghostRotationType;

    public int CalculateImportance(ArchetypeChunk chunk)
    {
        return 1;
    }

    public bool WantsPredictionDelta => false;

    public int SnapshotSize => UnsafeUtility.SizeOf<AsteroidSnapshotData>();

    public void BeginSerialize(ComponentSystemBase system)
    {
        ghostPositionType = system.GetArchetypeChunkComponentType<Translation>();
        ghostRotationType = system.GetArchetypeChunkComponentType<Rotation>();

        asteroidTagType = ComponentType.ReadWrite<AsteroidTagComponentData>();
        positionType = ComponentType.ReadWrite<Translation>();
        rotationType = ComponentType.ReadWrite<Rotation>();
    }

    private ComponentType asteroidTagType;
    private ComponentType positionType;
    private ComponentType rotationType;

    public bool CanSerialize(EntityArchetype arch)
    {
        var components = arch.GetComponentTypes();
        int matches = 0;
        for (int i = 0; i < components.Length; ++i)
        {
            if (components[i] == asteroidTagType)
                ++matches;
            if (components[i] == positionType)
                ++matches;
            if (components[i] == rotationType)
                ++matches;
        }

        return (matches == 3);
    }

    public void CopyToSnapshot(ArchetypeChunk chunk, int ent, uint tick, ref AsteroidSnapshotData snapshot)
    {
        var pos = chunk.GetNativeArray(ghostPositionType);
        var rot = chunk.GetNativeArray(ghostRotationType);
        snapshot.tick = tick;
        snapshot.SetPosX(pos[ent].Value.x);
        snapshot.SetPosY(pos[ent].Value.y);
        snapshot.SetRot(rot[ent].Value);
    }
}

public class AsteroidGhostSpawnSystem : DefaultGhostSpawnSystem<AsteroidSnapshotData>
{
    protected override EntityArchetype GetGhostArchetype()
    {
        return EntityManager.CreateArchetype(
            ComponentType.ReadWrite<AsteroidSnapshotData>(),
            ComponentType.ReadWrite<ReplicatedEntity>(),
            ComponentType.ReadWrite<AsteroidTagComponentData>(),
            ComponentType.ReadWrite<CurrentSimulatedPosition>(),
            ComponentType.ReadWrite<CurrentSimulatedRotation>(),
            ComponentType.ReadWrite<Translation>(),
            ComponentType.ReadWrite<Rotation>()
        );
    }
}

[UpdateInGroup(typeof(GhostReceiveSystemGroup))]
public class AsteroidGhostUpdateSystem : JobComponentSystem
{
    [BurstCompile]
    [RequireComponentTag(typeof(AsteroidSnapshotData))]
    struct UpdateJob : IJobProcessComponentDataWithEntity<Translation, Rotation>
    {
        [NativeDisableParallelForRestriction] public BufferFromEntity<AsteroidSnapshotData> snapshotFromEntity;
        public uint targetTick;
        public void Execute(Entity entity, int index, ref Translation position, ref Rotation rotation)
        {
            var snapshot = snapshotFromEntity[entity];
            AsteroidSnapshotData snapshotData;
            snapshot.GetDataAtTick(targetTick, out snapshotData);
            position = new Translation {Value = new float3(snapshotData.GetPosX(), snapshotData.GetPosY(), 0)};
            rotation = new Rotation {Value = snapshotData.GetRot()};
        }
    }
    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        var updateJob = new UpdateJob
        {
            snapshotFromEntity = GetBufferFromEntity<AsteroidSnapshotData>(),
            targetTick = NetworkTimeSystem.interpolateTargetTick
        };
        return updateJob.Schedule(this, inputDeps);
    }
}

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

public struct RenderInterpolationParameters
{
    public float startTime;
    public float fixedDeltaTime;
}

[UpdateInGroup(typeof(ClientPresentationSystemGroup))]
public class RenderInterpolationSystem : JobComponentSystem
{
    // FIXME: should use singleton component
    public static RenderInterpolationParameters parameters;
    private EntityQuery posInterpolationGroup;
    private EntityQuery rotInterpolationGroup;
    private uint lastInterpolationVersion;
    protected override void OnCreateManager()
    {
        posInterpolationGroup = GetEntityQuery(
            ComponentType.ReadWrite<Translation>(),
            ComponentType.ReadOnly<CurrentSimulatedPosition>(),
            ComponentType.ReadOnly<PreviousSimulatedPosition>());
        rotInterpolationGroup = GetEntityQuery(
            ComponentType.ReadWrite<Rotation>(),
            ComponentType.ReadOnly<CurrentSimulatedRotation>(),
            ComponentType.ReadOnly<PreviousSimulatedRotation>());
    }

    [BurstCompile]
    struct PosInterpolateJob : IJobChunk
    {
        public float curWeight;
        public float prevWeight;
        public ArchetypeChunkComponentType<Translation> positionType;
        [ReadOnly] public ArchetypeChunkComponentType<CurrentSimulatedPosition> curPositionType;
        [ReadOnly] public ArchetypeChunkComponentType<PreviousSimulatedPosition> prevPositionType;
        public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
        {
            // If current was written after copying it to prev we need to interpolate, otherwise they must be identical
            if (ChangeVersionUtility.DidChange(chunk.GetComponentVersion(curPositionType), chunk.GetComponentVersion(prevPositionType)))
            {
                var prevPos = chunk.GetNativeArray(prevPositionType);
                var curPos = chunk.GetNativeArray(curPositionType);
                var pos = chunk.GetNativeArray(positionType);
                for (var ent = 0; ent < pos.Length; ++ent)
                {
                    var p = curPos[ent].Value * curWeight + prevPos[ent].Value * prevWeight;
                    pos[ent] = new Translation {Value = p};
                }
            }
        }
    }
    [BurstCompile]
    struct RotInterpolateJob : IJobChunk
    {
        public float curWeight;
        public float prevWeight;
        public ArchetypeChunkComponentType<Rotation> rotationType;
        [ReadOnly] public ArchetypeChunkComponentType<CurrentSimulatedRotation> curRotationType;
        [ReadOnly] public ArchetypeChunkComponentType<PreviousSimulatedRotation> prevRotationType;
        public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
        {
            // If current was written after copying it to prev we need to interpolate, otherwise they must be identical
            if (ChangeVersionUtility.DidChange(chunk.GetComponentVersion(curRotationType), chunk.GetComponentVersion(prevRotationType)))
            {
                var prevRot = chunk.GetNativeArray(prevRotationType);
                var curRot = chunk.GetNativeArray(curRotationType);
                var rot = chunk.GetNativeArray(rotationType);
                for (var ent = 0; ent < rot.Length; ++ent)
                {
                    var a = math.slerp(prevRot[ent].Value, curRot[ent].Value, curWeight);
                    rot[ent] = new Rotation {Value = a};
                }
            }
        }
    }

    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        var posInterpolateJob = new PosInterpolateJob();
        var rotInterpolateJob = new RotInterpolateJob();
        posInterpolateJob.positionType = GetArchetypeChunkComponentType<Translation>();
        posInterpolateJob.prevPositionType = GetArchetypeChunkComponentType<PreviousSimulatedPosition>(true);
        posInterpolateJob.curPositionType = GetArchetypeChunkComponentType<CurrentSimulatedPosition>(true);
        rotInterpolateJob.rotationType = GetArchetypeChunkComponentType<Rotation>();
        rotInterpolateJob.prevRotationType = GetArchetypeChunkComponentType<PreviousSimulatedRotation>(true);
        rotInterpolateJob.curRotationType = GetArchetypeChunkComponentType<CurrentSimulatedRotation>(true);

        posInterpolateJob.curWeight = rotInterpolateJob.curWeight = (Time.time - parameters.startTime) / parameters.fixedDeltaTime;
        posInterpolateJob.prevWeight = rotInterpolateJob.prevWeight = 1.0f - posInterpolateJob.curWeight;

        lastInterpolationVersion = GlobalSystemVersion;

        JobHandle dep = posInterpolateJob.Schedule(posInterpolationGroup, inputDeps);
        return rotInterpolateJob.Schedule(rotInterpolationGroup, dep);
    }
}

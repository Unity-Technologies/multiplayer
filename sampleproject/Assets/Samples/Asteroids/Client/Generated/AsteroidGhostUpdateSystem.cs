using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Transforms;

[UpdateInGroup(typeof(GhostUpdateSystemGroup))]
public class AsteroidGhostUpdateSystem : JobComponentSystem
{
    [BurstCompile]
    [RequireComponentTag(typeof(AsteroidSnapshotData))]
    [ExcludeComponent(typeof(PredictedEntityComponent))]
    struct UpdateInterpolatedJob : IJobForEachWithEntity<Translation, Rotation>
    {
        [NativeDisableParallelForRestriction] public BufferFromEntity<AsteroidSnapshotData> snapshotFromEntity;
        public uint targetTick;
        public void Execute(Entity entity, int index,
            ref Translation ghostTranslation,
            ref Rotation ghostRotation)
        {
            var snapshot = snapshotFromEntity[entity];
            AsteroidSnapshotData snapshotData;
            snapshot.GetDataAtTick(targetTick, out snapshotData);

            ghostTranslation.Value = snapshotData.GetTranslationValue();
            ghostRotation.Value = snapshotData.GetRotationValue();

        }
    }
    [BurstCompile]
    [RequireComponentTag(typeof(AsteroidSnapshotData), typeof(PredictedEntityComponent))]
    struct UpdatePredictedJob : IJobForEachWithEntity<Translation, Rotation>
    {
        [NativeDisableParallelForRestriction] public BufferFromEntity<AsteroidSnapshotData> snapshotFromEntity;
        public uint targetTick;
        public void Execute(Entity entity, int index,
            ref Translation ghostTranslation,
            ref Rotation ghostRotation)
        {
            var snapshot = snapshotFromEntity[entity];
            AsteroidSnapshotData snapshotData;
            snapshot.GetDataAtTick(targetTick, out snapshotData);

            ghostTranslation.Value = snapshotData.GetTranslationValue();
            ghostRotation.Value = snapshotData.GetRotationValue();

        }
    }
    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        var updateInterpolatedJob = new UpdateInterpolatedJob
        {
            snapshotFromEntity = GetBufferFromEntity<AsteroidSnapshotData>(),
            targetTick = NetworkTimeSystem.interpolateTargetTick
        };
        var updatePredictedJob = new UpdatePredictedJob
        {
            snapshotFromEntity = GetBufferFromEntity<AsteroidSnapshotData>(),
            targetTick = NetworkTimeSystem.predictTargetTick
        };
        inputDeps = updateInterpolatedJob.Schedule(this, inputDeps);
        return updatePredictedJob.Schedule(this, inputDeps);
    }
}

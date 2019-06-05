using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Transforms;

[UpdateInGroup(typeof(GhostUpdateSystemGroup))]
public class BulletGhostUpdateSystem : JobComponentSystem
{
    [BurstCompile]
    [RequireComponentTag(typeof(BulletSnapshotData))]
    [ExcludeComponent(typeof(PredictedEntityComponent))]
    struct UpdateInterpolatedJob : IJobForEachWithEntity<Translation, Rotation>
    {
        [NativeDisableParallelForRestriction] public BufferFromEntity<BulletSnapshotData> snapshotFromEntity;
        public uint targetTick;
        public void Execute(Entity entity, int index,
            ref Translation ghostTranslation,
            ref Rotation ghostRotation)
        {
            var snapshot = snapshotFromEntity[entity];
            BulletSnapshotData snapshotData;
            snapshot.GetDataAtTick(targetTick, out snapshotData);

            ghostTranslation.Value = snapshotData.GetTranslationValue();
            ghostRotation.Value = snapshotData.GetRotationValue();

        }
    }
    [BurstCompile]
    [RequireComponentTag(typeof(BulletSnapshotData), typeof(PredictedEntityComponent))]
    struct UpdatePredictedJob : IJobForEachWithEntity<Translation, Rotation>
    {
        [NativeDisableParallelForRestriction] public BufferFromEntity<BulletSnapshotData> snapshotFromEntity;
        public uint targetTick;
        public void Execute(Entity entity, int index,
            ref Translation ghostTranslation,
            ref Rotation ghostRotation)
        {
            var snapshot = snapshotFromEntity[entity];
            BulletSnapshotData snapshotData;
            snapshot.GetDataAtTick(targetTick, out snapshotData);

            ghostTranslation.Value = snapshotData.GetTranslationValue();
            ghostRotation.Value = snapshotData.GetRotationValue();

        }
    }
    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        var updateInterpolatedJob = new UpdateInterpolatedJob
        {
            snapshotFromEntity = GetBufferFromEntity<BulletSnapshotData>(),
            targetTick = NetworkTimeSystem.interpolateTargetTick
        };
        var updatePredictedJob = new UpdatePredictedJob
        {
            snapshotFromEntity = GetBufferFromEntity<BulletSnapshotData>(),
            targetTick = NetworkTimeSystem.predictTargetTick
        };
        inputDeps = updateInterpolatedJob.Schedule(this, inputDeps);
        return updatePredictedJob.Schedule(this, inputDeps);
    }
}

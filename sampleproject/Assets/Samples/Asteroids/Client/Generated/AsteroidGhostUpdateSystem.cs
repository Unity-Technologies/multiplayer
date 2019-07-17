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
    struct UpdateInterpolatedJob : IJobForEachWithEntity<Rotation, Translation>
    {
        [NativeDisableParallelForRestriction] public BufferFromEntity<AsteroidSnapshotData> snapshotFromEntity;
        public uint targetTick;
        public void Execute(Entity entity, int index,
            ref Rotation ghostRotation,
            ref Translation ghostTranslation)
        {
            var snapshot = snapshotFromEntity[entity];
            AsteroidSnapshotData snapshotData;
            snapshot.GetDataAtTick(targetTick, out snapshotData);

            ghostRotation.Value = snapshotData.GetRotationValue();
            ghostTranslation.Value = snapshotData.GetTranslationValue();

        }
    }
    [BurstCompile]
    [RequireComponentTag(typeof(AsteroidSnapshotData), typeof(PredictedEntityComponent))]
    struct UpdatePredictedJob : IJobForEachWithEntity<Rotation, Translation>
    {
        [NativeDisableParallelForRestriction] public BufferFromEntity<AsteroidSnapshotData> snapshotFromEntity;
        public uint targetTick;
        public void Execute(Entity entity, int index,
            ref Rotation ghostRotation,
            ref Translation ghostTranslation)
        {
            var snapshot = snapshotFromEntity[entity];
            AsteroidSnapshotData snapshotData;
            snapshot.GetDataAtTick(targetTick, out snapshotData);

            ghostRotation.Value = snapshotData.GetRotationValue();
            ghostTranslation.Value = snapshotData.GetTranslationValue();

        }
    }
    private NetworkTimeSystem m_NetworkTimeSystem;
    protected override void OnCreateManager()
    {
        m_NetworkTimeSystem = World.GetOrCreateSystem<NetworkTimeSystem>();
    }
    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        var updateInterpolatedJob = new UpdateInterpolatedJob
        {
            snapshotFromEntity = GetBufferFromEntity<AsteroidSnapshotData>(),
            targetTick = m_NetworkTimeSystem.interpolateTargetTick
        };
        var updatePredictedJob = new UpdatePredictedJob
        {
            snapshotFromEntity = GetBufferFromEntity<AsteroidSnapshotData>(),
            targetTick = m_NetworkTimeSystem.predictTargetTick
        };
        inputDeps = updateInterpolatedJob.Schedule(this, inputDeps);
        return updatePredictedJob.Schedule(this, inputDeps);
    }
}

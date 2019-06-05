using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Transforms;

[UpdateInGroup(typeof(GhostUpdateSystemGroup))]
public class ShipGhostUpdateSystem : JobComponentSystem
{
    [BurstCompile]
    [RequireComponentTag(typeof(ShipSnapshotData))]
    [ExcludeComponent(typeof(PredictedEntityComponent))]
    struct UpdateInterpolatedJob : IJobForEachWithEntity<Translation, Rotation, ShipStateComponentData>
    {
        [NativeDisableParallelForRestriction] public BufferFromEntity<ShipSnapshotData> snapshotFromEntity;
        public uint targetTick;
        public void Execute(Entity entity, int index,
            ref Translation ghostTranslation,
            ref Rotation ghostRotation,
            ref ShipStateComponentData ghostShipStateComponentData)
        {
            var snapshot = snapshotFromEntity[entity];
            ShipSnapshotData snapshotData;
            snapshot.GetDataAtTick(targetTick, out snapshotData);

            ghostTranslation.Value = snapshotData.GetTranslationValue();
            ghostRotation.Value = snapshotData.GetRotationValue();
            ghostShipStateComponentData.State = snapshotData.GetShipStateComponentDataState();

        }
    }
    [BurstCompile]
    [RequireComponentTag(typeof(ShipSnapshotData), typeof(PredictedEntityComponent))]
    struct UpdatePredictedJob : IJobForEachWithEntity<Translation, Rotation, Velocity, ShipStateComponentData, PlayerIdComponentData>
    {
        [NativeDisableParallelForRestriction] public BufferFromEntity<ShipSnapshotData> snapshotFromEntity;
        public uint targetTick;
        public void Execute(Entity entity, int index,
            ref Translation ghostTranslation,
            ref Rotation ghostRotation,
            ref Velocity ghostVelocity,
            ref ShipStateComponentData ghostShipStateComponentData,
            ref PlayerIdComponentData ghostPlayerIdComponentData)
        {
            var snapshot = snapshotFromEntity[entity];
            ShipSnapshotData snapshotData;
            snapshot.GetDataAtTick(targetTick, out snapshotData);

            ghostTranslation.Value = snapshotData.GetTranslationValue();
            ghostRotation.Value = snapshotData.GetRotationValue();
            ghostVelocity.Value = snapshotData.GetVelocityValue();
            ghostShipStateComponentData.State = snapshotData.GetShipStateComponentDataState();
            ghostPlayerIdComponentData.PlayerId = snapshotData.GetPlayerIdComponentDataPlayerId();

        }
    }
    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        var updateInterpolatedJob = new UpdateInterpolatedJob
        {
            snapshotFromEntity = GetBufferFromEntity<ShipSnapshotData>(),
            targetTick = NetworkTimeSystem.interpolateTargetTick
        };
        var updatePredictedJob = new UpdatePredictedJob
        {
            snapshotFromEntity = GetBufferFromEntity<ShipSnapshotData>(),
            targetTick = NetworkTimeSystem.predictTargetTick
        };
        inputDeps = updateInterpolatedJob.Schedule(this, inputDeps);
        return updatePredictedJob.Schedule(this, inputDeps);
    }
}

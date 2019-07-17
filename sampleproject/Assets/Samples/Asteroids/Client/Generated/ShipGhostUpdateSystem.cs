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
    struct UpdateInterpolatedJob : IJobForEachWithEntity<Rotation, ShipStateComponentData, Translation>
    {
        [NativeDisableParallelForRestriction] public BufferFromEntity<ShipSnapshotData> snapshotFromEntity;
        public uint targetTick;
        public void Execute(Entity entity, int index,
            ref Rotation ghostRotation,
            ref ShipStateComponentData ghostShipStateComponentData,
            ref Translation ghostTranslation)
        {
            var snapshot = snapshotFromEntity[entity];
            ShipSnapshotData snapshotData;
            snapshot.GetDataAtTick(targetTick, out snapshotData);

            ghostRotation.Value = snapshotData.GetRotationValue();
            ghostShipStateComponentData.State = snapshotData.GetShipStateComponentDataState();
            ghostTranslation.Value = snapshotData.GetTranslationValue();

        }
    }
    [BurstCompile]
    [RequireComponentTag(typeof(ShipSnapshotData), typeof(PredictedEntityComponent))]
    struct UpdatePredictedJob : IJobForEachWithEntity<PlayerIdComponentData, Rotation, ShipStateComponentData, Translation, Velocity>
    {
        [NativeDisableParallelForRestriction] public BufferFromEntity<ShipSnapshotData> snapshotFromEntity;
        public uint targetTick;
        public void Execute(Entity entity, int index,
            ref PlayerIdComponentData ghostPlayerIdComponentData,
            ref Rotation ghostRotation,
            ref ShipStateComponentData ghostShipStateComponentData,
            ref Translation ghostTranslation,
            ref Velocity ghostVelocity)
        {
            var snapshot = snapshotFromEntity[entity];
            ShipSnapshotData snapshotData;
            snapshot.GetDataAtTick(targetTick, out snapshotData);

            ghostPlayerIdComponentData.PlayerId = snapshotData.GetPlayerIdComponentDataPlayerId();
            ghostRotation.Value = snapshotData.GetRotationValue();
            ghostShipStateComponentData.State = snapshotData.GetShipStateComponentDataState();
            ghostTranslation.Value = snapshotData.GetTranslationValue();
            ghostVelocity.Value = snapshotData.GetVelocityValue();

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
            snapshotFromEntity = GetBufferFromEntity<ShipSnapshotData>(),
            targetTick = m_NetworkTimeSystem.interpolateTargetTick
        };
        var updatePredictedJob = new UpdatePredictedJob
        {
            snapshotFromEntity = GetBufferFromEntity<ShipSnapshotData>(),
            targetTick = m_NetworkTimeSystem.predictTargetTick
        };
        inputDeps = updateInterpolatedJob.Schedule(this, inputDeps);
        return updatePredictedJob.Schedule(this, inputDeps);
    }
}

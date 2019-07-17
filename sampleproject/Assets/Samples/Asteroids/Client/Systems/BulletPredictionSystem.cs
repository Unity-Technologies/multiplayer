using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Transforms;

[UpdateAfter(typeof(GhostReceiveSystemGroup))]
[UpdateBefore(typeof(AfterSimulationInterpolationSystem))]
[UpdateInGroup(typeof(ClientSimulationSystemGroup))]
public class BulletPredictionSystem : JobComponentSystem
{
    [BurstCompile]
    [RequireComponentTag(typeof(BulletTagComponentData), typeof(PredictedEntityComponent))]
    struct BulletJob : IJobForEachWithEntity<Velocity, Translation>
    {
        public float deltaTime;
        public uint targetTick;
        [NativeDisableParallelForRestriction] public BufferFromEntity<BulletSnapshotData> snapshotFromEntity;

        public void Execute(Entity entity, int index, [ReadOnly] ref Velocity velocity, ref Translation position)
        {
            var snapshot = snapshotFromEntity[entity];
            BulletSnapshotData snapshotData;
            snapshot.GetDataAtTick(targetTick, out snapshotData);
            uint sourceTick = snapshotData.Tick;
            for (uint tick = sourceTick + 1; tick != targetTick + 1; ++tick)
            {
                position.Value.xy += velocity.Value * deltaTime;
            }
        }
    }

    private NetworkTimeSystem m_NetworkTimeSystem;
    protected override void OnCreateManager()
    {
        m_NetworkTimeSystem = World.GetOrCreateSystem<NetworkTimeSystem>();
    }
    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        var topGroup = World.GetExistingSystem<ClientSimulationSystemGroup>();
        var job = new BulletJob
        {
            targetTick = m_NetworkTimeSystem.predictTargetTick,
            deltaTime = topGroup.UpdateDeltaTime,
            snapshotFromEntity = GetBufferFromEntity<BulletSnapshotData>()
        };
        return job.Schedule(this, inputDeps);
    }
}


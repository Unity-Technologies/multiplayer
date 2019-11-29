using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;
using Unity.Collections;
using Unity.Jobs;
using Unity.NetCode;

namespace Asteroids.Mixed
{
    [UpdateInGroup(typeof(GhostPredictionSystemGroup))]
    public class BulletSystem : JobComponentSystem
    {
        [BurstCompile]
        [RequireComponentTag(typeof(BulletTagComponent))]
        struct BulletJob : IJobForEach<Velocity, Translation, PredictedGhostComponent>
        {
            public uint tick;
            public float deltaTime;
            public void Execute([ReadOnly] ref Velocity velocity, ref Translation position, [ReadOnly] ref PredictedGhostComponent prediction)
            {
                if (!GhostPredictionSystemGroup.ShouldPredict(tick, prediction))
                    return;
                position.Value.xy += velocity.Value * deltaTime;
            }
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var predictionGroup = World.GetExistingSystem<GhostPredictionSystemGroup>();
            var job = new BulletJob {tick = predictionGroup.PredictingTick, deltaTime = Time.DeltaTime};
            return job.Schedule(this, inputDeps);
        }
    }
}

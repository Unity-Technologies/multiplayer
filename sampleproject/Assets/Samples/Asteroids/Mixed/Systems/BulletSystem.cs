using Unity.Entities;
using Unity.Transforms;
using Unity.NetCode;

namespace Asteroids.Mixed
{
    [UpdateInGroup(typeof(GhostPredictionSystemGroup))]
    public class BulletSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            var predictionGroup = World.GetExistingSystem<GhostPredictionSystemGroup>();
            var tick = predictionGroup.PredictingTick;
            var deltaTime = Time.DeltaTime;
            Entities.WithAll<BulletTagComponent>().ForEach((ref Translation position, in PredictedGhostComponent prediction, in Velocity velocity) =>
            {
                if (!GhostPredictionSystemGroup.ShouldPredict(tick, prediction))
                    return;
                position.Value.xy += velocity.Value * deltaTime;
            }).ScheduleParallel();
        }
    }
}

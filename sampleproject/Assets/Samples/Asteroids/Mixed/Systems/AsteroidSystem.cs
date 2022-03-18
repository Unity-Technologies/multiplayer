using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.NetCode;

namespace Asteroids.Mixed
{
    [UpdateInGroup(typeof(GhostPredictionSystemGroup))]
    public partial class AsteroidSystem : SystemBase
    {
        private GhostPredictionSystemGroup m_GhostPredictionSystemGroup;
        protected override void OnCreate()
        {
            m_GhostPredictionSystemGroup = World.GetExistingSystem<GhostPredictionSystemGroup>();
        }
        protected override void OnUpdate()
        {
            var deltaTime = Time.DeltaTime;
            var tick = m_GhostPredictionSystemGroup.PredictingTick;
            Entities.WithNone<StaticAsteroid>().WithAll<AsteroidTagComponentData>().ForEach((ref Translation position, ref Rotation rotation, in Velocity velocity, in PredictedGhostComponent pred) =>
            {
                if (!GhostPredictionSystemGroup.ShouldPredict(tick, pred))
                    return;
                position.Value.xy += velocity.Value * deltaTime;
                rotation.Value = math.mul(rotation.Value, quaternion.RotateZ(math.radians(100 * deltaTime)));
            }).ScheduleParallel();
        }
    }
}

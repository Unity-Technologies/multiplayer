using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;
using Unity.Jobs;
using Unity.NetCode;

namespace Asteroids.Server
{
    [UpdateInGroup(typeof(ServerSimulationSystemGroup))]
    public class AsteroidSystem : JobComponentSystem
    {
        [BurstCompile]
        [RequireComponentTag(typeof(AsteroidTagComponentData))]
        struct AsteroidJob : IJobForEach<Velocity, Translation, Rotation>
        {
            public float deltaTime;
            public void Execute([ReadOnly] ref Velocity velocity, ref Translation position, ref Rotation rotation)
            {
                position.Value.xy += velocity.Value * deltaTime;
                rotation.Value = math.mul(rotation.Value, quaternion.RotateZ(math.radians(100 * deltaTime)));
            }
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var asteroidJob = new AsteroidJob() {deltaTime = Time.DeltaTime};
            return asteroidJob.Schedule(this, inputDeps);
        }
    }
}

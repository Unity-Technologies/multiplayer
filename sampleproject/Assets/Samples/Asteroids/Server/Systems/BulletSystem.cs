using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;
using Unity.Collections;
using Unity.Jobs;

namespace Asteroids.Server
{
    [UpdateInGroup(typeof(ServerSimulationSystemGroup))]
    public class BulletSystem : JobComponentSystem
    {
        [BurstCompile]
        [RequireComponentTag(typeof(BulletTagComponentData))]
        struct BulletJob : IJobForEach<Velocity, Translation>
        {
            public float deltaTime;
            public void Execute([ReadOnly] ref Velocity velocity, ref Translation position)
            {
                position.Value.xy += velocity.Value * deltaTime;
            }
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var topGroup = World.GetExistingSystem<ServerSimulationSystemGroup>();
            var job = new BulletJob {deltaTime = topGroup.UpdateDeltaTime};
            return job.Schedule(this, inputDeps);
        }
    }
}

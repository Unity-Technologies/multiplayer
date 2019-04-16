using Unity.Burst;
using UnityEngine;
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
        struct BulletJob : IJobProcessComponentData<Velocity, Translation>
        {
            public float deltaTime;
            public void Execute([ReadOnly] ref Velocity velocity, ref Translation position)
            {
                position.Value += velocity.Value * deltaTime;
            }
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var job = new BulletJob {deltaTime = Time.deltaTime};
            return job.Schedule(this, inputDeps);
        }
    }
}

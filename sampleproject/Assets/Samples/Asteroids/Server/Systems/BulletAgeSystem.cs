using Unity.Burst;
using UnityEngine;
using Unity.Entities;
using Unity.Jobs;

namespace Asteroids.Server
{
    [UpdateInGroup(typeof(ServerSimulationSystemGroup))]
    public class BulletAgeSystem : JobComponentSystem
    {
        [BurstCompile]
        struct BulletJob : IJobProcessComponentDataWithEntity<BulletAgeComponentData>
        {
            public EntityCommandBuffer.Concurrent commandBuffer;
            public float deltaTime;
            public void Execute(Entity entity, int index, ref BulletAgeComponentData age)
            {
                age.age += deltaTime;
                if (age.age > age.maxAge)
                    commandBuffer.DestroyEntity(index, entity);

            }
        }

        private BeginSimulationEntityCommandBufferSystem barrier;

        protected override void OnCreateManager()
        {
            barrier = World.GetOrCreateManager<BeginSimulationEntityCommandBufferSystem>();
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var bulletJob = new BulletJob {commandBuffer = barrier.CreateCommandBuffer().ToConcurrent(), deltaTime = Time.deltaTime};
            var handle = bulletJob.Schedule(this, inputDeps);
            barrier.AddJobHandleForProducer(handle);
            return handle;
        }
    }
}

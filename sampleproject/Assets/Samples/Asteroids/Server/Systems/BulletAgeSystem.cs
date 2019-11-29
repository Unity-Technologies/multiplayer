using Unity.Burst;
using Unity.Entities;
using Unity.Jobs;
using Unity.NetCode;

namespace Asteroids.Server
{
    [UpdateInGroup(typeof(ServerSimulationSystemGroup))]
    public class BulletAgeSystem : JobComponentSystem
    {
        [BurstCompile]
        struct BulletJob : IJobForEachWithEntity<BulletAgeComponent>
        {
            public EntityCommandBuffer.Concurrent commandBuffer;
            public float deltaTime;
            public void Execute(Entity entity, int index, ref BulletAgeComponent age)
            {
                age.age += deltaTime;
                if (age.age > age.maxAge)
                    commandBuffer.DestroyEntity(index, entity);

            }
        }

        private BeginSimulationEntityCommandBufferSystem barrier;

        protected override void OnCreate()
        {
            barrier = World.GetOrCreateSystem<BeginSimulationEntityCommandBufferSystem>();
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var bulletJob = new BulletJob {commandBuffer = barrier.CreateCommandBuffer().ToConcurrent(), deltaTime = Time.DeltaTime};
            var handle = bulletJob.Schedule(this, inputDeps);
            barrier.AddJobHandleForProducer(handle);
            return handle;
        }
    }
}

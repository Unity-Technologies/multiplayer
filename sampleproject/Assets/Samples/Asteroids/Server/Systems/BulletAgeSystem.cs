using Unity.Burst;
using Unity.Entities;
using Unity.Jobs;

namespace Asteroids.Server
{
    [UpdateInGroup(typeof(ServerSimulationSystemGroup))]
    public class BulletAgeSystem : JobComponentSystem
    {
        [BurstCompile]
        struct BulletJob : IJobForEachWithEntity<BulletAgeComponentData>
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
            barrier = World.GetOrCreateSystem<BeginSimulationEntityCommandBufferSystem>();
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var topGroup = World.GetExistingSystem<ServerSimulationSystemGroup>();
            var bulletJob = new BulletJob {commandBuffer = barrier.CreateCommandBuffer().ToConcurrent(), deltaTime = topGroup.UpdateDeltaTime};
            var handle = bulletJob.Schedule(this, inputDeps);
            barrier.AddJobHandleForProducer(handle);
            return handle;
        }
    }
}

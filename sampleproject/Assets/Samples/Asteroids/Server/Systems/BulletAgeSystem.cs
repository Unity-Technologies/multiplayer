using Unity.Entities;
using Unity.NetCode;

namespace Asteroids.Server
{
    [UpdateInGroup(typeof(ServerSimulationSystemGroup))]
    public partial class BulletAgeSystem : SystemBase
    {
        private EndSimulationEntityCommandBufferSystem barrier;

        protected override void OnCreate()
        {
            barrier = World.GetOrCreateSystem<EndSimulationEntityCommandBufferSystem>();
        }

        protected override void OnUpdate()
        {
            var commandBuffer = barrier.CreateCommandBuffer().AsParallelWriter();
            var deltaTime = Time.DeltaTime;
            Entities.ForEach((Entity entity, int nativeThreadIndex, ref BulletAgeComponent age) =>
            {
                age.age += deltaTime;
                if (age.age > age.maxAge)
                    commandBuffer.DestroyEntity(nativeThreadIndex, entity);

            }).ScheduleParallel();
        }
    }
}

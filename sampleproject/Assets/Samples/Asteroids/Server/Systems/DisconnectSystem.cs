using Unity.Entities;
using Unity.Jobs;

namespace Asteroids.Server
{
    [UpdateInGroup(typeof(ServerSimulationSystemGroup))]
    [UpdateAfter(typeof(CollisionSystem))]
    public class DisconnectSystem : JobComponentSystem
    {
        private BeginSimulationEntityCommandBufferSystem m_Barrier;
        [RequireComponentTag(typeof(NetworkStreamDisconnected))]
        struct DisconnectJob : IJobForEach<CommandTargetComponent>
        {
            public EntityCommandBuffer commandBuffer;
            public void Execute(ref CommandTargetComponent state)
            {
                if (state.targetEntity != Entity.Null)
                {
                    commandBuffer.DestroyEntity(state.targetEntity);
                    state.targetEntity = Entity.Null;
                }
            }
        }

        protected override void OnCreateManager()
        {
            m_Barrier = World.GetOrCreateSystem<BeginSimulationEntityCommandBufferSystem>();
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var job = new DisconnectJob {commandBuffer = m_Barrier.CreateCommandBuffer()};
            var handle = job.ScheduleSingle(this, inputDeps);
            m_Barrier.AddJobHandleForProducer(handle);
            return handle;
        }
    }
}

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
        struct DisconnectJob : IJobProcessComponentData<PlayerStateComponentData>
        {
            public EntityCommandBuffer commandBuffer;
            public void Execute(ref PlayerStateComponentData state)
            {
                if (state.PlayerShip != Entity.Null)
                {
                    commandBuffer.DestroyEntity(state.PlayerShip);
                    state.PlayerShip = Entity.Null;
                }
            }
        }

        protected override void OnCreateManager()
        {
            m_Barrier = World.GetOrCreateManager<BeginSimulationEntityCommandBufferSystem>();
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

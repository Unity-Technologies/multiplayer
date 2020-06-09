using Unity.Entities;
using Unity.NetCode;

namespace Asteroids.Server
{
    [UpdateInGroup(typeof(ServerSimulationSystemGroup))]
    [UpdateAfter(typeof(CollisionSystem))]
    public class DisconnectSystem : SystemBase
    {
        private BeginSimulationEntityCommandBufferSystem m_Barrier;

        protected override void OnCreate()
        {
            m_Barrier = World.GetOrCreateSystem<BeginSimulationEntityCommandBufferSystem>();
        }

        protected override void OnUpdate()
        {
            var commandBuffer = m_Barrier.CreateCommandBuffer();
            Entities.WithAll<NetworkStreamDisconnected>().ForEach((ref CommandTargetComponent state) =>
            {
                if (state.targetEntity != Entity.Null)
                {
                    commandBuffer.DestroyEntity(state.targetEntity);
                    state.targetEntity = Entity.Null;
                }
            }).Schedule();
            m_Barrier.AddJobHandleForProducer(Dependency);
        }
    }
}

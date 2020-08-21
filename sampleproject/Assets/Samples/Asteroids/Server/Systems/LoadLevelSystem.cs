using Unity.Entities;
using Unity.Jobs;
using Unity.Collections;
using Unity.NetCode;

namespace Asteroids.Server
{
    public struct LevelRequestedTag : IComponentData
    {
    }

    [UpdateInGroup(typeof(ServerSimulationSystemGroup))]
    [UpdateBefore(typeof(RpcSystem))]
    public class LoadLevelSystem : SystemBase
    {
        private BeginSimulationEntityCommandBufferSystem m_Barrier;
        private EntityQuery m_LevelGroup;

        protected override void OnCreate()
        {
            m_Barrier = World.GetOrCreateSystem<BeginSimulationEntityCommandBufferSystem>();
            m_LevelGroup = GetEntityQuery(ComponentType.ReadWrite<LevelComponent>());
            RequireSingletonForUpdate<ServerSettings>();
        }

        protected override void OnUpdate()
        {
            if (m_LevelGroup.IsEmptyIgnoreFilter)
            {
                var settings = GetSingleton<ServerSettings>();
                var newLevel = EntityManager.CreateEntity();
                EntityManager.AddComponentData(newLevel, new LevelComponent
                {
                    width = settings.levelWidth,
                    height = settings.levelHeight,
                    playerForce = settings.playerForce,
                    bulletVelocity = settings.bulletVelocity
                });
                return;
            }
            JobHandle levelDep;
            var commandBuffer = m_Barrier.CreateCommandBuffer();
            var level = m_LevelGroup.ToComponentDataArrayAsync<LevelComponent>(Allocator.TempJob, out levelDep);

            var requestLoadJob = Entities.WithNone<LevelRequestedTag>().WithReadOnly(level).WithDisposeOnCompletion(level).
                ForEach((Entity entity, in NetworkIdComponent netId) =>
            {
                commandBuffer.AddComponent(entity, new LevelRequestedTag());
                var req = commandBuffer.CreateEntity();
                commandBuffer.AddComponent(req, new LevelLoadRequest
                {
                    width = level[0].width,
                    height = level[0].height,
                    playerForce = level[0].playerForce,
                    bulletVelocity = level[0].bulletVelocity
                });
                commandBuffer.AddComponent(req, new SendRpcCommandRequestComponent {TargetConnection = entity});
            }).Schedule(JobHandle.CombineDependencies(Dependency, levelDep));
            Dependency = requestLoadJob;
            m_Barrier.AddJobHandleForProducer(Dependency);
        }
    }
}

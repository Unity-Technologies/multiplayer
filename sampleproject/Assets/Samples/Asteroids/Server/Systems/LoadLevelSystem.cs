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
    public class LoadLevelSystem : JobComponentSystem
    {
        private BeginSimulationEntityCommandBufferSystem m_Barrier;
        private EntityQuery m_LevelGroup;

        [ExcludeComponent(typeof(LevelRequestedTag))]
        struct RequestLoadJob : IJobForEachWithEntity<NetworkIdComponent>
        {
            public EntityCommandBuffer commandBuffer;
            [DeallocateOnJobCompletion] [ReadOnly] public NativeArray<LevelComponent> level;
            public void Execute(Entity entity, int index, [ReadOnly] ref NetworkIdComponent netId)
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
            }
        }

        protected override void OnCreate()
        {
            m_Barrier = World.GetOrCreateSystem<BeginSimulationEntityCommandBufferSystem>();
            m_LevelGroup = GetEntityQuery(ComponentType.ReadWrite<LevelComponent>());
            RequireSingletonForUpdate<ServerSettings>();
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            if (m_LevelGroup.IsEmptyIgnoreFilter)
            {
                var settings = GetSingleton<ServerSettings>();
                var level = EntityManager.CreateEntity();
                EntityManager.AddComponentData(level, new LevelComponent
                {
                    width = settings.levelWidth,
                    height = settings.levelHeight,
                    playerForce = settings.playerForce,
                    bulletVelocity = settings.bulletVelocity
                });
                return inputDeps;
            }
            JobHandle levelDep;
            var job = new RequestLoadJob
            {
                commandBuffer = m_Barrier.CreateCommandBuffer(),
                level = m_LevelGroup.ToComponentDataArray<LevelComponent>(Allocator.TempJob, out levelDep)
            };
            var handle = job.ScheduleSingle(this, JobHandle.CombineDependencies(inputDeps, levelDep));
            m_Barrier.AddJobHandleForProducer(handle);
            return handle;
        }
    }
}

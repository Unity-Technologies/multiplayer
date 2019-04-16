using Unity.Entities;
using Unity.Jobs;
using Unity.Collections;

namespace Asteroids.Server
{
    public struct LevelRequestedTag : IComponentData
    {
    }

    [UpdateInGroup(typeof(ServerSimulationSystemGroup))]
    [UpdateBefore(typeof(NetworkStreamSendSystem))]
    [AlwaysUpdateSystem]
    public class LoadLevelSystem : JobComponentSystem
    {
        private BeginSimulationEntityCommandBufferSystem m_Barrier;
        private RpcQueue<RpcLoadLevel> m_RpcQueue;
        private ComponentGroup m_LevelGroup;

        [ExcludeComponent(typeof(LevelRequestedTag))]
        struct RequestLoadJob : IJobProcessComponentDataWithEntity<NetworkIdComponent>
        {
            public EntityCommandBuffer commandBuffer;
            public RpcQueue<RpcLoadLevel> rpcQueue;
            public BufferFromEntity<OutgoingRpcDataStreamBufferComponent> rpcBuffer;
            [DeallocateOnJobCompletion] [ReadOnly] public NativeArray<LevelComponent> level;
            public void Execute(Entity entity, int index, [ReadOnly] ref NetworkIdComponent netId)
            {
                commandBuffer.AddComponent(entity, new LevelRequestedTag());
                rpcQueue.Schedule(rpcBuffer[entity], new RpcLoadLevel {width = level[0].width, height = level[0].height});
            }
        }

        protected override void OnCreateManager()
        {
            m_Barrier = World.GetOrCreateManager<BeginSimulationEntityCommandBufferSystem>();
            m_RpcQueue = World.GetOrCreateManager<RpcSystem>().GetRpcQueue<RpcLoadLevel>();
            m_LevelGroup = GetComponentGroup(ComponentType.ReadWrite<LevelComponent>());
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            if (m_LevelGroup.IsEmptyIgnoreFilter)
            {
                var settings = GetSingleton<ServerSettings>();
                var level = EntityManager.CreateEntity();
                EntityManager.AddComponentData(level, new LevelComponent {width = settings.levelWidth, height = settings.levelHeight});
                return inputDeps;
            }
            JobHandle levelDep;
            var job = new RequestLoadJob
            {
                commandBuffer = m_Barrier.CreateCommandBuffer(),
                rpcQueue = m_RpcQueue,
                rpcBuffer = GetBufferFromEntity<OutgoingRpcDataStreamBufferComponent>(),
                level = m_LevelGroup.ToComponentDataArray<LevelComponent>(Allocator.TempJob, out levelDep)
            };
            var handle = job.ScheduleSingle(this, JobHandle.CombineDependencies(inputDeps, levelDep));
            m_Barrier.AddJobHandleForProducer(handle);
            return handle;
        }
    }
}

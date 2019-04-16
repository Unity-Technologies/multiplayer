using Unity.Entities;
using Unity.Jobs;
using Unity.Collections;

namespace Asteroids.Client
{
    [UpdateInGroup(typeof(ClientSimulationSystemGroup))]
    [UpdateBefore(typeof(NetworkStreamSendSystem))]
    public class LoadLevelSystem : JobComponentSystem
    {
        private BeginSimulationEntityCommandBufferSystem m_Barrier;
        private RpcQueue<RpcLevelLoaded> m_RpcQueue;
        private ComponentGroup m_LevelGroup;
        private Entity m_LevelSingleton;
        struct LoadJob : IJobProcessComponentDataWithEntity<LevelLoadRequest>
        {
            public EntityCommandBuffer.Concurrent commandBuffer;
            public RpcQueue<RpcLevelLoaded> rpcQueue;
            public BufferFromEntity<OutgoingRpcDataStreamBufferComponent> rpcFromEntity;
            public Entity levelSingleton;
            public ComponentDataFromEntity<LevelComponent> levelFromEntity;
            public void Execute(Entity entity, int index, [ReadOnly] ref LevelLoadRequest request)
            {
                commandBuffer.DestroyEntity(index, entity);
                // Check for disconnects
                if (!rpcFromEntity.Exists(request.connection))
                    return;
                // set the level size - fake loading of level
                levelFromEntity[levelSingleton] = new LevelComponent {width = request.width, height = request.height};
                commandBuffer.AddComponent(index, request.connection, new PlayerStateComponentData());
                rpcQueue.Schedule(rpcFromEntity[request.connection], new RpcLevelLoaded());
            }
        }

        protected override void OnCreateManager()
        {
            m_Barrier = World.GetOrCreateManager<BeginSimulationEntityCommandBufferSystem>();
            m_RpcQueue = World.GetOrCreateManager<RpcSystem>().GetRpcQueue<RpcLevelLoaded>();

            // The level always exist, "loading" just resizes it
            m_LevelSingleton = EntityManager.CreateEntity();
            EntityManager.AddComponentData(m_LevelSingleton, new LevelComponent {width = 0, height = 0});
            m_LevelGroup = GetComponentGroup(ComponentType.ReadWrite<LevelComponent>());
            RequireForUpdate(m_LevelGroup);
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var job = new LoadJob
            {
                commandBuffer = m_Barrier.CreateCommandBuffer().ToConcurrent(),
                rpcQueue = m_RpcQueue,
                rpcFromEntity = GetBufferFromEntity<OutgoingRpcDataStreamBufferComponent>(),
                levelSingleton = m_LevelSingleton,
                levelFromEntity = GetComponentDataFromEntity<LevelComponent>()
            };
            var handle = job.ScheduleSingle(this, inputDeps);
            m_Barrier.AddJobHandleForProducer(handle);
            return handle;
        }
    }
}

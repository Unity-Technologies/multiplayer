using Unity.Entities;
using Unity.Jobs;
using Unity.Collections;
using Unity.NetCode;

namespace Asteroids.Client
{
    [UpdateInGroup(typeof(ClientSimulationSystemGroup))]
    [UpdateBefore(typeof(RpcSystem))]
    public class LoadLevelSystem : JobComponentSystem
    {
        private BeginSimulationEntityCommandBufferSystem m_Barrier;
        private RpcQueue<RpcLevelLoaded> m_RpcQueue;
        private EntityQuery m_LevelGroup;
        private Entity m_LevelSingleton;
        struct LoadJob : IJobForEachWithEntity<LevelLoadRequest, ReceiveRpcCommandRequestComponent>
        {
            public EntityCommandBuffer.Concurrent commandBuffer;
            public RpcQueue<RpcLevelLoaded> rpcQueue;
            public BufferFromEntity<OutgoingRpcDataStreamBufferComponent> rpcFromEntity;
            public Entity levelSingleton;
            public ComponentDataFromEntity<LevelComponent> levelFromEntity;
            public void Execute(Entity entity, int index, [ReadOnly] ref LevelLoadRequest request, [ReadOnly] ref ReceiveRpcCommandRequestComponent requestSource)
            {
                commandBuffer.DestroyEntity(index, entity);
                // Check for disconnects
                if (!rpcFromEntity.Exists(requestSource.SourceConnection))
                    return;
                // set the level size - fake loading of level
                levelFromEntity[levelSingleton] = new LevelComponent
                {
                    width = request.width,
                    height = request.height,
                    playerForce = request.playerForce,
                    bulletVelocity = request.bulletVelocity
                };
                commandBuffer.AddComponent(index, requestSource.SourceConnection, new PlayerStateComponentData());
                commandBuffer.AddComponent(index, requestSource.SourceConnection, default(NetworkStreamInGame));
                rpcQueue.Schedule(rpcFromEntity[requestSource.SourceConnection], new RpcLevelLoaded());
            }
        }

        protected override void OnCreate()
        {
            m_Barrier = World.GetOrCreateSystem<BeginSimulationEntityCommandBufferSystem>();
            m_RpcQueue = World.GetOrCreateSystem<RpcSystem>().GetRpcQueue<RpcLevelLoaded>();

            // The level always exist, "loading" just resizes it
            m_LevelSingleton = EntityManager.CreateEntity();
            EntityManager.AddComponentData(m_LevelSingleton, new LevelComponent {width = 0, height = 0});
            m_LevelGroup = GetEntityQuery(ComponentType.ReadWrite<LevelComponent>());
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

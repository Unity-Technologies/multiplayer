using Unity.Entities;
using Unity.NetCode;

namespace Asteroids.Client
{
    [UpdateInGroup(typeof(ClientSimulationSystemGroup))]
    [UpdateBefore(typeof(RpcSystem))]
    public class LoadLevelSystem : SystemBase
    {
        private BeginSimulationEntityCommandBufferSystem m_Barrier;
        private RpcQueue<RpcLevelLoaded, RpcLevelLoaded> m_RpcQueue;
        private EntityQuery m_LevelGroup;
        private Entity m_LevelSingleton;

        protected override void OnCreate()
        {
            m_Barrier = World.GetOrCreateSystem<BeginSimulationEntityCommandBufferSystem>();
            m_RpcQueue = World.GetOrCreateSystem<RpcSystem>().GetRpcQueue<RpcLevelLoaded, RpcLevelLoaded>();

            // The level always exist, "loading" just resizes it
            m_LevelSingleton = EntityManager.CreateEntity();
            EntityManager.AddComponentData(m_LevelSingleton, new LevelComponent {width = 0, height = 0});
            m_LevelGroup = GetEntityQuery(ComponentType.ReadWrite<LevelComponent>());
            RequireForUpdate(m_LevelGroup);
        }

        protected override void OnUpdate()
        {
            var commandBuffer = m_Barrier.CreateCommandBuffer().AsParallelWriter();
            var rpcFromEntity = GetBufferFromEntity<OutgoingRpcDataStreamBufferComponent>();
            var levelFromEntity = GetComponentDataFromEntity<LevelComponent>();
            var levelSingleton = m_LevelSingleton;
            var rpcQueue = m_RpcQueue;
            Entities.ForEach((Entity entity, int nativeThreadIndex, in LevelLoadRequest request, in ReceiveRpcCommandRequestComponent requestSource) =>
            {
                commandBuffer.DestroyEntity(nativeThreadIndex, entity);
                // Check for disconnects
                if (!rpcFromEntity.HasComponent(requestSource.SourceConnection))
                    return;
                // set the level size - fake loading of level
                levelFromEntity[levelSingleton] = new LevelComponent
                {
                    width = request.width,
                    height = request.height,
                    playerForce = request.playerForce,
                    bulletVelocity = request.bulletVelocity
                };
                commandBuffer.AddComponent(nativeThreadIndex, requestSource.SourceConnection, new PlayerStateComponentData());
                commandBuffer.AddComponent(nativeThreadIndex, requestSource.SourceConnection, default(NetworkStreamInGame));
                rpcQueue.Schedule(rpcFromEntity[requestSource.SourceConnection], new RpcLevelLoaded());
            }).Schedule();
            m_Barrier.AddJobHandleForProducer(Dependency);
        }
    }
}

using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;

namespace Asteroids.Client
{
    [UpdateInGroup(typeof(ClientSimulationSystemGroup))]
    [UpdateBefore(typeof(RpcSystem))]
    public partial class LoadLevelSystem : SystemBase
    {
        private BeginSimulationEntityCommandBufferSystem m_Barrier;
        private RpcQueue<RpcLevelLoaded, RpcLevelLoaded> m_RpcQueue;
        private Entity m_LevelSingleton;

        protected override void OnCreate()
        {
            m_Barrier = World.GetOrCreateSystem<BeginSimulationEntityCommandBufferSystem>();
            m_RpcQueue = World.GetOrCreateSystem<RpcSystem>().GetRpcQueue<RpcLevelLoaded, RpcLevelLoaded>();

            RequireForUpdate(GetEntityQuery(ComponentType.ReadOnly<LevelLoadRequest>(), ComponentType.ReadOnly<ReceiveRpcCommandRequestComponent>()));
            // This is just here to make sure the subscen is streamed in before the client sets up the level data
            RequireSingletonForUpdate<AsteroidsSpawner>();
        }

        protected override void OnUpdate()
        {
            if (!HasSingleton<LevelComponent>())
            {
                // The level always exist, "loading" just resizes it
                m_LevelSingleton = EntityManager.CreateEntity();
                EntityManager.AddComponentData(m_LevelSingleton, new LevelComponent {width = 0, height = 0});
            }
            var commandBuffer = m_Barrier.CreateCommandBuffer().AsParallelWriter();
            var rpcFromEntity = GetBufferFromEntity<OutgoingRpcDataStreamBufferComponent>();
            var ghostFromEntity = GetComponentDataFromEntity<GhostComponent>(true);
            var levelFromEntity = GetComponentDataFromEntity<LevelComponent>();
            var levelSingleton = m_LevelSingleton;
            var rpcQueue = m_RpcQueue;
            Entities
                .WithReadOnly(ghostFromEntity)
                .ForEach((Entity entity, int nativeThreadIndex, in LevelLoadRequest request, in ReceiveRpcCommandRequestComponent requestSource) =>
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
                rpcQueue.Schedule(rpcFromEntity[requestSource.SourceConnection], ghostFromEntity, new RpcLevelLoaded());
            }).Schedule();
            m_Barrier.AddJobHandleForProducer(Dependency);
            Entities.ForEach((ref Translation trans, ref NonUniformScale scale, in LevelBorder border) => {
                var level = levelFromEntity[levelSingleton];
                if (border.Side == 0)
                {
                    trans.Value.x = level.width/2;
                    trans.Value.y = 1;
                    scale.Value.x = level.width;
                    scale.Value.y = 2;
                }
                else if (border.Side == 1)
                {
                    trans.Value.x = level.width/2;
                    trans.Value.y = level.height-1;
                    scale.Value.x = level.width;
                    scale.Value.y = 2;
                }
                else if (border.Side == 2)
                {
                    trans.Value.x = 1;
                    trans.Value.y = level.height/2;
                    scale.Value.x = 2;
                    scale.Value.y = level.height;
                }
                else if (border.Side == 3)
                {
                    trans.Value.x = level.width-1;
                    trans.Value.y = level.height/2;
                    scale.Value.x = 2;
                    scale.Value.y = level.height;
                }
            }).Schedule();
        }
    }
}

using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;

namespace Asteroids.Client
{
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ThinClientSimulation)]
    [UpdateBefore(typeof(RpcSystem))]
    [CreateAfter(typeof(RpcSystem))]
    public partial class LoadLevelSystem : SystemBase
    {
        private BeginSimulationEntityCommandBufferSystem m_Barrier;
        private RpcQueue<RpcLevelLoaded, RpcLevelLoaded> m_RpcQueue;
        private Entity m_LevelSingleton;

        protected override void OnCreate()
        {
            m_Barrier = World.GetExistingSystemManaged<BeginSimulationEntityCommandBufferSystem>();
            m_RpcQueue = GetSingleton<RpcCollection>().GetRpcQueue<RpcLevelLoaded, RpcLevelLoaded>();

            RequireForUpdate(GetEntityQuery(ComponentType.ReadOnly<LevelLoadRequest>(), ComponentType.ReadOnly<ReceiveRpcCommandRequestComponent>()));
            // This is just here to make sure the subscen is streamed in before the client sets up the level data
            RequireForUpdate<AsteroidsSpawner>();
        }

        protected override void OnUpdate()
        {
            if (!HasSingleton<LevelComponent>())
            {
                // The level always exist, "loading" just resizes it
                m_LevelSingleton = EntityManager.CreateEntity();
                EntityManager.AddComponentData(m_LevelSingleton, new LevelComponent {levelWidth = 0, levelHeight = 0});
            }
            var commandBuffer = m_Barrier.CreateCommandBuffer().AsParallelWriter();
            var rpcFromEntity = GetBufferLookup<OutgoingRpcDataStreamBufferComponent>();
            var ghostFromEntity = GetComponentLookup<GhostComponent>(true);
            var levelFromEntity = GetComponentLookup<LevelComponent>();
            var levelSingleton = m_LevelSingleton;
            var rpcQueue = m_RpcQueue;
            Entities
                .WithReadOnly(ghostFromEntity)
                .ForEach((Entity entity, int nativeThreadIndex, in LevelLoadRequest request, in ReceiveRpcCommandRequestComponent requestSource) =>
            {
                commandBuffer.DestroyEntity(nativeThreadIndex, entity);
                // Check for disconnects
                if (!rpcFromEntity.HasBuffer(requestSource.SourceConnection))
                    return;
                // set the level size - fake loading of level
                levelFromEntity[levelSingleton] = request.levelData;

                commandBuffer.AddComponent(nativeThreadIndex, requestSource.SourceConnection, new PlayerStateComponentData());
                commandBuffer.AddComponent(nativeThreadIndex, requestSource.SourceConnection, default(NetworkStreamInGame));
                rpcQueue.Schedule(rpcFromEntity[requestSource.SourceConnection], ghostFromEntity, new RpcLevelLoaded());
            }).Schedule();
            m_Barrier.AddJobHandleForProducer(Dependency);
            Entities.ForEach((ref Translation trans, ref NonUniformScale scale, in LevelBorder border) => {
                var level = levelFromEntity[levelSingleton];
                if (border.Side == 0)
                {
                    trans.Value.x = level.levelWidth/2f;
                    trans.Value.y = 1;
                    scale.Value.x = level.levelWidth;
                    scale.Value.y = 2;
                }
                else if (border.Side == 1)
                {
                    trans.Value.x = level.levelWidth/2f;
                    trans.Value.y = level.levelHeight-1;
                    scale.Value.x = level.levelWidth;
                    scale.Value.y = 2;
                }
                else if (border.Side == 2)
                {
                    trans.Value.x = 1;
                    trans.Value.y = level.levelHeight/2f;
                    scale.Value.x = 2;
                    scale.Value.y = level.levelHeight;
                }
                else if (border.Side == 3)
                {
                    trans.Value.x = level.levelWidth-1;
                    trans.Value.y = level.levelHeight/2f;
                    scale.Value.x = 2;
                    scale.Value.y = level.levelHeight;
                }
            }).Schedule();
        }
    }
}

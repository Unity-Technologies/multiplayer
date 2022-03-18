using Unity.Entities;
using Unity.Jobs;
using Unity.Collections;
using Unity.NetCode;
using Unity.Mathematics;

namespace Asteroids.Server
{
    public struct LevelRequestedTag : IComponentData
    {
    }

    [UpdateInGroup(typeof(ServerSimulationSystemGroup))]
    [UpdateBefore(typeof(RpcSystem))]
    public partial class LoadLevelSystem : SystemBase
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
            if (!HasSingleton<GhostDistanceImportance>())
            {
                var settings = GetSingleton<ServerSettings>();
                // Try to store a bit less than full chunks to avoid fragmenting the data too much
                var maxAsteroidsPerTile = 25;
                var minTileSize = 256;
                float asteroidsPerPx = (float)settings.numAsteroids / (float)(settings.levelWidth*settings.levelHeight);
                // We want to make sure that asteroidsPerPx * tileSize * tileSize = maxAsteroidsPerTile
                int tileSize = math.max(minTileSize, (int)math.ceil(math.sqrt((float)maxAsteroidsPerTile / asteroidsPerPx)));
                var grid = EntityManager.CreateEntity();
                EntityManager.AddComponentData(grid, new GhostDistanceImportance
                {
                    ScaleImportanceByDistance = GhostDistanceImportance.DefaultScaleFunctionPointer,
                    TileSize = new int3(tileSize, tileSize, 256),
                    TileCenter = new int3(0, 0, 128),
                    TileBorderWidth = new float3(1f, 1f, 1f)
                });
            }
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

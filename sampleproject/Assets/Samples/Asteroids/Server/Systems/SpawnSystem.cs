using System.Diagnostics;
using Unity.Entities;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.NetCode;

namespace Asteroids.Server
{
    [UpdateInGroup(typeof(ServerSimulationSystemGroup))]
    public class AsteroidSpawnSystem : SystemBase
    {
        private EntityQuery m_AsteroidGroup;
        private BeginSimulationEntityCommandBufferSystem m_Barrier;
        private ServerSimulationSystemGroup m_ServerSimulationSystemGroup;
        private EntityQuery m_LevelGroup;
        private EntityQuery m_ConnectionGroup;
        private Entity m_Prefab;
        private float m_Radius;

        protected override void OnCreate()
        {
            m_AsteroidGroup = GetEntityQuery(ComponentType.ReadWrite<AsteroidTagComponentData>());
            m_Barrier = World.GetOrCreateSystem<BeginSimulationEntityCommandBufferSystem>();
            m_ServerSimulationSystemGroup = World.GetExistingSystem<ServerSimulationSystemGroup>();

            m_LevelGroup = GetEntityQuery(ComponentType.ReadWrite<LevelComponent>());
            RequireForUpdate(m_LevelGroup);
            m_ConnectionGroup = GetEntityQuery(ComponentType.ReadWrite<NetworkStreamConnection>());
        }

        protected override void OnUpdate()
        {
            if (m_ConnectionGroup.IsEmptyIgnoreFilter)
            {
                // No connected players, just destroy all asteroids to save CPU
                EntityManager.DestroyEntity(m_AsteroidGroup);
                return;
            }

            var settings = GetSingleton<ServerSettings>();
            if (m_Prefab == Entity.Null)
            {
                var prefabEntity = GetSingletonEntity<GhostPrefabCollectionComponent>();
                var prefabs = EntityManager.GetBuffer<GhostPrefabBuffer>(prefabEntity);
                for (int i = 0; i < prefabs.Length; ++i)
                {
                    if (EntityManager.HasComponent<AsteroidTagComponentData>(prefabs[i].Value) && (EntityManager.HasComponent<StaticAsteroid>(prefabs[i].Value) == settings.staticAsteroidOptimization))
                        m_Prefab = prefabs[i].Value;
                }
                if (m_Prefab == Entity.Null)
                    return;
                m_Radius = EntityManager.GetComponentData<CollisionSphereComponent>(m_Prefab).radius;

            }

            JobHandle levelHandle;
            var commandBuffer = m_Barrier.CreateCommandBuffer();
            var count = m_AsteroidGroup.CalculateEntityCountWithoutFiltering();
            var asteroidPrefab = m_Prefab;
            var asteroidRadius = m_Radius;
            var level = m_LevelGroup.ToComponentDataArrayAsync<LevelComponent>(Allocator.TempJob, out levelHandle);
            var rand = new Unity.Mathematics.Random((uint)Stopwatch.GetTimestamp());
            var tick = m_ServerSimulationSystemGroup.ServerTick;
            Dependency = Job.WithDisposeOnCompletion(level).WithCode(() => {
                for (int i = count; i < settings.numAsteroids; ++i)
                {
                    // Spawn asteroid at random pos

                    var padding = 2 * asteroidRadius;
                    var pos = new Translation{Value = new float3(rand.NextFloat(padding, level[0].width-padding),
                        rand.NextFloat(padding, level[0].height-padding), 0)};
                    float angle = rand.NextFloat(-0.0f, 359.0f);
                    var rot = new Rotation{Value = quaternion.RotateZ(math.radians(angle))};

                    var vel = new Velocity {Value = math.mul(rot.Value, new float3(0, settings.asteroidVelocity, 0)).xy};

                    var e = commandBuffer.Instantiate(asteroidPrefab);

                    commandBuffer.SetComponent(e, pos);
                    commandBuffer.SetComponent(e, rot);
                    if (settings.staticAsteroidOptimization)
                        commandBuffer.SetComponent(e, new StaticAsteroid{InitialPosition = pos.Value.xy, InitialVelocity = vel.Value, InitialAngle = angle, SpawnTick = tick});
                    else
                        commandBuffer.SetComponent(e, vel);
                }
            }).Schedule(JobHandle.CombineDependencies(Dependency, levelHandle));
            m_Barrier.AddJobHandleForProducer(Dependency);
        }
    }

    public struct ShipSpawnInProgress : IComponentData
    {
    }

    [UpdateInGroup(typeof(ServerSimulationSystemGroup))]
    public class PlayerSpawnSystem : SystemBase
    {
        private BeginSimulationEntityCommandBufferSystem m_Barrier;
        private EntityQuery m_LevelGroup;
        private Entity m_Prefab;
        private float m_Radius;

        protected override void OnCreate()
        {
            m_Barrier = World.GetOrCreateSystem<BeginSimulationEntityCommandBufferSystem>();
            m_LevelGroup = GetEntityQuery(ComponentType.ReadWrite<LevelComponent>());
            RequireForUpdate(m_LevelGroup);
            RequireSingletonForUpdate<GhostPrefabCollectionComponent>();
        }

        protected override void OnUpdate()
        {
            if (m_Prefab == Entity.Null)
            {
                var prefabEntity = GetSingletonEntity<GhostPrefabCollectionComponent>();
                var prefabs = EntityManager.GetBuffer<GhostPrefabBuffer>(prefabEntity);
                for (int i = 0; i < prefabs.Length; ++i)
                {
                    if (EntityManager.HasComponent<ShipTagComponentData>(prefabs[i].Value))
                        m_Prefab = prefabs[i].Value;
                }
                if (m_Prefab == Entity.Null)
                    return;
                m_Radius = EntityManager.GetComponentData<CollisionSphereComponent>(m_Prefab).radius;
            }

            JobHandle levelHandle;
            var commandBuffer = m_Barrier.CreateCommandBuffer();
            var playerStateFromEntity = GetComponentDataFromEntity<PlayerStateComponentData>();
            var commandTargetFromEntity = GetComponentDataFromEntity<CommandTargetComponent>();
            var networkIdFromEntity = GetComponentDataFromEntity<NetworkIdComponent>();
            var shipPrefab = m_Prefab;
            var shipRadius = m_Radius;
            var level = m_LevelGroup.ToComponentDataArrayAsync<LevelComponent>(Allocator.TempJob, out levelHandle);
            var rand = new Unity.Mathematics.Random((uint) Stopwatch.GetTimestamp());

            Dependency = Entities.WithReadOnly(level).WithDisposeOnCompletion(level).
                ForEach((Entity entity, in PlayerSpawnRequest request,
                in ReceiveRpcCommandRequestComponent requestSource) =>
            {
                // Destroy the request
                commandBuffer.DestroyEntity(entity);
                if (!playerStateFromEntity.HasComponent(requestSource.SourceConnection) ||
                    !commandTargetFromEntity.HasComponent(requestSource.SourceConnection) ||
                    commandTargetFromEntity[requestSource.SourceConnection].targetEntity != Entity.Null ||
                    playerStateFromEntity[requestSource.SourceConnection].IsSpawning != 0)
                    return;
                var ship = commandBuffer.Instantiate(shipPrefab);
                var padding = 2 * shipRadius;
                var pos = new Translation
                {
                    Value = new float3(rand.NextFloat(padding, level[0].width - padding),
                        rand.NextFloat(padding, level[0].height - padding), 0)
                };
                //var pos = new PositionComponentData(GameSettings.mapWidth / 2, GameSettings.mapHeight / 2);
                var rot = new Rotation {Value = quaternion.RotateZ(math.radians(90f))};

                commandBuffer.SetComponent(ship, pos);
                commandBuffer.SetComponent(ship, rot);
                commandBuffer.SetComponent(ship, new GhostOwnerComponent {NetworkId = networkIdFromEntity[requestSource.SourceConnection].Value});
                commandBuffer.SetComponent(ship, new PlayerIdComponentData {PlayerEntity = requestSource.SourceConnection});

                commandBuffer.AddComponent(ship, new ShipSpawnInProgress());

                // Mark the player as currently spawning
                playerStateFromEntity[requestSource.SourceConnection] = new PlayerStateComponentData {IsSpawning = 1};
            }).Schedule(JobHandle.CombineDependencies(Dependency, levelHandle));
            m_Barrier.AddJobHandleForProducer(Dependency);
        }
    }

    [UpdateInGroup(typeof(ServerSimulationSystemGroup))]
    [UpdateBefore(typeof(GhostSendSystem))]
    public class PlayerCompleteSpawnSystem : SystemBase
    {
        private BeginSimulationEntityCommandBufferSystem m_Barrier;

        protected override void OnCreate()
        {
            m_Barrier = World.GetOrCreateSystem<BeginSimulationEntityCommandBufferSystem>();
        }

        protected override void OnUpdate()
        {
            var commandBuffer = m_Barrier.CreateCommandBuffer();
            var playerStateFromEntity = GetComponentDataFromEntity<PlayerStateComponentData>();
            var commandTargetFromEntity = GetComponentDataFromEntity<CommandTargetComponent>();
            var connectionFromEntity = GetComponentDataFromEntity<NetworkStreamConnection>();

            Entities.WithAll<ShipSpawnInProgress>().
                ForEach((Entity entity, in PlayerIdComponentData player) =>
                {
                    if (!playerStateFromEntity.HasComponent(player.PlayerEntity) ||
                        !connectionFromEntity[player.PlayerEntity].Value.IsCreated)
                    {
                        // Player was disconnected during spawn, or other error
                        commandBuffer.DestroyEntity(entity);
                        return;
                    }

                    commandBuffer.RemoveComponent<ShipSpawnInProgress>(entity);
                    commandTargetFromEntity[player.PlayerEntity] = new CommandTargetComponent {targetEntity = entity};
                    playerStateFromEntity[player.PlayerEntity] = new PlayerStateComponentData {IsSpawning = 0};
                }).Schedule();
            m_Barrier.AddJobHandleForProducer(Dependency);
        }
    }
}

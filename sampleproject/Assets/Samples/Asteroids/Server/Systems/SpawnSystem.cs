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
    public class AsteroidSpawnSystem : JobComponentSystem
    {
        struct SpawnJob : IJob
        {
            public EntityCommandBuffer commandBuffer;
            public int count;
            public int targetCount;
            public Entity asteroidPrefab;
            public float asteroidRadius;
            public float asteroidVelocity;
            [ReadOnly] [DeallocateOnJobCompletion] public NativeArray<LevelComponent> level;

            public Unity.Mathematics.Random rand;

            public void Execute()
            {
                for (int i = count; i < targetCount; ++i)
                {
                    // Spawn asteroid at random pos

                    var padding = 2 * asteroidRadius;
                    var pos = new Translation{Value = new float3(rand.NextFloat(padding, level[0].width-padding),
                        rand.NextFloat(padding, level[0].height-padding), 0)};
                    var rot = new Rotation{Value = quaternion.RotateZ(math.radians(rand.NextFloat(-0.0f, 359.0f)))};

                    var vel = new Velocity {Value = math.mul(rot.Value, new float3(0, asteroidVelocity, 0)).xy};

                    var e = commandBuffer.Instantiate(asteroidPrefab);

                    commandBuffer.SetComponent(e, pos);
                    commandBuffer.SetComponent(e, rot);
                    commandBuffer.SetComponent(e, vel);
                }
            }
        }

        protected override void OnCreate()
        {
            asteroidGroup = GetEntityQuery(ComponentType.ReadWrite<AsteroidTagComponentData>());
            barrier = World.GetOrCreateSystem<BeginSimulationEntityCommandBufferSystem>();

            m_LevelGroup = GetEntityQuery(ComponentType.ReadWrite<LevelComponent>());
            RequireForUpdate(m_LevelGroup);
            m_connectionGroup = GetEntityQuery(ComponentType.ReadWrite<NetworkStreamConnection>());
        }

        private NativeArray<int> count;
        private EntityQuery asteroidGroup;
        private BeginSimulationEntityCommandBufferSystem barrier;
        private EntityQuery m_LevelGroup;
        private EntityQuery m_connectionGroup;
        private Entity m_Prefab;
        private float m_Radius;
        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            if (m_connectionGroup.IsEmptyIgnoreFilter)
            {
                // No connected players, just destroy all asteroids to save CPU
                inputDeps.Complete();
                World.EntityManager.DestroyEntity(asteroidGroup);
                return default(JobHandle);
            }

            if (m_Prefab == Entity.Null)
            {
                var prefabs = GetSingleton<GhostPrefabCollectionComponent>();
                var serverPrefabs = EntityManager.GetBuffer<GhostPrefabBuffer>(prefabs.serverPrefabs);
                m_Prefab = serverPrefabs[AsteroidsGhostSerializerCollection.FindGhostType<AsteroidSnapshotData>()].Value;
                m_Radius = EntityManager.GetComponentData<CollisionSphereComponent>(m_Prefab).radius;

            }
            var settings = GetSingleton<ServerSettings>();
            var maxAsteroids = settings.numAsteroids;

            JobHandle levelHandle;
            var spawnJob = new SpawnJob
            {
                commandBuffer = barrier.CreateCommandBuffer(),
                count = asteroidGroup.CalculateEntityCountWithoutFiltering(),
                targetCount = maxAsteroids,
                asteroidPrefab = m_Prefab,
                asteroidRadius = m_Radius,
                asteroidVelocity = settings.asteroidVelocity,
                level = m_LevelGroup.ToComponentDataArrayAsync<LevelComponent>(Allocator.TempJob, out levelHandle),
                rand = new Unity.Mathematics.Random((uint)Stopwatch.GetTimestamp())
            };
            var handle = spawnJob.Schedule(JobHandle.CombineDependencies(inputDeps, levelHandle));
            barrier.AddJobHandleForProducer(handle);
            return handle;
        }
    }

    public struct ShipSpawnInProgressTag : IComponentData
    {
    }

    [UpdateInGroup(typeof(ServerSimulationSystemGroup))]
    public class PlayerSpawnSystem : SystemBase
    {
        protected override void OnCreate()
        {
            m_Barrier = World.GetOrCreateSystem<BeginSimulationEntityCommandBufferSystem>();
            m_LevelGroup = GetEntityQuery(ComponentType.ReadWrite<LevelComponent>());
            RequireForUpdate(m_LevelGroup);
        }

        private BeginSimulationEntityCommandBufferSystem m_Barrier;
        private EntityQuery m_LevelGroup;
        private Entity m_Prefab;
        private float m_Radius;

        protected override void OnUpdate()
        {
            if (m_Prefab == Entity.Null)
            {
                var prefabs = GetSingleton<GhostPrefabCollectionComponent>();
                var serverPrefabs = EntityManager.GetBuffer<GhostPrefabBuffer>(prefabs.serverPrefabs);
                m_Prefab = serverPrefabs[AsteroidsGhostSerializerCollection.FindGhostType<ShipSnapshotData>()].Value;
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

            var  spawnJob = Entities.WithReadOnly(level).WithDeallocateOnJobCompletion(level).
                ForEach((Entity entity, in PlayerSpawnRequest request,
                in ReceiveRpcCommandRequestComponent requestSource) =>
            {
                // Destroy the request
                commandBuffer.DestroyEntity(entity);
                if (!playerStateFromEntity.Exists(requestSource.SourceConnection) ||
                    !commandTargetFromEntity.Exists(requestSource.SourceConnection) ||
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
                commandBuffer.SetComponent(ship,
                    new PlayerIdComponentData
                    {
                        PlayerId = networkIdFromEntity[requestSource.SourceConnection].Value,
                        PlayerEntity = requestSource.SourceConnection
                    });

                commandBuffer.AddComponent(ship, new ShipSpawnInProgressTag());

                // Mark the player as currently spawning
                playerStateFromEntity[requestSource.SourceConnection] = new PlayerStateComponentData {IsSpawning = 1};
            }).Schedule(JobHandle.CombineDependencies(Dependency, levelHandle));
            Dependency = spawnJob;
            m_Barrier.AddJobHandleForProducer(Dependency);
        }
    }

    [UpdateInGroup(typeof(ServerSimulationSystemGroup))]
    [UpdateBefore(typeof(AsteroidsGhostSendSystem))]
    public class PlayerCompleteSpawnSystem : SystemBase
    {
        protected override void OnCreate()
        {
            m_Barrier = World.GetOrCreateSystem<BeginSimulationEntityCommandBufferSystem>();
        }

        private BeginSimulationEntityCommandBufferSystem m_Barrier;

        protected override void OnUpdate()
        {
            var commandBuffer = m_Barrier.CreateCommandBuffer();
            var playerStateFromEntity = GetComponentDataFromEntity<PlayerStateComponentData>();
            var commandTargetFromEntity = GetComponentDataFromEntity<CommandTargetComponent>();
            var connectionFromEntity = GetComponentDataFromEntity<NetworkStreamConnection>();

            Entities.WithAll<ShipSpawnInProgressTag>().
                ForEach((Entity entity, in PlayerIdComponentData player) =>
                {
                    if (!playerStateFromEntity.Exists(player.PlayerEntity) ||
                        !connectionFromEntity[player.PlayerEntity].Value.IsCreated)
                    {
                        // Player was disconnected during spawn, or other error
                        commandBuffer.DestroyEntity(entity);
                        return;
                    }

                    commandBuffer.RemoveComponent<ShipSpawnInProgressTag>(entity);
                    commandTargetFromEntity[player.PlayerEntity] = new CommandTargetComponent {targetEntity = entity};
                    playerStateFromEntity[player.PlayerEntity] = new PlayerStateComponentData {IsSpawning = 0};
                }).Schedule();
            m_Barrier.AddJobHandleForProducer(Dependency);
        }
    }
}

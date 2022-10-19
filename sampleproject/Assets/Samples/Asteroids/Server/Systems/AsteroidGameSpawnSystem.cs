using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace Asteroids.Server
{
    /// <summary>Handles spawning of Ships and Asteroids.</summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct AsteroidGameSpawnSystem : ISystem
    {
        EntityQuery m_LevelQuery;
        EntityQuery m_ConnectionQuery;
        EntityQuery m_ShipQuery;
        EntityQuery m_DynamicAsteroidsQuery;
        EntityQuery m_StaticAsteroidsQuery;

        Entity m_AsteroidPrefab;
        Entity m_ShipPrefab;

        float m_AsteroidRadius;
        float m_ShipRadius;

        ComponentLookup<PlayerStateComponentData> playerStateFromEntity;
        ComponentLookup<CommandTargetComponent> commandTargetFromEntity;
        ComponentLookup<NetworkIdComponent> networkIdFromEntity;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            var builder = new EntityQueryBuilder(Allocator.Temp).WithAll<Translation, ShipStateComponentData>();

            m_ShipQuery = state.GetEntityQuery(builder);

            builder.Reset();
            builder.WithAll<Translation, AsteroidTagComponentData>()
                .WithNone<StaticAsteroid>();

            m_DynamicAsteroidsQuery = state.GetEntityQuery(builder);

            builder.Reset();
            builder.WithAll<StaticAsteroid>();

            m_StaticAsteroidsQuery = state.GetEntityQuery(builder);

            builder.Reset();
            builder.WithAllRW<LevelComponent>();

            m_LevelQuery = state.GetEntityQuery(builder);

            builder.Reset();
            builder.WithAllRW<NetworkStreamConnection>();

            m_ConnectionQuery = state.GetEntityQuery(builder);

            state.RequireForUpdate(m_LevelQuery);
            state.RequireForUpdate<AsteroidsSpawner>();

            playerStateFromEntity = state.GetComponentLookup<PlayerStateComponentData>();
            commandTargetFromEntity = state.GetComponentLookup<CommandTargetComponent>();
            networkIdFromEntity = state.GetComponentLookup<NetworkIdComponent>();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (m_ConnectionQuery.IsEmptyIgnoreFilter)
            {
                // No connected players, just destroy all asteroids to save CPU
                state.EntityManager.DestroyEntity(m_StaticAsteroidsQuery);
                state.EntityManager.DestroyEntity(m_DynamicAsteroidsQuery);
                return;
            }

            var settings = SystemAPI.GetSingleton<ServerSettings>();
            if (m_AsteroidPrefab == Entity.Null || m_ShipPrefab == Entity.Null)
            {
                var asteroidsSpawner = SystemAPI.GetSingleton<AsteroidsSpawner>();
                m_AsteroidPrefab = settings.levelData.staticAsteroidOptimization ? asteroidsSpawner.StaticAsteroid : asteroidsSpawner.Asteroid;
                m_ShipPrefab = asteroidsSpawner.Ship;

                if (m_AsteroidPrefab == Entity.Null || m_ShipPrefab == Entity.Null)
                    return;

                m_AsteroidRadius = state.EntityManager.GetComponentData<CollisionSphereComponent>(m_AsteroidPrefab).radius;
                m_ShipRadius = state.EntityManager.GetComponentData<CollisionSphereComponent>(m_ShipPrefab).radius;
            }

            var ecb = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged);

            playerStateFromEntity.Update(ref state);
            commandTargetFromEntity.Update(ref state);
            networkIdFromEntity.Update(ref state);

            var dynamicAsteroidEntities = m_DynamicAsteroidsQuery.ToEntityListAsync(state.WorldUpdateAllocator,
                out var asteroidEntitiesHandle);
            var dynamicAsteroidTranslations = m_DynamicAsteroidsQuery.ToComponentDataListAsync<Translation>(state.WorldUpdateAllocator,
                out var asteroidTranslationsHandle);
            var staticAsteroids = m_StaticAsteroidsQuery.ToComponentDataListAsync<StaticAsteroid>(state.WorldUpdateAllocator,
                out var staticAsteroidsHandle);
            var staticAsteroidEntities = m_StaticAsteroidsQuery.ToEntityListAsync(state.WorldUpdateAllocator,
                out var staticAsteroidEntitiesHandle);
            var shipTranslations = m_ShipQuery.ToComponentDataListAsync<Translation>(state.WorldUpdateAllocator,
                out var shipTranslationsHandle);
            var level = m_LevelQuery.ToComponentDataListAsync<LevelComponent>(state.WorldUpdateAllocator,
                out var levelHandle);

            var jobHandleIt = 0;
            var jobHandles = new NativeArray<JobHandle>(6, Allocator.Temp);
            jobHandles[jobHandleIt++] = asteroidEntitiesHandle;
            jobHandles[jobHandleIt++] = asteroidTranslationsHandle;
            jobHandles[jobHandleIt++] = staticAsteroidsHandle;
            jobHandles[jobHandleIt++] = staticAsteroidEntitiesHandle;
            jobHandles[jobHandleIt++] = shipTranslationsHandle;
            jobHandles[jobHandleIt] = levelHandle;

            var combinedDependencies = JobHandle.CombineDependencies(jobHandles);
            jobHandles.Dispose();

            state.Dependency = JobHandle.CombineDependencies(state.Dependency, combinedDependencies);

            var tick = SystemAPI.GetSingleton<NetworkTime>().ServerTick;
            var random = new Random(tick.SerializedData);

            SystemAPI.TryGetSingleton<ClientServerTickRate>(out var tickRate);
            tickRate.ResolveDefaults();
            var fixedDeltaTime = 1.0f / (float) tickRate.SimulationTickRate;

            var shipLevelPadding = m_ShipRadius + 50;
            var asteroidLevelPadding = m_AsteroidRadius + 3;
            var minShipAsteroidSpawnDistance = m_ShipRadius + m_AsteroidRadius + 100;
            var minShipToShipSpawnDistance = (m_ShipRadius + m_ShipRadius) + 300;

            var shipTranslationsList = new NativeList<Translation>(64, state.WorldUpdateAllocator);

            var shipListJob = new CreateShipListJob
            {
                shipTranslationsIn = shipTranslations,
                shipTranslationsOut = shipTranslationsList,
            };
            state.Dependency = shipListJob.Schedule(state.Dependency);

            var spawnPlayerShips = new SpawnPlayerShips
            {
                ecb = ecb,
                playerStateFromEntity = playerStateFromEntity,
                commandTargetFromEntity = commandTargetFromEntity,
                networkIdFromEntity = networkIdFromEntity,
                shipTranslations = shipTranslationsList,
                dynamicAsteroidTranslations = dynamicAsteroidTranslations,
                staticAsteroids = staticAsteroids,
                dynamicAsteroidEntities = dynamicAsteroidEntities,
                staticAsteroidEntities = staticAsteroidEntities,
                level = level,
                random = random,
                tick = tick,
                shipPrefab = m_ShipPrefab,
                fixedDeltaTime = fixedDeltaTime,
                shipLevelPadding = shipLevelPadding,
                minShipAsteroidSpawnDistance = minShipAsteroidSpawnDistance,
                minShipToShipSpawnDistance = minShipToShipSpawnDistance
            };
            state.Dependency = spawnPlayerShips.Schedule(state.Dependency);

            var spawnAsteroids = new SpawnAllAsteroids
            {
                ecb = ecb,
                dynamicAsteroidEntities = dynamicAsteroidEntities,
                staticAsteroidEntities = staticAsteroidEntities,
                shipTranslationsList = shipTranslationsList,
                level = level,
                random = random,
                tick = tick,
                asteroidPrefab = m_AsteroidPrefab,
                asteroidLevelPadding = asteroidLevelPadding,
                minShipAsteroidSpawnDistance = minShipAsteroidSpawnDistance,
                numAsteroids = settings.levelData.numAsteroids,
                asteroidVelocity = settings.levelData.asteroidVelocity,
                staticAsteroidOptimization = settings.levelData.staticAsteroidOptimization
            };
            state.Dependency = spawnAsteroids.Schedule(state.Dependency);
        }

        [BurstCompile]
        struct CreateShipListJob : IJob
        {
            [ReadOnly] public NativeList<Translation> shipTranslationsIn;
            public NativeList<Translation> shipTranslationsOut;
            public void Execute()
            {
                shipTranslationsOut.AddRange(shipTranslationsIn.AsArray());
            }
        }

        [BurstCompile]
        internal partial struct SpawnPlayerShips : IJobEntity
        {
            public EntityCommandBuffer ecb;
            public ComponentLookup<PlayerStateComponentData> playerStateFromEntity;
            public ComponentLookup<CommandTargetComponent> commandTargetFromEntity;
            public ComponentLookup<NetworkIdComponent> networkIdFromEntity;
            public NativeList<Translation> shipTranslations;
            [ReadOnly] public NativeList<Translation> dynamicAsteroidTranslations;
            [ReadOnly] public NativeList<StaticAsteroid> staticAsteroids;
            [ReadOnly] public NativeList<Entity> dynamicAsteroidEntities;
            [ReadOnly] public NativeList<Entity> staticAsteroidEntities;
            [ReadOnly] public NativeList<LevelComponent> level;

            public Random random;
            public NetworkTick tick;
            public Entity shipPrefab;
            public float fixedDeltaTime;
            public float shipLevelPadding;
            public float minShipAsteroidSpawnDistance;
            public float minShipToShipSpawnDistance;

            void Execute(Entity entity, in PlayerSpawnRequest request, in ReceiveRpcCommandRequestComponent requestSource)
            {
                // Destroy the spawn request:
                ecb.DestroyEntity(entity);

                // Is request even valid?
                if (!playerStateFromEntity.HasComponent(requestSource.SourceConnection) ||
                    !commandTargetFromEntity.HasComponent(requestSource.SourceConnection) ||
                    commandTargetFromEntity[requestSource.SourceConnection].targetEntity != Entity.Null ||
                    playerStateFromEntity[requestSource.SourceConnection].IsSpawning != 0)
                    return;

                // Try find a random spawn position for the Ship that isn't near another player.
                // Don't allow failure though, just take a "bad" position instead.
                TryFindSpawnPos(ref random, shipTranslations.AsArray(), level[0], shipLevelPadding, minShipToShipSpawnDistance, out var validShipPos);

                // Instantiate ship:
                var shipEntity = ecb.Instantiate(shipPrefab);
                var pos = new Translation {Value = validShipPos};
                var rot = new Rotation {Value = quaternion.RotateZ(math.radians(90f))};

                ecb.SetComponent(shipEntity, pos);
                ecb.SetComponent(shipEntity, rot);
                ecb.SetComponent(shipEntity, new GhostOwnerComponent {NetworkId = networkIdFromEntity[requestSource.SourceConnection].Value});
                ecb.SetComponent(shipEntity, new PlayerIdComponentData {PlayerEntity = requestSource.SourceConnection});
                ecb.SetComponent(requestSource.SourceConnection, new CommandTargetComponent {targetEntity = shipEntity});
                ecb.SetComponent(requestSource.SourceConnection, new PlayerStateComponentData {IsSpawning = 0});
                ecb.AppendToBuffer(requestSource.SourceConnection, new LinkedEntityGroup {Value = shipEntity});

                // Add to the list to prevent asteroids below from spawning near them.
                shipTranslations.Add(pos);

                // Mark the player as currently spawning
                playerStateFromEntity[requestSource.SourceConnection] = new PlayerStateComponentData {IsSpawning = 1};

                // Destroy asteroids that are too close to this spawn:
                var minShipAsteroidSpawnDistanceSqr = minShipAsteroidSpawnDistance * minShipAsteroidSpawnDistance;
                for (int i = 0; i < dynamicAsteroidTranslations.Length; i++)
                {
                    if (math.distancesq(dynamicAsteroidTranslations[i].Value, validShipPos) < minShipAsteroidSpawnDistanceSqr)
                        ecb.DestroyEntity(dynamicAsteroidEntities[i]);
                }
                for (int i = 0; i < staticAsteroids.Length; i++)
                {
                    if (math.distancesq(staticAsteroids[i].GetPosition(tick, 1f, fixedDeltaTime), validShipPos) < minShipAsteroidSpawnDistanceSqr)
                        ecb.DestroyEntity(staticAsteroidEntities[i]);
                }
            }
        }

        [BurstCompile]
        struct SpawnAllAsteroids : IJob
        {
            public EntityCommandBuffer ecb;
            [ReadOnly] public NativeList<Entity> dynamicAsteroidEntities;
            [ReadOnly] public NativeList<Entity> staticAsteroidEntities;
            [ReadOnly] public NativeList<Translation> shipTranslationsList;
            [ReadOnly] public NativeList<LevelComponent> level;

            public Random random;
            public NetworkTick tick;
            public Entity asteroidPrefab;
            public float asteroidLevelPadding;
            public float minShipAsteroidSpawnDistance;
            public int numAsteroids;
            public float asteroidVelocity;
            public bool staticAsteroidOptimization;

            public void Execute()
            {
                var currentNumAsteroids = staticAsteroidEntities.Length + dynamicAsteroidEntities.Length;
                for (int i = currentNumAsteroids; i < numAsteroids; ++i)
                {
                    // Spawn asteroid at random pos, assuming we can find a valid one that isn't under a ship.
                    // Don't treat this as an error (because it may happen occasionally by chance, or if the map is packed with ships).
                    // Instead, just stop attempting to spawn any more this frame.
                    if (!TryFindSpawnPos(ref random, shipTranslationsList.AsArray(), level[0], asteroidLevelPadding, minShipAsteroidSpawnDistance, out var validAsteroidPos))
                        return;

                    var pos = new Translation{Value = validAsteroidPos};
                    var angle = random.NextFloat(-0.0f, 359.0f);
                    var rot = new Rotation{Value = quaternion.RotateZ(math.radians(angle))};
                    var vel = new Velocity {Value = math.mul(rot.Value, new float3(0, asteroidVelocity, 0)).xy};

                    var e = ecb.Instantiate(asteroidPrefab);

                    ecb.SetComponent(e, pos);
                    ecb.SetComponent(e, rot);
                    if (staticAsteroidOptimization)
                        ecb.SetComponent(e,
                            new StaticAsteroid
                            {
                                InitialPosition = pos.Value.xy, InitialVelocity = vel.Value, InitialAngle = angle,
                                SpawnTick = tick
                            });
                    else
                        ecb.SetComponent(e, vel);
                }
            }
        }

        static bool TryFindSpawnPos(ref Random rand, NativeArray<Translation> avoidTranslations, LevelComponent levelComponent, float levelPadding, float minSpawnDistance, out float3 validRandomAsteroidPosition)
        {
            validRandomAsteroidPosition = 0;
            var minSpawnDistanceSqr = minSpawnDistance * minSpawnDistance;

            for (var attempt = 0; attempt < 5; attempt++)
            {
                validRandomAsteroidPosition = new float3(rand.NextFloat(levelPadding, levelComponent.levelWidth - levelPadding), rand.NextFloat(levelPadding, levelComponent.levelHeight - levelPadding), 0);

                var isValidLocation = true;
                for (var i = 0; i < avoidTranslations.Length; i++)
                {
                    if (math.distancesq(avoidTranslations[i].Value, validRandomAsteroidPosition) < minSpawnDistanceSqr)
                    {
                        isValidLocation = false;
                        break;
                    }
                }
                if(isValidLocation)
                    return true;
            }
            return false;
        }
    }
}

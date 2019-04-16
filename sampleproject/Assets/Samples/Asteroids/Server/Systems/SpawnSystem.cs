using System.Diagnostics;
using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace Asteroids.Server
{
    [UpdateInGroup(typeof(ServerSimulationSystemGroup))]
    [AlwaysUpdateSystem]
    public class AsteroidSpawnSystem : JobComponentSystem
    {
        [BurstCompile]
        struct CountJob : IJob
        {
            [DeallocateOnJobCompletion] public NativeArray<ArchetypeChunk> chunks;
            public NativeArray<int> count;
            [ReadOnly] public ArchetypeChunkEntityType entityType;
            public void Execute()
            {
                int cnt = 0;
                for (int i = 0; i < chunks.Length; ++i)
                {
                    var ents = chunks[i].GetNativeArray(entityType);
                    cnt += ents.Length;
                }

                count[0] = cnt;
            }
        }

        struct SpawnJob : IJob
        {
            public EntityCommandBuffer commandBuffer;
            public NativeArray<int> count;
            public int targetCount;
            public EntityArchetype asteroidArchetype;
            public float asteroidRadius;
            public float asteroidVelocity;
            [ReadOnly] [DeallocateOnJobCompletion] public NativeArray<LevelComponent> level;

            public Unity.Mathematics.Random rand;

            public void Execute()
            {
                for (int i = count[0]; i < targetCount; ++i)
                {
                    // Spawn asteroid at random pos

                    var padding = 2 * asteroidRadius;
                    var pos = new Translation{Value = new float3(rand.NextFloat(padding, level[0].width-padding),
                        rand.NextFloat(padding, level[0].height-padding), 0)};
                    var rot = new Rotation{Value = quaternion.RotateZ(math.radians(rand.NextFloat(-0.0f, 359.0f)))};

                    var vel = new Velocity {Value = math.mul(rot.Value, new float3(0, asteroidVelocity, 0))};

                    var e = commandBuffer.CreateEntity(asteroidArchetype);

                    commandBuffer.SetComponent(e, pos);
                    commandBuffer.SetComponent(e, rot);
                    commandBuffer.SetComponent(e, vel);
                    commandBuffer.SetComponent(
                        e, new CollisionSphereComponentData(asteroidRadius));
                }
            }
        }

        protected override void OnCreateManager()
        {
            count = new NativeArray<int>(1, Allocator.Persistent);
            asteroidGroup = GetComponentGroup(ComponentType.ReadWrite<AsteroidTagComponentData>());
            barrier = World.GetOrCreateManager<BeginSimulationEntityCommandBufferSystem>();

            m_LevelGroup = GetComponentGroup(ComponentType.ReadWrite<LevelComponent>());
            RequireForUpdate(m_LevelGroup);
            m_connectionGroup = GetComponentGroup(ComponentType.ReadWrite<NetworkStreamConnection>());
        }

        protected override void OnDestroyManager()
        {
            count.Dispose();
        }

        private NativeArray<int> count;
        private ComponentGroup asteroidGroup;
        private BeginSimulationEntityCommandBufferSystem barrier;
        private ComponentGroup m_LevelGroup;
        private ComponentGroup m_connectionGroup;
        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            if (m_connectionGroup.IsEmptyIgnoreFilter)
            {
                // No connected players, just destroy all asteroids to save CPU
                inputDeps.Complete();
                World.GetExistingManager<EntityManager>().DestroyEntity(asteroidGroup);
                return default(JobHandle);
            }
            var settings = GetSingleton<ServerSettings>();
            var maxAsteroids = settings.numAsteroids;

            JobHandle gatherJob;
            var countJob = new CountJob
            {
                chunks = asteroidGroup.CreateArchetypeChunkArray(Allocator.TempJob, out gatherJob),
                count = count,
                entityType = GetArchetypeChunkEntityType()
            };
            inputDeps = countJob.Schedule(JobHandle.CombineDependencies(inputDeps, gatherJob));

            JobHandle levelHandle;
            var spawnJob = new SpawnJob
            {
                commandBuffer = barrier.CreateCommandBuffer(),
                count = count,
                targetCount = maxAsteroids,
                asteroidArchetype = settings.asteroidArchetype,
                asteroidRadius = settings.asteroidRadius,
                asteroidVelocity = settings.asteroidVelocity,
                level = m_LevelGroup.ToComponentDataArray<LevelComponent>(Allocator.TempJob, out levelHandle),
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
    public class PlayerSpawnSystem : JobComponentSystem
    {
        protected override void OnCreateManager()
        {
            barrier = World.GetOrCreateManager<BeginSimulationEntityCommandBufferSystem>();
            m_LevelGroup = GetComponentGroup(ComponentType.ReadWrite<LevelComponent>());
            RequireForUpdate(m_LevelGroup);
        }

        private BeginSimulationEntityCommandBufferSystem barrier;
        private ComponentGroup m_LevelGroup;

        struct SpawnJob : IJobProcessComponentDataWithEntity<PlayerSpawnRequest>
        {
            public EntityCommandBuffer commandBuffer;
            public ComponentDataFromEntity<PlayerStateComponentData> playerStateFromEntity;
            public ComponentDataFromEntity<NetworkIdComponent> networkIdFromEntity;
            public EntityArchetype shipArchetype;
            public float playerRadius;
            [ReadOnly] [DeallocateOnJobCompletion] public NativeArray<LevelComponent> level;

            public Unity.Mathematics.Random rand;

            public void Execute(Entity entity, int index, [ReadOnly] ref PlayerSpawnRequest request)
            {
                // Destroy the request
                commandBuffer.DestroyEntity(entity);
                if (!playerStateFromEntity.Exists(request.connection) ||
                    playerStateFromEntity[request.connection].PlayerShip != Entity.Null ||
                    playerStateFromEntity[request.connection].IsSpawning != 0)
                    return;
                var ship = commandBuffer.CreateEntity(shipArchetype);
                var padding = 2 * playerRadius;
                var pos = new Translation{Value = new float3(rand.NextFloat(padding, level[0].width-padding),
                    rand.NextFloat(padding, level[0].height-padding), 0)};
                //var pos = new PositionComponentData(GameSettings.mapWidth / 2, GameSettings.mapHeight / 2);
                var rot = new Rotation{Value = quaternion.RotateZ(math.radians(90f))};

                commandBuffer.SetComponent(ship, pos);
                commandBuffer.SetComponent(ship, rot);
                commandBuffer.SetComponent(ship, new PlayerIdComponentData {PlayerId = networkIdFromEntity[request.connection].Value, PlayerEntity = request.connection});

                commandBuffer.SetComponent(
                    ship, new CollisionSphereComponentData(playerRadius));

                commandBuffer.AddComponent(ship, new ShipSpawnInProgressTag());

                // Mark the player as currently spawning
                playerStateFromEntity[request.connection] = new PlayerStateComponentData { PlayerShip = Entity.Null, IsSpawning = 1};
            }
        }
        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var settings = GetSingleton<ServerSettings>();
            JobHandle levelHandle;
            var spawnJob = new SpawnJob
            {
                commandBuffer = barrier.CreateCommandBuffer(),
                playerStateFromEntity = GetComponentDataFromEntity<PlayerStateComponentData>(),
                networkIdFromEntity = GetComponentDataFromEntity<NetworkIdComponent>(),
                shipArchetype = settings.shipArchetype,
                playerRadius = settings.playerRadius,
                level = m_LevelGroup.ToComponentDataArray<LevelComponent>(Allocator.TempJob, out levelHandle),
                rand = new Unity.Mathematics.Random((uint)Stopwatch.GetTimestamp())
            };
            var handle = spawnJob.ScheduleSingle(this, JobHandle.CombineDependencies(inputDeps, levelHandle));
            barrier.AddJobHandleForProducer(handle);
            return handle;
        }
    }

    [UpdateInGroup(typeof(ServerSimulationSystemGroup))]
    [UpdateBefore(typeof(GhostSendSystem))]
    public class PlayerCompleteSpawnSystem : JobComponentSystem
    {
        protected override void OnCreateManager()
        {
            barrier = World.GetOrCreateManager<BeginSimulationEntityCommandBufferSystem>();
        }

        private BeginSimulationEntityCommandBufferSystem barrier;

        [RequireComponentTag(typeof(ShipSpawnInProgressTag))]
        struct SpawnJob : IJobProcessComponentDataWithEntity<PlayerIdComponentData>
        {
            public EntityCommandBuffer commandBuffer;
            public ComponentDataFromEntity<PlayerStateComponentData> playerStateFromEntity;
            public ComponentDataFromEntity<NetworkStreamConnection> connectionFromEntity;

            public void Execute(Entity entity, int index, [ReadOnly] ref PlayerIdComponentData player)
            {
                if (!playerStateFromEntity.Exists(player.PlayerEntity) || !connectionFromEntity[player.PlayerEntity].Value.IsCreated)
                {
                    // Player was disconnected during spawn, or other error
                    commandBuffer.DestroyEntity(entity);
                    return;
                }
                commandBuffer.RemoveComponent<ShipSpawnInProgressTag>(entity);
                playerStateFromEntity[player.PlayerEntity] = new PlayerStateComponentData {PlayerShip = entity, IsSpawning = 0};
            }
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var spawnJob = new SpawnJob
            {
                commandBuffer = barrier.CreateCommandBuffer(),
                playerStateFromEntity = GetComponentDataFromEntity<PlayerStateComponentData>(),
                connectionFromEntity = GetComponentDataFromEntity<NetworkStreamConnection>()
            };
            var handle = spawnJob.ScheduleSingle(this, inputDeps);
            barrier.AddJobHandleForProducer(handle);
            return handle;
        }
    }
}

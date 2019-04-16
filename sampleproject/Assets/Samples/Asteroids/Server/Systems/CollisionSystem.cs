using System;
using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Jobs;
using Unity.Transforms;

namespace Asteroids.Server
{
    [UpdateInGroup(typeof(ServerSimulationSystemGroup))]
    [UpdateAfter(typeof(AsteroidSystem))]
    [UpdateAfter(typeof(BulletSystem))]
    [UpdateAfter(typeof(BulletAgeSystem))]
    [UpdateAfter(typeof(SteeringSystem))]
    [UpdateBefore(typeof(GhostSendSystem))]
    public class CollisionSystem : JobComponentSystem
    {
        private ComponentGroup shipGroup;
        private ComponentGroup bulletGroup;
        private ComponentGroup asteroidGroup;
        private ComponentGroup m_LevelGroup;
        private BeginSimulationEntityCommandBufferSystem barrier;
        private NativeQueue<Entity> playerClearQueue;
        private ComponentGroup settingsGroup;

        protected override void OnCreateManager()
        {
            shipGroup = GetComponentGroup(ComponentType.ReadOnly<Translation>(),
                ComponentType.ReadOnly<CollisionSphereComponentData>(), ComponentType.ReadOnly<ShipTagComponentData>());
            bulletGroup = GetComponentGroup(ComponentType.ReadOnly<Translation>(),
                ComponentType.ReadOnly<CollisionSphereComponentData>(), ComponentType.ReadOnly<BulletTagComponentData>(),
                ComponentType.ReadOnly<BulletAgeComponentData>());
            asteroidGroup = GetComponentGroup(ComponentType.ReadOnly<Translation>(),
                ComponentType.ReadOnly<CollisionSphereComponentData>(), ComponentType.ReadOnly<AsteroidTagComponentData>());
            barrier = World.GetOrCreateManager<BeginSimulationEntityCommandBufferSystem>();
            playerClearQueue = new NativeQueue<Entity>(Allocator.Persistent);

            m_LevelGroup = GetComponentGroup(ComponentType.ReadWrite<LevelComponent>());
            RequireForUpdate(m_LevelGroup);

            settingsGroup = GetComponentGroup(ComponentType.ReadOnly<ServerSettings>());
        }

        protected override void OnDestroyManager()
        {
            playerClearQueue.Dispose();
        }

        [BurstCompile]
        struct DestroyAsteroidJob : IJobChunk
        {
            public EntityCommandBuffer.Concurrent commandBuffer;
            [ReadOnly] public NativeArray<ArchetypeChunk> bulletChunks;
            [ReadOnly] public ArchetypeChunkComponentType<BulletAgeComponentData> bulletAgeType;
            [ReadOnly] public ArchetypeChunkComponentType<Translation> positionType;
            [ReadOnly] public ArchetypeChunkComponentType<CollisionSphereComponentData> sphereType;
            [ReadOnly] public ArchetypeChunkEntityType entityType;

            [ReadOnly] public NativeArray<LevelComponent> level;
            public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
            {
                var asteroidPos = chunk.GetNativeArray(positionType);
                var asteroidSphere = chunk.GetNativeArray(sphereType);
                var asteroidEntity = chunk.GetNativeArray(entityType);
                for (int asteroid = 0; asteroid < asteroidPos.Length; ++asteroid)
                {
                    var firstPos = asteroidPos[asteroid].Value.xy;
                    var firstRadius = asteroidSphere[asteroid].radius;
                    if (firstPos.x - firstRadius < 0 || firstPos.y - firstRadius < 0 ||
                        firstPos.x + firstRadius > level[0].width ||
                        firstPos.y + firstRadius > level[0].height)
                    {
                        commandBuffer.DestroyEntity(chunkIndex, asteroidEntity[asteroid]);
                        continue;
                    }
                    // TODO: can check asteroid / asteroid here if required
                    for (int bc = 0; bc < bulletChunks.Length; ++bc)
                    {
                        var bulletAge = bulletChunks[bc].GetNativeArray(bulletAgeType);
                        var bulletPos = bulletChunks[bc].GetNativeArray(positionType);
                        var bulletSphere = bulletChunks[bc].GetNativeArray(sphereType);
                        for (int bullet = 0; bullet < bulletAge.Length; ++bullet)
                        {
                            if (bulletAge[bullet].age > bulletAge[bullet].maxAge)
                                continue;
                            var secondPos = bulletPos[bullet].Value.xy;
                            var secondRadius = bulletSphere[bullet].radius;
                            if (Intersect(firstRadius, secondRadius, firstPos, secondPos))
                            {
                                commandBuffer.DestroyEntity(chunkIndex, asteroidEntity[asteroid]);
                            }
                        }
                    }
                }
            }
        }
        [BurstCompile]
        struct DestroyShipJob : IJobChunk
        {
            public EntityCommandBuffer.Concurrent commandBuffer;
            [ReadOnly] public NativeArray<ArchetypeChunk> asteroidChunks;
            [ReadOnly] public NativeArray<ArchetypeChunk> bulletChunks;
            [ReadOnly] public ArchetypeChunkComponentType<BulletAgeComponentData> bulletAgeType;
            [ReadOnly] public ArchetypeChunkComponentType<Translation> positionType;
            [ReadOnly] public ArchetypeChunkComponentType<CollisionSphereComponentData> sphereType;
            [ReadOnly] public ArchetypeChunkComponentType<PlayerIdComponentData> playerIdType;
            [ReadOnly] public ArchetypeChunkEntityType entityType;

            [DeallocateOnJobCompletion] [ReadOnly] public NativeArray<ServerSettings> serverSettings;

            public NativeQueue<Entity>.Concurrent playerClearQueue;

            [ReadOnly] public NativeArray<LevelComponent> level;
            public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
            {
                var shipPos = chunk.GetNativeArray(positionType);
                var shipSphere = chunk.GetNativeArray(sphereType);
                var shipPlayerId = chunk.GetNativeArray(playerIdType);
                var shipEntity = chunk.GetNativeArray(entityType);
                for (int ship = 0; ship < shipPos.Length; ++ship)
                {
                    int alive = 1;
                    var firstPos = shipPos[ship].Value.xy;
                    var firstRadius = shipSphere[ship].radius;
                    if (firstPos.x - firstRadius < 0 || firstPos.y - firstRadius < 0 ||
                        firstPos.x + firstRadius > level[0].width ||
                        firstPos.y + firstRadius > level[0].height)
                    {
                        if (shipPlayerId.IsCreated)
                            playerClearQueue.Enqueue(shipPlayerId[ship].PlayerEntity);
                        commandBuffer.DestroyEntity(chunkIndex, shipEntity[ship]);
                        continue;
                    }

                    if (serverSettings.Length > 0 && serverSettings[0].damageShips == 0)
                        continue;
                    /*for (int bc = 0; bc < bulletChunks.Length && alive != 0; ++bc)
                    {
                        var bulletAge = bulletChunks[bc].GetNativeArray(bulletAgeType);
                        var bulletPos = bulletChunks[bc].GetNativeArray(positionType);
                        var bulletSphere = bulletChunks[bc].GetNativeArray(sphereType);
                        for (int bullet = 0; bullet < bulletAge.Length; ++bullet)
                        {
                            if (bulletAge[bullet].age > bulletAge[bullet].maxAge)
                                continue;
                            var secondPos = bulletPos[bullet].Value.xy;
                            var secondRadius = bulletSphere[bullet].radius;
                            if (Intersect(firstRadius, secondRadius, firstPos, secondPos))
                            {
                                if (shipPlayerId.IsCreated)
                                    playerClearQueue.Enqueue(shipPlayerId[ship].PlayerEntity);
                                commandBuffer.DestroyEntity(chunkIndex, shipEntity[ship]);
                                alive = 0;
                                break;
                            }
                        }
                    }*/
                    for (int ac = 0; ac < asteroidChunks.Length && alive != 0; ++ac)
                    {
                        var asteroidPos = asteroidChunks[ac].GetNativeArray(positionType);
                        var asteroidSphere = asteroidChunks[ac].GetNativeArray(sphereType);
                        for (int asteroid = 0; asteroid < asteroidPos.Length; ++asteroid)
                        {
                            var secondPos = asteroidPos[asteroid].Value.xy;
                            var secondRadius = asteroidSphere[asteroid].radius;
                            if (Intersect(firstRadius, secondRadius, firstPos, secondPos))
                            {
                                if (shipPlayerId.IsCreated)
                                    playerClearQueue.Enqueue(shipPlayerId[ship].PlayerEntity);
                                commandBuffer.DestroyEntity(chunkIndex, shipEntity[ship]);
                                alive = 0;
                                break;
                            }
                        }
                    }
                }
            }
        }

        struct ClearShipPointerJob : IJob
        {
            public NativeQueue<Entity> playerClearQueue;
            public ComponentDataFromEntity<PlayerStateComponentData> playerState;

            public void Execute()
            {
                Entity ent;
                while (playerClearQueue.TryDequeue(out ent))
                {
                    if (playerState.Exists(ent))
                    {
                        var state = playerState[ent];
                        state.PlayerShip = Entity.Null;
                        playerState[ent] = state;
                    }
                }
            }
        }

        struct ChunkCleanupJob : IJob
        {
            [ReadOnly] [DeallocateOnJobCompletion] public NativeArray<ArchetypeChunk> asteroidChunks;
            [ReadOnly] [DeallocateOnJobCompletion] public NativeArray<ArchetypeChunk> bulletChunks;
            [ReadOnly] [DeallocateOnJobCompletion] public NativeArray<LevelComponent> level;
            public void Execute()
            {
            }
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            JobHandle bulletHandle;
            JobHandle asteroidHandle;
            JobHandle levelHandle;
            JobHandle settingsHandle;

            var asteroidJob = new DestroyAsteroidJob
            {
                commandBuffer = barrier.CreateCommandBuffer().ToConcurrent(),
                bulletChunks = bulletGroup.CreateArchetypeChunkArray(Allocator.TempJob, out bulletHandle),
                bulletAgeType = GetArchetypeChunkComponentType<BulletAgeComponentData>(true),
                positionType = GetArchetypeChunkComponentType<Translation>(true),
                sphereType = GetArchetypeChunkComponentType<CollisionSphereComponentData>(true),
                entityType = GetArchetypeChunkEntityType(),
                level = m_LevelGroup.ToComponentDataArray<LevelComponent>(Allocator.TempJob, out levelHandle)
            };
            var shipJob = new DestroyShipJob
            {
                commandBuffer = barrier.CreateCommandBuffer().ToConcurrent(),
                asteroidChunks = asteroidGroup.CreateArchetypeChunkArray(Allocator.TempJob, out asteroidHandle),
                bulletChunks = asteroidJob.bulletChunks,
                bulletAgeType = asteroidJob.bulletAgeType,
                positionType = asteroidJob.positionType,
                sphereType = asteroidJob.sphereType,
                playerIdType = GetArchetypeChunkComponentType<PlayerIdComponentData>(),
                entityType = asteroidJob.entityType,
                serverSettings = settingsGroup.ToComponentDataArray<ServerSettings>(Allocator.TempJob, out settingsHandle),
                playerClearQueue = playerClearQueue.ToConcurrent(),
                level = asteroidJob.level
            };
            var asteroidDep = JobHandle.CombineDependencies(inputDeps, bulletHandle, levelHandle);
            var shipDep = JobHandle.CombineDependencies(asteroidDep, asteroidHandle, settingsHandle);

            var h1 = asteroidJob.Schedule(asteroidGroup, asteroidDep);
            var h2 = shipJob.Schedule(shipGroup, shipDep);

            var handle = JobHandle.CombineDependencies(h1, h2);
            barrier.AddJobHandleForProducer(handle);

            var cleanupShipJob = new ClearShipPointerJob
            {
                playerClearQueue = playerClearQueue,
                playerState = GetComponentDataFromEntity<PlayerStateComponentData>()
            };
            var cleanupChunkJob = new ChunkCleanupJob
            {
                bulletChunks = shipJob.bulletChunks,
                asteroidChunks = shipJob.asteroidChunks,
                level = shipJob.level
            };
            return JobHandle.CombineDependencies(cleanupShipJob.Schedule(h2), cleanupChunkJob.Schedule(handle));
        }

        private static bool Intersect(float firstRadius, float secondRadius, float2 firstPos, float2 secondPos)
        {
            float2 diff = firstPos - secondPos;
            float distSq = math.dot(diff, diff);

            return distSq <= (firstRadius + secondRadius) * (firstRadius + secondRadius);
        }
    }
}

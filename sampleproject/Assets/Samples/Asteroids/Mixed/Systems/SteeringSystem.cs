using Unity.Entities;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.NetCode;
using Unity.Networking.Transport.Utilities;

namespace Asteroids.Mixed
{
    [UpdateInGroup(typeof(GhostPredictionSystemGroup))]
    public class SteeringSystem : JobComponentSystem
    {
        private BeginSimulationEntityCommandBufferSystem barrier;
        private GhostPredictionSystemGroup predictionGroup;
        private Entity bulletPrefab;
        private EntityArchetype bulletSpawnArchetype;
        private uint lastSpawnTick;

        //[BurstCompile]
        [RequireComponentTag(typeof(ShipTagComponentData), typeof(ShipCommandData))]
        struct SteeringJob : IJobForEachWithEntity<Translation, Rotation, Velocity, ShipStateComponentData, PlayerIdComponentData, PredictedGhostComponent>
        {
            private const int k_CoolDownTicksCount = 10;
            public EntityCommandBuffer.Concurrent commandBuffer;
            public float deltaTime;
            public float displacement;
            public float playerForce;
            public float bulletVelocity;
            public Entity bulletPrefab;
            public EntityArchetype bulletSpawnArchetype;
            public uint currentTick;
            [ReadOnly] public BufferFromEntity<ShipCommandData> inputFromEntity;
            public void Execute(Entity entity, int index, ref Translation position, ref Rotation rotation, ref Velocity velocity,
                ref ShipStateComponentData state, [ReadOnly] ref PlayerIdComponentData playerIdData, [ReadOnly] ref PredictedGhostComponent prediction)
            {
                if (!GhostPredictionSystemGroup.ShouldPredict(currentTick, prediction))
                    return;
                var input = inputFromEntity[entity];
                ShipCommandData inputData;
                if (!input.GetDataAtTick(currentTick, out inputData))
                    inputData.shoot = 0;

                state.State = inputData.thrust;

                if (inputData.left == 1)
                {
                    rotation.Value = math.mul(rotation.Value, quaternion.RotateZ(math.radians(-displacement * deltaTime)));
                }

                if (inputData.right == 1)
                {
                    rotation.Value = math.mul(rotation.Value, quaternion.RotateZ(math.radians(displacement * deltaTime)));
                }

                if (inputData.thrust == 1)
                {
                    float3 fwd = new float3(0, playerForce * deltaTime, 0);
                    velocity.Value += math.mul(rotation.Value, fwd).xy;
                }

                position.Value.xy += velocity.Value * deltaTime;

                var canShoot = state.WeaponCooldown == 0 || SequenceHelpers.IsNewer(currentTick, state.WeaponCooldown);
                if (inputData.shoot != 0 && canShoot)
                {
                    if (bulletPrefab != Entity.Null)
                    {
                        var e = commandBuffer.Instantiate(index, bulletPrefab);

                        commandBuffer.SetComponent(index, e, position);
                        commandBuffer.SetComponent(index, e, rotation);

                        var vel = new Velocity
                            {Value = math.mul(rotation.Value, new float3(0, bulletVelocity, 0)).xy};

                        commandBuffer.SetComponent(index, e,
                            new PlayerIdComponentData {PlayerId = playerIdData.PlayerId});
                        commandBuffer.SetComponent(index, e, vel);
                    }
                    else
                    {
                        var e = commandBuffer.CreateEntity(index, bulletSpawnArchetype);
                        var bulletData = default(BulletSnapshotData);
                        bulletData.tick = currentTick;
                        bulletData.SetRotationValue(rotation.Value);
                        bulletData.SetTranslationValue(position.Value);
                        // Offset bullets for debugging spawn prediction
                        //bulletData.SetTranslationValue(position.Value + new float3(0,10,0));
                        bulletData.SetPlayerIdComponentDataPlayerId(playerIdData.PlayerId, default(GhostSerializerState));
                        var bulletSnapshots = commandBuffer.SetBuffer<BulletSnapshotData>(index, e);
                        bulletSnapshots.Add(bulletData);
                    }

                    state.WeaponCooldown = currentTick + k_CoolDownTicksCount;
                }
                else if (canShoot)
                {
                    state.WeaponCooldown = 0;
                }
            }
        }

        protected override void OnCreate()
        {
            barrier = World.GetOrCreateSystem<BeginSimulationEntityCommandBufferSystem>();
            predictionGroup = World.GetOrCreateSystem<GhostPredictionSystemGroup>();
            RequireSingletonForUpdate<LevelComponent>();
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            if (bulletPrefab == Entity.Null && bulletSpawnArchetype == default)
            {
                if (World.GetExistingSystem<ServerSimulationSystemGroup>() != null)
                {
                    var prefabs = GetSingleton<GhostPrefabCollectionComponent>();
                    var serverPrefabs = EntityManager.GetBuffer<GhostPrefabBuffer>(prefabs.serverPrefabs);
                    for (int i = 0; i < serverPrefabs.Length; ++i)
                    {
                        if (EntityManager.HasComponent<BulletTagComponent>(serverPrefabs[i].Value))
                            bulletPrefab = serverPrefabs[i].Value;
                    }
                }
                else
                {
                    bulletSpawnArchetype = EntityManager.CreateArchetype(
                        ComponentType.ReadWrite<PredictedGhostSpawnRequestComponent>(),
                        ComponentType.ReadWrite<BulletSnapshotData>());
                }
            }
            var level = GetSingleton<LevelComponent>();
            var steerJob = new SteeringJob
            {
                commandBuffer = barrier.CreateCommandBuffer().ToConcurrent(),
                deltaTime = Time.DeltaTime,
                displacement = 100.0f,
                playerForce = level.playerForce,
                bulletVelocity = level.bulletVelocity,
                bulletPrefab = bulletPrefab,
                bulletSpawnArchetype = bulletSpawnArchetype,
                currentTick = predictionGroup.PredictingTick,
                inputFromEntity = GetBufferFromEntity<ShipCommandData>(true)
            };
            var handle = steerJob.Schedule(this, inputDeps);
            barrier.AddJobHandleForProducer(handle);
            return handle;
        }
    }
}


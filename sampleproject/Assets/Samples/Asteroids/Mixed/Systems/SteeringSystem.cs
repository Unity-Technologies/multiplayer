using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.NetCode;
using Unity.Networking.Transport.Utilities;

namespace Asteroids.Mixed
{
    [UpdateInGroup(typeof(GhostPredictionSystemGroup))]
    public class SteeringSystem : SystemBase
    {
        private const int k_CoolDownTicksCount = 10;

        private BeginSimulationEntityCommandBufferSystem m_Barrier;
        private GhostPredictionSystemGroup m_PredictionGroup;
        private Entity m_BulletPrefab;
        private EntityArchetype m_BulletSpawnArchetype;

        protected override void OnCreate()
        {
            m_Barrier = World.GetOrCreateSystem<BeginSimulationEntityCommandBufferSystem>();
            m_PredictionGroup = World.GetOrCreateSystem<GhostPredictionSystemGroup>();
            RequireSingletonForUpdate<LevelComponent>();
        }

        protected override void OnUpdate()
        {
            if (m_BulletPrefab == Entity.Null && m_BulletSpawnArchetype == default)
            {
                if (World.GetExistingSystem<ServerSimulationSystemGroup>() != null)
                {
                    var prefabs = GetSingleton<GhostPrefabCollectionComponent>();
                    var serverPrefabs = EntityManager.GetBuffer<GhostPrefabBuffer>(prefabs.serverPrefabs);
                    for (int i = 0; i < serverPrefabs.Length; ++i)
                    {
                        if (EntityManager.HasComponent<BulletTagComponent>(serverPrefabs[i].Value))
                            m_BulletPrefab = serverPrefabs[i].Value;
                    }
                }
                else
                {
                    m_BulletSpawnArchetype = EntityManager.CreateArchetype(
                        ComponentType.ReadWrite<PredictedGhostSpawnRequestComponent>(),
                        ComponentType.ReadWrite<BulletSnapshotData>());
                }
            }

            var level = GetSingleton<LevelComponent>();
            var commandBuffer = m_Barrier.CreateCommandBuffer().ToConcurrent();
            var deltaTime = Time.DeltaTime;
            var displacement = 100.0f;
            var playerForce = level.playerForce;
            var bulletVelocity = level.bulletVelocity;
            var bulletPrefab = m_BulletPrefab;
            var bulletSpawnArchetype = m_BulletSpawnArchetype;
            var currentTick = m_PredictionGroup.PredictingTick;
            var inputFromEntity = GetBufferFromEntity<ShipCommandData>(true);
            Entities.WithReadOnly(inputFromEntity).WithAll<ShipTagComponentData, ShipCommandData>().
                ForEach((Entity entity, int nativeThreadIndex, ref Translation position, ref Rotation rotation,
                ref Velocity velocity, ref ShipStateComponentData state,
                in PlayerIdComponentData playerIdData, in PredictedGhostComponent prediction) =>
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
                    rotation.Value = math.mul(rotation.Value,
                        quaternion.RotateZ(math.radians(-displacement * deltaTime)));
                }

                if (inputData.right == 1)
                {
                    rotation.Value = math.mul(rotation.Value,
                        quaternion.RotateZ(math.radians(displacement * deltaTime)));
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
                        var e = commandBuffer.Instantiate(nativeThreadIndex, bulletPrefab);

                        commandBuffer.SetComponent(nativeThreadIndex, e, position);
                        commandBuffer.SetComponent(nativeThreadIndex, e, rotation);

                        var vel = new Velocity
                            {Value = math.mul(rotation.Value, new float3(0, bulletVelocity, 0)).xy};

                        commandBuffer.SetComponent(nativeThreadIndex, e,
                            new PlayerIdComponentData {PlayerId = playerIdData.PlayerId});
                        commandBuffer.SetComponent(nativeThreadIndex, e, vel);
                    }
                    else
                    {
                        var e = commandBuffer.CreateEntity(nativeThreadIndex, bulletSpawnArchetype);
                        var bulletData = default(BulletSnapshotData);
                        bulletData.tick = currentTick;
                        bulletData.SetRotationValue(rotation.Value);
                        bulletData.SetTranslationValue(position.Value);
                        // Offset bullets for debugging spawn prediction
                        //bulletData.SetTranslationValue(position.Value + new float3(0,10,0));
                        bulletData.SetPlayerIdComponentDataPlayerId(playerIdData.PlayerId,
                            default(GhostSerializerState));
                        var bulletSnapshots = commandBuffer.SetBuffer<BulletSnapshotData>(nativeThreadIndex, e);
                        bulletSnapshots.Add(bulletData);
                    }

                    state.WeaponCooldown = currentTick + k_CoolDownTicksCount;
                }
                else if (canShoot)
                {
                    state.WeaponCooldown = 0;
                }
            }).ScheduleParallel();
            m_Barrier.AddJobHandleForProducer(Dependency);
        }
    }
}


    using Asteroids.Client;
    using Unity.Collections;
    using Unity.Entities;
    using Unity.Jobs;
    using Unity.Mathematics;
    using Unity.Networking.Transport.Utilities;
    using Unity.Transforms;

    [UpdateAfter(typeof(InputSystem))]
    [UpdateAfter(typeof(GhostReceiveSystemGroup))]
    [UpdateBefore(typeof(AfterSimulationInterpolationSystem))]
    [UpdateInGroup(typeof(ClientSimulationSystemGroup))]
    public class SteeringPredictionSystem : JobComponentSystem
    {
        private BeginSimulationEntityCommandBufferSystem barrier;
        private EntityArchetype bulletSpawnArchetype;
        private NativeArray<uint> lastPredictedSpawn;
        private NetworkTimeSystem m_NetworkTimeSystem;

        //[BurstCompile]
        [RequireComponentTag(typeof(ShipTagComponentData), typeof(ShipCommandData), typeof(PredictedEntityComponent))]
        struct SteeringJob : IJobForEachWithEntity<Translation, Rotation, Velocity, ShipStateComponentData, PlayerIdComponentData>
        {
            private const int k_CoolDownTicksCount = 10;

            public float deltaTime;
            public float displacement;
            public float playerForce;
            public uint currentTick;
            public NativeArray<uint> lastPredictedSpawn;

            public EntityCommandBuffer.Concurrent commandBuffer;
            public EntityArchetype bulletSpawnArchetype;
            [ReadOnly] public BufferFromEntity<ShipSnapshotData> snapshotFromEntity;
            [ReadOnly] public BufferFromEntity<ShipCommandData> inputFromEntity;

            public unsafe void Execute(Entity entity, int index, ref Translation position, ref Rotation rotation, ref Velocity velocity,
                ref ShipStateComponentData state, [ReadOnly] ref PlayerIdComponentData playerIdData)
            {
                var snapshot = snapshotFromEntity[entity];
                ShipSnapshotData snapshotData;
                snapshot.GetDataAtTick(currentTick, out snapshotData);

                var input = inputFromEntity[entity];
                // Iterate over last snapshot tick + 1 (we just applied the first one)
                // to the current tick + 1 and apply prediction
                for (uint tick = snapshotData.Tick+1; tick != currentTick + 1; ++tick)
                {
                    // Get input at the tick we're predicting
                    ShipCommandData inputData;
                    input.GetDataAtTick(tick, out inputData);

                    // For movement, apply the input so position/rotation will be adjusted
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

                    if (inputData.shoot != 0)
                    {
                        // Only spawn a bullet if this is a tick we have not simulated before
                        if (lastPredictedSpawn[0] == 0 || SequenceHelpers.IsNewer(tick, lastPredictedSpawn[0]+k_CoolDownTicksCount))
                        {
                            var e = commandBuffer.CreateEntity(index, bulletSpawnArchetype);
                            var bulletData = default(BulletSnapshotData);
                            bulletData.tick = tick;
                            bulletData.SetRotationValue(rotation.Value);
                            bulletData.SetTranslationValue(position.Value);
                            // Offset bullets for debugging spawn prediction
                            //bulletData.SetTranslationValue(position.Value + new float3(0,10,0));
                            bulletData.SetPlayerIdComponentDataPlayerId(playerIdData.PlayerId);
                            var bulletSnapshots = commandBuffer.SetBuffer<BulletSnapshotData>(index, e);
                            bulletSnapshots.Add(bulletData);
                            lastPredictedSpawn[0] = tick;
                        }
                    }
                }
            }
        }

        protected override void OnCreateManager()
        {
            barrier = World.GetOrCreateSystem<BeginSimulationEntityCommandBufferSystem>();
            bulletSpawnArchetype = EntityManager.CreateArchetype(
                ComponentType.ReadWrite<PredictedSpawnRequestComponent>(),
                ComponentType.ReadWrite<BulletSnapshotData>());
            lastPredictedSpawn = new NativeArray<uint>(1, Allocator.Persistent);
            m_NetworkTimeSystem = World.GetOrCreateSystem<NetworkTimeSystem>();
            RequireSingletonForUpdate<ClientSettings>();
        }

        protected override void OnDestroyManager()
        {
            lastPredictedSpawn.Dispose();
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var topGroup = World.GetExistingSystem<ClientSimulationSystemGroup>();
            var settings = GetSingleton<ClientSettings>();
            var steerJob = new SteeringJob
            {
                deltaTime = topGroup.UpdateDeltaTime,
                displacement = 100.0f,
                playerForce = settings.playerForce,
                currentTick = m_NetworkTimeSystem.predictTargetTick,
                lastPredictedSpawn = lastPredictedSpawn,
                bulletSpawnArchetype = bulletSpawnArchetype,
                commandBuffer = barrier.CreateCommandBuffer().ToConcurrent(),
                snapshotFromEntity = GetBufferFromEntity<ShipSnapshotData>(true),
                inputFromEntity = GetBufferFromEntity<ShipCommandData>(true)
            };
            var handle = steerJob.ScheduleSingle(this, inputDeps);
            barrier.AddJobHandleForProducer(handle);
            return handle;
        }
    }

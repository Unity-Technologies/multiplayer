using Unity.Entities;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace Asteroids.Server
{
    [UpdateAfter(typeof(AsteroidsCommandReceiveSystem))]
    [UpdateInGroup(typeof(ServerSimulationSystemGroup))]
    public class SteeringSystem : JobComponentSystem
    {
        private BeginSimulationEntityCommandBufferSystem barrier;

        //[BurstCompile]
        [RequireComponentTag(typeof(ShipTagComponentData), typeof(ShipCommandData))]
        struct SteeringJob : IJobForEachWithEntity<Translation, Rotation, Velocity, ShipStateComponentData, PlayerIdComponentData>
        {
            private const int k_CoolDownTicksCount = 10;
            public EntityCommandBuffer.Concurrent commandBuffer;
            public float deltaTime;
            public float displacement;
            public float playerForce;
            public EntityArchetype bulletArchetype;
            public float bulletVelocity;
            public float bulletRadius;
            public uint currentTick;
            [ReadOnly] public BufferFromEntity<ShipCommandData> inputFromEntity;
            public unsafe void Execute(Entity entity, int index, ref Translation position, ref Rotation rotation, ref Velocity velocity,
                ref ShipStateComponentData state, [ReadOnly] ref PlayerIdComponentData playerIdData)
            {
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

                if (state.WeaponCooldown > 0)
                    --state.WeaponCooldown;
                if (inputData.shoot != 0 && state.WeaponCooldown == 0)
                {
                    var e = commandBuffer.CreateEntity(index, bulletArchetype);

                    commandBuffer.SetComponent(index, e, position);
                    commandBuffer.SetComponent(index, e, rotation);

                    var vel = new Velocity
                        {Value = math.mul(rotation.Value, new float3(0, bulletVelocity, 0)).xy};

                    commandBuffer.SetComponent(index, e, new BulletAgeComponentData(1.5f));
                    commandBuffer.SetComponent(index, e, new PlayerIdComponentData(){ PlayerId = playerIdData.PlayerId });
                    commandBuffer.SetComponent(index, e, vel);
                    commandBuffer.SetComponent(index, e,
                        new CollisionSphereComponentData(bulletRadius));

                    state.WeaponCooldown = k_CoolDownTicksCount;
                }
            }
        }

        private ServerSimulationSystemGroup serverSimulationSystemGroup;
        protected override void OnCreateManager()
        {
            barrier = World.GetOrCreateSystem<BeginSimulationEntityCommandBufferSystem>();
            serverSimulationSystemGroup = World.GetOrCreateSystem<ServerSimulationSystemGroup>();
            RequireSingletonForUpdate<ServerSettings>();
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var topGroup = World.GetExistingSystem<ServerSimulationSystemGroup>();
            var settings = GetSingleton<ServerSettings>();
            var steerJob = new SteeringJob
            {
                commandBuffer = barrier.CreateCommandBuffer().ToConcurrent(),
                deltaTime = topGroup.UpdateDeltaTime,
                displacement = 100.0f,
                playerForce = settings.playerForce,
                bulletArchetype = settings.bulletArchetype,
                bulletVelocity = settings.bulletVelocity,
                bulletRadius = settings.bulletRadius,
                currentTick = serverSimulationSystemGroup.ServerTick,
                inputFromEntity = GetBufferFromEntity<ShipCommandData>(true)
            };
            var handle = steerJob.Schedule(this, inputDeps);
            barrier.AddJobHandleForProducer(handle);
            return handle;
        }
    }
}

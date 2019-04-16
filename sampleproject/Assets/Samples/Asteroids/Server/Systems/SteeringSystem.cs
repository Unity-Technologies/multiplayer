using Asteroids.Client;
using Unity.Burst;
using UnityEngine;
using Unity.Entities;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Networking.Transport.Utilities;
using Unity.Transforms;

namespace Asteroids.Server
{
    [UpdateAfter(typeof(InputCommandSystem))]
    [UpdateInGroup(typeof(ServerSimulationSystemGroup))]
    public class SteeringSystem : JobComponentSystem
    {
        private BeginSimulationEntityCommandBufferSystem barrier;
        //[BurstCompile]
        [RequireComponentTag(typeof(ShipTagComponentData))]
        struct SteeringJob : IJobProcessComponentDataWithEntity<Translation, Rotation, Velocity, ShipStateComponentData,
            PlayerInputComponentData>
        {
            public EntityCommandBuffer.Concurrent commandBuffer;
            public float deltaTime;
            public float displacement;
            public float playerForce;
            public EntityArchetype bulletArchetype;
            public float bulletVelocity;
            public float bulletRadius;
            public uint currentTick;
            public unsafe void Execute(Entity entity, int index, ref Translation position, ref Rotation rotation, ref Velocity velocity,
                ref ShipStateComponentData state, [ReadOnly] ref PlayerInputComponentData input)
            {
                int idx = 0;
                int closestIdxBefore = 0;
                uint closestTickBefore = 0;
                while (idx < 32)
                {
                    if (input.tick[idx] == currentTick)
                        break;
                    if (input.tick[idx] != 0 && SequenceHelpers.IsNewer(currentTick, input.tick[idx]))
                    {
                        if (closestTickBefore == 0 || SequenceHelpers.IsNewer(input.tick[idx], closestTickBefore))
                        {
                            closestTickBefore = input.tick[idx];
                            closestIdxBefore = idx;
                        }
                    }
                    ++idx;
                }

                byte left = 0;
                byte right = 0;
                byte thrust = 0;
                byte shoot = 0;
                // Only trigger shots on proper input, not repeats
                if (idx < 32)
                    shoot = input.shoot[idx];
                if (idx >= 32 && closestTickBefore != 0)
                    idx = closestIdxBefore;
                if (idx < 32)
                {
                    left = input.left[idx];
                    right = input.right[idx];
                    thrust = input.thrust[idx];
                }
                
                state.State = thrust;

                if (left == 1)
                {
                    rotation.Value = math.mul(rotation.Value, quaternion.RotateZ(math.radians(-displacement * deltaTime)));
                }

                if (right == 1)
                {
                    rotation.Value = math.mul(rotation.Value, quaternion.RotateZ(math.radians(displacement * deltaTime)));
                }

                if (thrust == 1)
                {
                    float3 fwd = new float3(0, playerForce * deltaTime, 0);
                    velocity.Value += math.mul(rotation.Value, fwd);
                }

                position.Value += velocity.Value * deltaTime;

                if (shoot != 0)
                {
                    var e = commandBuffer.CreateEntity(index, bulletArchetype);

                    commandBuffer.SetComponent(index, e, position);
                    commandBuffer.SetComponent(index, e, rotation);

                    var vel = new Velocity
                        {Value = math.mul(rotation.Value, new float3(0, bulletVelocity, 0))};

                    commandBuffer.SetComponent(index, e, new BulletAgeComponentData(1.5f));
                    commandBuffer.SetComponent(index, e, vel);
                    commandBuffer.SetComponent(index, e,
                        new CollisionSphereComponentData(bulletRadius));
                }
            }
        }

        private ServerSimulationSystemGroup serverSimulationSystemGroup;
        protected override void OnCreateManager()
        {
            barrier = World.GetOrCreateManager<BeginSimulationEntityCommandBufferSystem>();
            serverSimulationSystemGroup = World.GetOrCreateManager<ServerSimulationSystemGroup>();
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var settings = GetSingleton<ServerSettings>();
            var steerJob = new SteeringJob
            {
                commandBuffer = barrier.CreateCommandBuffer().ToConcurrent(),
                deltaTime = Time.deltaTime,
                displacement = 100.0f,
                playerForce = settings.playerForce,
                bulletArchetype = settings.bulletArchetype,
                bulletVelocity = settings.bulletVelocity,
                bulletRadius = settings.bulletRadius,
                currentTick = serverSimulationSystemGroup.ServerTick
            };
            var handle = steerJob.Schedule(this, inputDeps);
            barrier.AddJobHandleForProducer(handle);
            return handle;
        }
    }
}

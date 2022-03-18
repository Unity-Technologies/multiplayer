using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Random = UnityEngine.Random;
using Unity.NetCode;
using Unity.Rendering;

namespace Asteroids.Client
{
    [UpdateBefore(typeof(ParticleUpdateSystemGroup))]
    [UpdateInGroup(typeof(ClientSimulationSystemGroup))]
    public partial class ParticleEmitterSystem : SystemBase
    {
        private BeginSimulationEntityCommandBufferSystem m_Barrier;
        private EntityQuery m_Query;

        protected override void OnCreate()
        {
            m_Barrier = World.GetOrCreateSystem<BeginSimulationEntityCommandBufferSystem>();
            RequireForUpdate(m_Query);
        }

        protected override void OnUpdate()
        {
            var commandBuffer = m_Barrier.CreateCommandBuffer().AsParallelWriter();
            var deltaTime = Time.DeltaTime;
            Entities
                .WithStoreEntityQueryInField(ref m_Query)
                .WithNone<Particle>()
                .ForEach((Entity entity, int nativeThreadIndex, in ParticleEmitterComponentData emitter, in Translation position,
                in Rotation rotation) =>
            {
                if (emitter.active == 0)
                    return;
                int particles = (int) (deltaTime * emitter.particlesPerSecond + 0.5f);
                if (particles == 0)
                    return;
                float2 spawnOffset =
                    math.mul(rotation.Value, new float3(emitter.spawnOffset, 0)).xy;

                bool colorTrans = math.any(emitter.startColor != emitter.endColor);
                bool sizeTrans = emitter.startLength != emitter.endLength ||
                                 emitter.startWidth != emitter.endWidth;
                // Create the first particle, then instantiate the rest based on its value
                var particle = commandBuffer.Instantiate(nativeThreadIndex, emitter.particlePrefab);
                commandBuffer.AddComponent(nativeThreadIndex, particle, default(Particle));
                commandBuffer.AddComponent(nativeThreadIndex, particle, new URPMaterialPropertyBaseColor {Value = emitter.startColor});
                commandBuffer.AddComponent(nativeThreadIndex, particle, new ParticleAge(emitter.particleLifetime));
                commandBuffer.AddComponent(nativeThreadIndex, particle, emitter);
                // Set initial data
                commandBuffer.AddComponent(nativeThreadIndex, particle, new ParticleVelocity());
                commandBuffer.SetComponent(nativeThreadIndex, particle,
                    new Translation {Value = position.Value + new float3(spawnOffset, 0)});
                commandBuffer.SetComponent(nativeThreadIndex, particle, new Rotation {Value = rotation.Value});
                commandBuffer.SetComponent(nativeThreadIndex, particle, new NonUniformScale {Value = new float3(emitter.startWidth, emitter.startWidth + emitter.startLength, emitter.startWidth)});
                if (colorTrans)
                    commandBuffer.AddComponent(nativeThreadIndex, particle,
                        new ParticleColorTransition(emitter.startColor,
                            emitter.endColor));
                if (sizeTrans)
                    commandBuffer.AddComponent(nativeThreadIndex, particle,
                        new ParticleSizeTransition(emitter.startLength,
                            emitter.endLength, emitter.startWidth,
                            emitter.endWidth));
                if (particles > 1)
                {
                    for (int i = 1; i < particles; ++i)
                        commandBuffer.Instantiate(nativeThreadIndex, particle);
                }
            }).ScheduleParallel();
            m_Barrier.AddJobHandleForProducer(Dependency);
        }
    }

    [UpdateBefore(typeof(ParticleUpdateSystemGroup))]
    [UpdateAfter(typeof(ParticleEmitterSystem))]
    [UpdateInGroup(typeof(ClientSimulationSystemGroup))]
    public partial class ParticleInitializeSystem : SystemBase
    {
        private BeginSimulationEntityCommandBufferSystem m_Barrier;
        private EntityQuery m_Query;

        protected override void OnCreate()
        {
            m_Barrier = World.GetOrCreateSystem<BeginSimulationEntityCommandBufferSystem>();
            RequireForUpdate(m_Query);
        }

        protected override void OnUpdate()
        {
            var commandBuffer = m_Barrier.CreateCommandBuffer().AsParallelWriter();
            var emitterComponentType = ComponentType.ReadOnly<ParticleEmitterComponentData>();

            var rand = new Unity.Mathematics.Random((uint)System.Diagnostics.Stopwatch.GetTimestamp());
            Entities
                .WithStoreEntityQueryInField(ref m_Query)
                .WithAll<Particle>()
                .ForEach((Entity entity, int nativeThreadIndex,
                ref Translation position, ref Rotation rotation, ref ParticleVelocity velocity,
                in ParticleEmitterComponentData emitter) =>
            {
                var curRand = new Unity.Mathematics.Random(rand.NextUInt() + (uint)nativeThreadIndex);
                rotation.Value = math.mul(rotation.Value, quaternion.RotateZ(math.radians(curRand.NextFloat(-emitter.angleSpread,
                    emitter.angleSpread))));
                float particleVelocity = emitter.velocityBase +
                                         curRand.NextFloat(0, emitter.velocityRandom);
                float3 particleDir = new float3(0, particleVelocity, 0);
                velocity.velocity += math.mul(rotation.Value, particleDir).xy;

                position.Value.x += curRand.NextFloat(-emitter.spawnSpread, emitter.spawnSpread);
                position.Value.y += curRand.NextFloat(-emitter.spawnSpread, emitter.spawnSpread);
                commandBuffer.RemoveComponent(nativeThreadIndex, entity, emitterComponentType);
            }).ScheduleParallel();
            m_Barrier.AddJobHandleForProducer(Dependency);
        }
    }
}

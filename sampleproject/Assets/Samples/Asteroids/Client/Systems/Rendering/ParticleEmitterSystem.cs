using Unity.Collections;
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Jobs;
using Unity.Burst;
using Unity.Transforms;
using Random = UnityEngine.Random;

namespace Asteroids.Client
{
    struct ParticleSpawnCountComponent : IComponentData
    {
        public int spawnCount;
    }

    [UpdateBefore(typeof(ParticleUpdateSystemGroup))]
    [UpdateAfter(typeof(RenderInterpolationSystem))]
    [UpdateInGroup(typeof(ClientPresentationSystemGroup))]
    public class ParticleEmitterSystem : JobComponentSystem
    {
        private BeginPresentationEntityCommandBufferSystem barrier;

        private NativeQueue<int> spawnCountQueue;

        //[BurstCompile]
        [ExcludeComponent(typeof(ParticleComponentData))]
        struct ParticleSpawnJob : IJobProcessComponentDataWithEntity<ParticleEmitterComponentData, Translation, Rotation>
        {
            public NativeQueue<int>.Concurrent spawnCountQueue;
            public EntityCommandBuffer.Concurrent commandBuffer;
            public float deltaTime;
            public EntityArchetype m_ColorSizeParticleArchetype;
            public EntityArchetype m_ColorParticleArchetype;
            public EntityArchetype m_SizeParticleArchetype;
            public EntityArchetype m_ParticleArchetype;
            public void Execute(Entity entity, int index, [ReadOnly] ref ParticleEmitterComponentData emitter, [ReadOnly] ref Translation position,
                [ReadOnly] ref Rotation rotation)
            {
                if (emitter.active == 0)
                    return;
                int particles = (int) (deltaTime * emitter.particlesPerSecond + 0.5f);
                if (particles == 0)
                    return;
                spawnCountQueue.Enqueue(particles);
                float2 spawnOffset =
                    math.mul(rotation.Value, new float3(emitter.spawnOffset, 0)).xy;

                bool colorTrans = math.any(emitter.startColor != emitter.endColor);
                bool sizeTrans = emitter.startLength != emitter.endLength ||
                                 emitter.startWidth != emitter.endWidth;
                EntityArchetype particleArchetype;
                if (colorTrans && sizeTrans)
                    particleArchetype = m_ColorSizeParticleArchetype;
                else if (colorTrans)
                    particleArchetype = m_ColorParticleArchetype;
                else if (sizeTrans)
                    particleArchetype = m_SizeParticleArchetype;
                else
                    particleArchetype = m_ParticleArchetype;
                // Create the first particle, then instantiate the rest based on its value
                var particle = commandBuffer.CreateEntity(index, particleArchetype);
                commandBuffer.SetComponent(index, particle, emitter);
                // Set initial data
                commandBuffer.SetComponent(index, particle, new ParticleVelocityComponentData());
                commandBuffer.SetComponent(index, particle,
                    new Translation {Value = position.Value + new float3(spawnOffset, 0)});
                commandBuffer.SetComponent(index, particle, new Rotation {Value = rotation.Value});
                if (colorTrans)
                    commandBuffer.SetComponent(index, particle,
                        new ParticleColorTransitionComponentData(emitter.startColor,
                            emitter.endColor));
                if (sizeTrans)
                    commandBuffer.SetComponent(index, particle,
                        new ParticleSizeTransitionComponentData(emitter.startLength,
                            emitter.endLength, emitter.startWidth,
                            emitter.endWidth));
                if (particles > 1)
                {
                    for (int i = 1; i < particles; ++i)
                        commandBuffer.Instantiate(index, particle);
                    //particleEntities.ResizeUninitialized(particles - 1);
                    //NativeArray<Entity> temp = particleEntities;
                    //EntityManager.Instantiate(particle, temp);
                }
            }
        }

        EntityArchetype m_ColorSizeParticleArchetype;
        EntityArchetype m_ColorParticleArchetype;
        EntityArchetype m_SizeParticleArchetype;
        EntityArchetype m_ParticleArchetype;

        protected override void OnCreateManager()
        {
            m_ColorSizeParticleArchetype = EntityManager.CreateArchetype(typeof(ParticleEmitterComponentData),
                typeof(ParticleComponentData),
                typeof(ParticleAgeComponentData), typeof(ParticleVelocityComponentData),
                typeof(Translation), typeof(Rotation),
                typeof(ParticleColorTransitionComponentData), typeof(ParticleSizeTransitionComponentData));
            m_ColorParticleArchetype = EntityManager.CreateArchetype(typeof(ParticleEmitterComponentData),
                typeof(ParticleComponentData),
                typeof(ParticleAgeComponentData), typeof(ParticleVelocityComponentData),
                typeof(Translation), typeof(Rotation),
                typeof(ParticleColorTransitionComponentData));
            m_SizeParticleArchetype = EntityManager.CreateArchetype(typeof(ParticleEmitterComponentData),
                typeof(ParticleComponentData),
                typeof(ParticleAgeComponentData), typeof(ParticleVelocityComponentData),
                typeof(Translation), typeof(Rotation),
                typeof(ParticleSizeTransitionComponentData));
            m_ParticleArchetype = EntityManager.CreateArchetype(typeof(ParticleEmitterComponentData),
                typeof(ParticleComponentData),
                typeof(ParticleAgeComponentData), typeof(ParticleVelocityComponentData),
                typeof(Translation), typeof(Rotation));

            barrier = World.GetOrCreateManager<BeginPresentationEntityCommandBufferSystem>();
            spawnCountQueue = new NativeQueue<int>(Allocator.Persistent);

            EntityManager.CreateEntity(typeof(ParticleSpawnCountComponent));
        }

        protected override void OnDestroyManager()
        {
            spawnCountQueue.Dispose();
        }

        protected override JobHandle OnUpdate(JobHandle inputDep)
        {
            int spawnCount = 0;
            int cnt;
            while (spawnCountQueue.TryDequeue(out cnt))
                spawnCount += cnt;
            SetSingleton(new ParticleSpawnCountComponent {spawnCount = spawnCount});
            var commandBuffer = barrier.CreateCommandBuffer().ToConcurrent();
            var spawnJob = new ParticleSpawnJob();
            spawnJob.spawnCountQueue = spawnCountQueue.ToConcurrent();
            spawnJob.commandBuffer = commandBuffer;
            spawnJob.deltaTime = Time.deltaTime;
            spawnJob.m_ColorSizeParticleArchetype = m_ColorSizeParticleArchetype;
            spawnJob.m_ColorParticleArchetype = m_ColorParticleArchetype;
            spawnJob.m_SizeParticleArchetype = m_SizeParticleArchetype;
            spawnJob.m_ParticleArchetype = m_ParticleArchetype;
            inputDep = spawnJob.Schedule(this, inputDep);

            barrier.AddJobHandleForProducer(inputDep);
            return inputDep;
        }
    }

    [UpdateBefore(typeof(ParticleUpdateSystemGroup))]
    [UpdateAfter(typeof(ParticleEmitterSystem))]
    [UpdateInGroup(typeof(ClientPresentationSystemGroup))]
    public class ParticleInitializeSystem : JobComponentSystem
    {
        private BeginPresentationEntityCommandBufferSystem barrier;

        [BurstCompile]
        struct ParticleInitializeJob : IJobProcessComponentDataWithEntity<ParticleComponentData,
            Translation, Rotation, ParticleVelocityComponentData, ParticleAgeComponentData,
            ParticleEmitterComponentData>
        {
            public ComponentType emitterComponentType;
            public EntityCommandBuffer.Concurrent commandBuffer;
            [ReadOnly] public NativeArray<float> randomData;
            public int randomBase;
            private int curRandom;

            float RandomRange(float minVal, float maxVal)
            {
                float rnd = randomData[curRandom % randomData.Length];
                curRandom = (curRandom + 1) % randomData.Length;
                return rnd * (maxVal - minVal) + minVal;
            }

            public void Execute(Entity entity, int index, ref ParticleComponentData particle,
                ref Translation position, ref Rotation rotation, ref ParticleVelocityComponentData velocity,
                ref ParticleAgeComponentData age, [ReadOnly] ref ParticleEmitterComponentData emitter)
            {
                curRandom = randomBase + index;
                //float2 spawnOffset = RotationComponentData.rotate(spawners[em].emitter.spawnOffset, spawners[em].rotation);
                rotation.Value = math.mul(rotation.Value, quaternion.RotateZ(math.radians(RandomRange(-emitter.angleSpread,
                                        emitter.angleSpread))));
                float particleVelocity = emitter.velocityBase +
                                         RandomRange(0, emitter.velocityRandom);
                float3 particleDir = new float3(0, particleVelocity, 0);
                velocity.velocity += math.mul(rotation.Value, particleDir).xy;

                particle = new ParticleComponentData(emitter.startLength,
                    emitter.startWidth, emitter.startColor);
                age = new ParticleAgeComponentData(emitter.particleLifetime);
                position.Value.x += RandomRange(-emitter.spawnSpread, emitter.spawnSpread);
                position.Value.y += RandomRange(-emitter.spawnSpread, emitter.spawnSpread);
                commandBuffer.RemoveComponent(index, entity, emitterComponentType);
            }
        }

        protected override void OnCreateManager()
        {
            randomData = new NativeArray<float>(10 * 1024, Allocator.Persistent);
            for (int i = 0; i < randomData.Length; ++i)
                randomData[i] = Random.Range(0.0f, 1.0f);
            randomDataBase = 0;

            barrier = World.GetOrCreateManager<BeginPresentationEntityCommandBufferSystem>();
        }

        NativeArray<float> randomData;
        int randomDataBase;

        protected override void OnDestroyManager()
        {
            randomData.Dispose();
        }

        protected override JobHandle OnUpdate(JobHandle inputDep)
        {
            var commandBuffer = barrier.CreateCommandBuffer().ToConcurrent();
            var initJob = new ParticleInitializeJob();
            initJob.emitterComponentType = ComponentType.ReadWrite<ParticleEmitterComponentData>();
            initJob.commandBuffer = commandBuffer;
            initJob.randomData = randomData;
            initJob.randomBase = randomDataBase;
            var spawnCount = GetSingleton<ParticleSpawnCountComponent>().spawnCount;
            randomDataBase = (randomDataBase + spawnCount) % randomData.Length;

            inputDep = initJob.Schedule(this, inputDep);
            barrier.AddJobHandleForProducer(inputDep);
            return inputDep;
        }
    }
}

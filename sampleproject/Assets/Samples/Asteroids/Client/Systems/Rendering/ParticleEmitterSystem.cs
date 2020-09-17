using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Random = UnityEngine.Random;
using Unity.NetCode;

namespace Asteroids.Client
{
    struct ParticleSpawnCountComponent : IComponentData
    {
        public int spawnCount;
    }

    [UpdateBefore(typeof(ParticleUpdateSystemGroup))]
    [UpdateInGroup(typeof(ClientPresentationSystemGroup))]
    public class ParticleEmitterSystem : SystemBase
    {
        private BeginPresentationEntityCommandBufferSystem m_Barrier;
        private NativeQueue<int> m_SpawnCountQueue;

        EntityArchetype m_ColorSizeParticleArchetype;
        EntityArchetype m_ColorParticleArchetype;
        EntityArchetype m_SizeParticleArchetype;
        EntityArchetype m_ParticleArchetype;

        protected override void OnCreate()
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

            m_Barrier = World.GetOrCreateSystem<BeginPresentationEntityCommandBufferSystem>();
            m_SpawnCountQueue = new NativeQueue<int>(Allocator.Persistent);

            EntityManager.CreateEntity(typeof(ParticleSpawnCountComponent));
        }

        protected override void OnDestroy()
        {
            m_SpawnCountQueue.Dispose();
        }

        protected override void OnUpdate()
        {
            int spawnCount = 0;
            int cnt;
            while (m_SpawnCountQueue.TryDequeue(out cnt))
                spawnCount += cnt;
            SetSingleton(new ParticleSpawnCountComponent {spawnCount = spawnCount});
            var commandBuffer = m_Barrier.CreateCommandBuffer().AsParallelWriter();
            var spawnCountQueue = m_SpawnCountQueue.AsParallelWriter();
            var colorSizeParticleArchetype = m_ColorSizeParticleArchetype;
            var colorParticleArchetype = m_ColorParticleArchetype;
            var sizeParticleArchetype = m_SizeParticleArchetype;
            var particleArchetype = m_ParticleArchetype;
            var deltaTime = Time.DeltaTime;
            Entities.WithNone<ParticleComponentData>().ForEach((Entity entity, int nativeThreadIndex, in ParticleEmitterComponentData emitter, in Translation position,
                in Rotation rotation) =>
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
                EntityArchetype currentParticleType;
                if (colorTrans && sizeTrans)
                    currentParticleType = colorSizeParticleArchetype;
                else if (colorTrans)
                    currentParticleType = colorParticleArchetype;
                else if (sizeTrans)
                    currentParticleType = sizeParticleArchetype;
                else
                    currentParticleType = particleArchetype;
                // Create the first particle, then instantiate the rest based on its value
                var particle = commandBuffer.CreateEntity(nativeThreadIndex, currentParticleType);
                commandBuffer.SetComponent(nativeThreadIndex, particle, emitter);
                // Set initial data
                commandBuffer.SetComponent(nativeThreadIndex, particle, new ParticleVelocityComponentData());
                commandBuffer.SetComponent(nativeThreadIndex, particle,
                    new Translation {Value = position.Value + new float3(spawnOffset, 0)});
                commandBuffer.SetComponent(nativeThreadIndex, particle, new Rotation {Value = rotation.Value});
                if (colorTrans)
                    commandBuffer.SetComponent(nativeThreadIndex, particle,
                        new ParticleColorTransitionComponentData(emitter.startColor,
                            emitter.endColor));
                if (sizeTrans)
                    commandBuffer.SetComponent(nativeThreadIndex, particle,
                        new ParticleSizeTransitionComponentData(emitter.startLength,
                            emitter.endLength, emitter.startWidth,
                            emitter.endWidth));
                if (particles > 1)
                {
                    for (int i = 1; i < particles; ++i)
                        commandBuffer.Instantiate(nativeThreadIndex, particle);
                    //particleEntities.ResizeUninitialized(particles - 1);
                    //NativeArray<Entity> temp = particleEntities;
                    //EntityManager.Instantiate(particle, temp);
                }
            }).ScheduleParallel();
            m_Barrier.AddJobHandleForProducer(Dependency);
        }
    }

    [UpdateBefore(typeof(ParticleUpdateSystemGroup))]
    [UpdateAfter(typeof(ParticleEmitterSystem))]
    [UpdateInGroup(typeof(ClientPresentationSystemGroup))]
    public class ParticleInitializeSystem : SystemBase
    {
        private BeginPresentationEntityCommandBufferSystem m_Barrier;
        private NativeArray<float> m_RandomData;
        private int m_RandomDataBase;

        protected override void OnCreate()
        {
            m_RandomData = new NativeArray<float>(10 * 1024, Allocator.Persistent);
            for (int i = 0; i < m_RandomData.Length; ++i)
                m_RandomData[i] = Random.Range(0.0f, 1.0f);
            m_RandomDataBase = 0;

            m_Barrier = World.GetOrCreateSystem<BeginPresentationEntityCommandBufferSystem>();
        }

        protected override void OnDestroy()
        {
            m_RandomData.Dispose();
        }

        static float RandomRange(float minVal, float maxVal, NativeArray<float> randomData, ref int curRandom)
        {
            float rnd = randomData[curRandom % randomData.Length];
            curRandom = (curRandom + 1) % randomData.Length;
            return rnd * (maxVal - minVal) + minVal;
        }

        protected override void OnUpdate()
        {
            var commandBuffer = m_Barrier.CreateCommandBuffer().AsParallelWriter();
            var emitterComponentType = ComponentType.ReadOnly<ParticleEmitterComponentData>();
            var randomData = m_RandomData;
            var randomBase = m_RandomDataBase;
            var spawnCount = GetSingleton<ParticleSpawnCountComponent>().spawnCount;
            m_RandomDataBase = (m_RandomDataBase + spawnCount) % m_RandomData.Length;

            Entities.WithReadOnly(randomData).ForEach((Entity entity, int nativeThreadIndex, ref ParticleComponentData particle,
                ref Translation position, ref Rotation rotation, ref ParticleVelocityComponentData velocity,
                ref ParticleAgeComponentData age, in ParticleEmitterComponentData emitter) =>
            {
                int curRandom = randomBase + nativeThreadIndex;
                //float2 spawnOffset = RotationComponentData.rotate(spawners[em].emitter.spawnOffset, spawners[em].rotation);
                rotation.Value = math.mul(rotation.Value, quaternion.RotateZ(math.radians(RandomRange(-emitter.angleSpread,
                    emitter.angleSpread, randomData, ref curRandom))));
                float particleVelocity = emitter.velocityBase +
                                         RandomRange(0, emitter.velocityRandom, randomData, ref curRandom);
                float3 particleDir = new float3(0, particleVelocity, 0);
                velocity.velocity += math.mul(rotation.Value, particleDir).xy;

                particle = new ParticleComponentData(emitter.startLength,
                    emitter.startWidth, emitter.startColor);
                age = new ParticleAgeComponentData(emitter.particleLifetime);
                position.Value.x += RandomRange(-emitter.spawnSpread, emitter.spawnSpread, randomData, ref curRandom);
                position.Value.y += RandomRange(-emitter.spawnSpread, emitter.spawnSpread, randomData, ref curRandom);
                commandBuffer.RemoveComponent(nativeThreadIndex, entity, emitterComponentType);
            }).ScheduleParallel();
            m_Barrier.AddJobHandleForProducer(Dependency);
        }
    }
}

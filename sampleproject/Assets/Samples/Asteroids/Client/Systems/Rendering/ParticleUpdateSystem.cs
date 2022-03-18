using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.NetCode;
using Unity.Rendering;

namespace Asteroids.Client
{
    [UpdateInGroup(typeof(ClientSimulationSystemGroup))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    public class ParticleUpdateSystemGroup : ComponentSystemGroup
    {
    }

    [UpdateInGroup(typeof(ParticleUpdateSystemGroup))]
    public partial class ParticleAgeSystem : SystemBase
    {
        private BeginSimulationEntityCommandBufferSystem m_Barrier;

        protected override void OnCreate()
        {
            m_Barrier = World.GetOrCreateSystem<BeginSimulationEntityCommandBufferSystem>();
        }

        protected override void OnUpdate()
        {
            var commandBuffer = m_Barrier.CreateCommandBuffer().AsParallelWriter();
            var deltaTime = Time.DeltaTime;
            Entities.ForEach((Entity entity, int nativeThreadIndex, ref ParticleAge age) =>
            {
                age.age += deltaTime;
                if (age.age >= age.maxAge)
                {
                    age.age = age.maxAge;
                    commandBuffer.DestroyEntity(nativeThreadIndex, entity);
                }
            }).ScheduleParallel();
            m_Barrier.AddJobHandleForProducer(Dependency);
        }
    }

    [UpdateInGroup(typeof(ParticleUpdateSystemGroup))]
    public partial class ParticleMoveSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            var deltaTime = Time.DeltaTime;
            Entities.ForEach((ref Translation position, in ParticleVelocity velocity) =>
            {
                position.Value.x += velocity.velocity.x * deltaTime;
                position.Value.y += velocity.velocity.y * deltaTime;
            }).ScheduleParallel();
        }
    }

    [UpdateAfter(typeof(ParticleAgeSystem))]
    [UpdateInGroup(typeof(ParticleUpdateSystemGroup))]
    public partial class ParticleColorTransitionSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            Entities.ForEach((ref URPMaterialPropertyBaseColor color, in ParticleColorTransition colorDiff, in ParticleAge age) =>
            {
                float colorScale = age.age / age.maxAge;
                color.Value = colorDiff.startColor + (colorDiff.endColor - colorDiff.startColor) * colorScale;
            }).ScheduleParallel();
        }
    }

    [UpdateAfter(typeof(ParticleAgeSystem))]
    [UpdateInGroup(typeof(ParticleUpdateSystemGroup))]
    public partial class ParticleSizeTransitionSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            Entities.ForEach((ref NonUniformScale scale, in ParticleSizeTransition size,
                in ParticleAge age) =>
            {
                float sizeScale = age.age / age.maxAge;
                var particleLength = size.startLength + (size.endLength - size.startLength) * sizeScale;
                var particleWidth = size.startWidth + (size.endWidth - size.startWidth) * sizeScale;
                scale.Value = new float3(particleWidth, particleWidth + particleLength, particleWidth);
            }).ScheduleParallel();
        }
    }
}

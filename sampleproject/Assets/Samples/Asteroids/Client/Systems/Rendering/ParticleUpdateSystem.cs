using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.NetCode;

namespace Asteroids.Client
{
    [UpdateInGroup(typeof(ClientPresentationSystemGroup))]
    [UpdateBefore(typeof(LineRenderSystem))]
    public class ParticleUpdateSystemGroup : ComponentSystemGroup
    {
    }

    [UpdateInGroup(typeof(ParticleUpdateSystemGroup))]
    public class ParticleRenderSystem : SystemBase
    {
        private EntityQuery m_LineGroup;
        private NativeQueue<LineRenderSystem.Line>.ParallelWriter m_LineQueue;

        protected override void OnCreate()
        {
            m_LineGroup = GetEntityQuery(ComponentType.ReadWrite<LineRendererComponentData>());
            m_LineQueue = World.GetOrCreateSystem<LineRenderSystem>().LineQueue;
            RequireForUpdate(m_LineGroup);
        }

        protected override void OnUpdate()
        {
            var lines = m_LineQueue;
            Entities.ForEach((in ParticleComponentData particle, in Translation position, in Rotation rotation) =>
            {
                float3 pos = position.Value;
                float3 dir = new float3(0, particle.length, 0);
                dir = math.mul(rotation.Value, dir);
                lines.Enqueue(new LineRenderSystem.Line(pos.xy, pos.xy - dir.xy, particle.color, particle.width));
            }).ScheduleParallel();
            m_LineGroup.AddDependency(Dependency);
        }
    }

    [UpdateBefore(typeof(ParticleRenderSystem))]
    [UpdateInGroup(typeof(ParticleUpdateSystemGroup))]
    public class ParticleAgeSystem : SystemBase
    {
        private BeginPresentationEntityCommandBufferSystem m_Barrier;

        protected override void OnCreate()
        {
            m_Barrier = World.GetOrCreateSystem<BeginPresentationEntityCommandBufferSystem>();
        }

        protected override void OnUpdate()
        {
            var commandBuffer = m_Barrier.CreateCommandBuffer().AsParallelWriter();
            var deltaTime = Time.DeltaTime;
            Entities.ForEach((Entity entity, int nativeThreadIndex, ref ParticleAgeComponentData age) =>
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

    [UpdateBefore(typeof(ParticleRenderSystem))]
    [UpdateInGroup(typeof(ParticleUpdateSystemGroup))]
    public class ParticleMoveSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            var deltaTime = Time.DeltaTime;
            Entities.ForEach((ref Translation position, in ParticleVelocityComponentData velocity) =>
            {
                position.Value.x += velocity.velocity.x * deltaTime;
                position.Value.y += velocity.velocity.y * deltaTime;
            }).ScheduleParallel();
        }
    }

    [UpdateBefore(typeof(ParticleRenderSystem))]
    [UpdateAfter(typeof(ParticleAgeSystem))]
    [UpdateInGroup(typeof(ParticleUpdateSystemGroup))]
    public class ParticleColorTransitionSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            Entities.ForEach((ref ParticleComponentData particle, in ParticleColorTransitionComponentData color, in ParticleAgeComponentData age) =>
            {
                float colorScale = age.age / age.maxAge;
                particle.color = color.startColor + (color.endColor - color.startColor) * colorScale;
            }).ScheduleParallel();
        }
    }

    [UpdateBefore(typeof(ParticleRenderSystem))]
    [UpdateAfter(typeof(ParticleAgeSystem))]
    [UpdateInGroup(typeof(ParticleUpdateSystemGroup))]
    public class ParticleSizeTransitionSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            Entities.ForEach((ref ParticleComponentData particle, in ParticleSizeTransitionComponentData size,
                in ParticleAgeComponentData age) =>
            {
                float sizeScale = age.age / age.maxAge;
                particle.length = size.startLength + (size.endLength - size.startLength) * sizeScale;
                particle.width = size.startWidth + (size.endWidth - size.startWidth) * sizeScale;
            }).ScheduleParallel();
        }
    }
}

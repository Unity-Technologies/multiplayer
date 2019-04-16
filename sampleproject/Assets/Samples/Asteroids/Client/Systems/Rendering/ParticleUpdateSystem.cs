using Unity.Collections;
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Jobs;
using Unity.Burst;
using Unity.Transforms;

namespace Asteroids.Client
{
    [UpdateInGroup(typeof(ClientPresentationSystemGroup))]
    [UpdateBefore(typeof(LineRenderSystem))]
    [UpdateAfter(typeof(RenderInterpolationSystem))]
    public class ParticleUpdateSystemGroup : ComponentSystemGroup
    {
    }

    [UpdateInGroup(typeof(ParticleUpdateSystemGroup))]
    public class ParticleRenderSystem : JobComponentSystem
    {
        private ComponentGroup lineGroup;
        private NativeQueue<LineRenderSystem.Line>.Concurrent lineQueue;
        protected override void OnCreateManager()
        {
            lineGroup = GetComponentGroup(ComponentType.ReadWrite<LineRendererComponentData>());
            lineQueue = World.GetOrCreateManager<LineRenderSystem>().LineQueue;
            RequireForUpdate(lineGroup);
        }

        [BurstCompile]
        struct ParticleRenderJob : IJobProcessComponentData<ParticleComponentData, Translation, Rotation>
        {
            public NativeQueue<LineRenderSystem.Line>.Concurrent lines;
            public void Execute([ReadOnly] ref ParticleComponentData particle, [ReadOnly] ref Translation position, [ReadOnly] ref Rotation rotation)
            {
                float3 pos = position.Value;
                float3 dir = new float3(0, particle.length, 0);
                dir = math.mul(rotation.Value, dir);
                lines.Enqueue(new LineRenderSystem.Line(pos.xy, pos.xy - dir.xy, particle.color, particle.width));
            }
        }

        protected override JobHandle OnUpdate(JobHandle inputDep)
        {
            var job = new ParticleRenderJob();
            job.lines = lineQueue;
            return job.Schedule(this, inputDep);
        }
    }

    [UpdateBefore(typeof(ParticleRenderSystem))]
    [UpdateInGroup(typeof(ParticleUpdateSystemGroup))]
    public class ParticleAgeSystem : JobComponentSystem
    {
        private BeginPresentationEntityCommandBufferSystem barrier;

        protected override void OnCreateManager()
        {
            barrier = World.GetOrCreateManager<BeginPresentationEntityCommandBufferSystem>();
        }

        [BurstCompile]
        struct ParticleAgeJob : IJobProcessComponentDataWithEntity<ParticleAgeComponentData>
        {
            public float deltaTime;
            public EntityCommandBuffer.Concurrent commandBuffer;

            public void Execute(Entity entity, int index, ref ParticleAgeComponentData age)
            {
                age.age += deltaTime;
                if (age.age >= age.maxAge)
                {
                    age.age = age.maxAge;
                    commandBuffer.DestroyEntity(index, entity);
                }
            }
        }

        protected override JobHandle OnUpdate(JobHandle inputDep)
        {
            var job = new ParticleAgeJob();
            job.commandBuffer = barrier.CreateCommandBuffer().ToConcurrent();
            job.deltaTime = Time.deltaTime;
            var handle = job.Schedule(this, inputDep);
            barrier.AddJobHandleForProducer(handle);
            return handle;
        }
    }

    [UpdateBefore(typeof(ParticleRenderSystem))]
    [UpdateInGroup(typeof(ParticleUpdateSystemGroup))]
    public class ParticleMoveSystem : JobComponentSystem
    {
        [BurstCompile]
        struct ParticleMoveJob : IJobProcessComponentData<Translation, ParticleVelocityComponentData>
        {
            public float deltaTime;

            public void Execute(ref Translation position, [ReadOnly] ref ParticleVelocityComponentData velocity)
            {
                position.Value.x += velocity.velocity.x * deltaTime;
                position.Value.y += velocity.velocity.y * deltaTime;
            }
        }

        protected override JobHandle OnUpdate(JobHandle inputDep)
        {
            var job = new ParticleMoveJob();
            job.deltaTime = Time.deltaTime;
            return job.Schedule(this, inputDep);
        }
    }

    [UpdateBefore(typeof(ParticleRenderSystem))]
    [UpdateAfter(typeof(ParticleAgeSystem))]
    [UpdateInGroup(typeof(ParticleUpdateSystemGroup))]
    public class ParticleColorTransitionSystem : JobComponentSystem
    {
        [BurstCompile]
        struct ParticleColorJob : IJobProcessComponentData<ParticleComponentData, ParticleColorTransitionComponentData, ParticleAgeComponentData>
        {
            public void Execute(ref ParticleComponentData particle, [ReadOnly] ref ParticleColorTransitionComponentData color, [ReadOnly] ref ParticleAgeComponentData age)
            {
                float colorScale = age.age / age.maxAge;
                particle.color = color.startColor + (color.endColor - color.startColor) * colorScale;
            }
        }

        protected override JobHandle OnUpdate(JobHandle inputDep)
        {
            var job = new ParticleColorJob();
            return job.Schedule(this, inputDep);
        }
    }

    [UpdateBefore(typeof(ParticleRenderSystem))]
    [UpdateAfter(typeof(ParticleAgeSystem))]
    [UpdateInGroup(typeof(ParticleUpdateSystemGroup))]
    public class ParticleSizeTransitionSystem : JobComponentSystem
    {
        [BurstCompile]
        struct SizeJob : IJobProcessComponentData<ParticleComponentData, ParticleSizeTransitionComponentData, ParticleAgeComponentData>
        {
            public void Execute(ref ParticleComponentData particle, [ReadOnly] ref ParticleSizeTransitionComponentData size, [ReadOnly] ref ParticleAgeComponentData age)
            {
                float sizeScale = age.age / age.maxAge;
                particle.length = size.startLength + (size.endLength - size.startLength) * sizeScale;
                particle.width = size.startWidth + (size.endWidth - size.startWidth) * sizeScale;
            }
        }

        protected override JobHandle OnUpdate(JobHandle inputDep)
        {
            var job = new SizeJob();
            return job.Schedule(this, inputDep);
        }
    }
}

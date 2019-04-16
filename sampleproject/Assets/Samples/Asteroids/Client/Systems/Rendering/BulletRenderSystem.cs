using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;


namespace Asteroids.Client
{
    [UpdateAfter(typeof(RenderInterpolationSystem))]
    [UpdateBefore(typeof(LineRenderSystem))]
    [UpdateInGroup(typeof(ClientPresentationSystemGroup))]
    public class BulletRenderSystem : JobComponentSystem
    {
        private ComponentGroup lineGroup;
        private NativeQueue<LineRenderSystem.Line>.Concurrent lineQueue;
        protected override void OnCreateManager()
        {
            lineGroup = GetComponentGroup(ComponentType.ReadWrite<LineRendererComponentData>());
            lineQueue = World.GetOrCreateManager<LineRenderSystem>().LineQueue;
        }

        [BurstCompile]
        [RequireComponentTag(typeof(BulletTagComponentData))]
        struct ChunkRenderJob : IJobProcessComponentData<Translation, Rotation>
        {
            public NativeQueue<LineRenderSystem.Line>.Concurrent lines;
            public float4 bulletColor;
            public float4 trailColor;
            public float3 bulletTop;
            public float3 bulletBottom;
            public float3 trailBottom;
            public float bulletWidth;
            public float trailWidth;

            public void Execute([ReadOnly] ref Translation position, [ReadOnly] ref Rotation rotation)
            {
                float3 pos = position.Value;
                var rot = rotation.Value;
                var rotTop = pos + math.mul(rot, bulletTop);
                var rotBot = pos + math.mul(rot, bulletBottom);
                var rotTrail = pos + math.mul(rot, trailBottom);
                lines.Enqueue(new LineRenderSystem.Line(rotTop.xy, rotBot.xy, bulletColor, bulletWidth));
                lines.Enqueue(new LineRenderSystem.Line(rotTop.xy, rotTrail.xy, trailColor, trailWidth));
            }
        }

        override protected JobHandle OnUpdate(JobHandle inputDeps)
        {
            if (lineGroup.IsEmptyIgnoreFilter)
                return inputDeps;
            var rendJob = new ChunkRenderJob();
            rendJob.lines = lineQueue;

            rendJob.bulletWidth = 2;
            float bulletLength = 2;
            rendJob.trailWidth = 4;
            float trailLength = 4;
            rendJob.bulletColor = new float4((float) 0xfc / (float) 255, (float) 0x0f / (float) 255,
                (float) 0xc0 / (float) 255, 1);
            rendJob.trailColor = new float4((float) 0xfc / (float) 255, (float) 0x0f / (float) 255,
                (float) 0xc0 / (float) 255, 0.25f);
            rendJob.bulletTop = new float3(0, bulletLength / 2, 0);
            rendJob.bulletBottom = new float3(0, -bulletLength / 2, 0);
            rendJob.trailBottom = new float3(0, -trailLength, 0);

            return rendJob.Schedule(this, inputDeps);
        }
    }
}

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;


namespace Asteroids.Client
{
    [UpdateAfter(typeof(RenderInterpolationSystem))]
    [UpdateBefore(typeof(LineRenderSystem))]
    [UpdateInGroup(typeof(ClientPresentationSystemGroup))]
    public class AsteroidRenderSystem : JobComponentSystem
    {
        private ComponentGroup lineGroup;
        private NativeQueue<LineRenderSystem.Line>.Concurrent lineQueue;
        protected override void OnCreateManager()
        {
            lineGroup = GetComponentGroup(ComponentType.ReadWrite<LineRendererComponentData>());
            lineQueue = World.GetOrCreateManager<LineRenderSystem>().LineQueue;
        }

        float pulse = 1;
        float pulseDelta = 1;
        const float pulseMax = 1.2f;
        const float pulseMin = 0.8f;

        [BurstCompile]
        [RequireComponentTag(typeof(AsteroidTagComponentData))]
        struct ChunkRenderJob : IJobProcessComponentData<Translation, Rotation>
        {
            public NativeQueue<LineRenderSystem.Line>.Concurrent lines;
            public float astrLineWidth;
            public float4 astrColor;
            public float3 astrTL;
            public float3 astrTR;
            public float3 astrBL;
            public float3 astrBR;
            public float pulse;

            public void Execute([ReadOnly] ref Translation position, [ReadOnly] ref Rotation rotation)
            {
                float3 pos = position.Value;
                var rot = rotation.Value;
                var rotTL = pos + math.mul(rot, astrTL) * pulse;
                var rotTR = pos + math.mul(rot, astrTR) * pulse;
                var rotBL = pos + math.mul(rot, astrBL) * pulse;
                var rotBR = pos + math.mul(rot, astrBR) * pulse;
                lines.Enqueue(new LineRenderSystem.Line(rotTL.xy, rotTR.xy, astrColor, astrLineWidth));
                lines.Enqueue(new LineRenderSystem.Line(rotTL.xy, rotBL.xy, astrColor, astrLineWidth));
                lines.Enqueue(new LineRenderSystem.Line(rotTR.xy, rotBR.xy, astrColor, astrLineWidth));
                lines.Enqueue(new LineRenderSystem.Line(rotBL.xy, rotBR.xy, astrColor, astrLineWidth));
            }
        }

        override protected JobHandle OnUpdate(JobHandle inputDeps)
        {
            if (lineGroup.IsEmptyIgnoreFilter)
                return inputDeps;
            var rendJob = new ChunkRenderJob();
            rendJob.lines = lineQueue;

            float astrWidth = 30;
            float astrHeight = 30;
            rendJob.astrLineWidth = 2;
            rendJob.astrColor = new float4(0.25f, 0.85f, 0.85f, 1);
            rendJob.astrTL = new float3(-astrWidth / 2, -astrHeight / 2, 0);
            rendJob.astrTR = new float3(astrWidth / 2, -astrHeight / 2, 0);
            rendJob.astrBL = new float3(-astrWidth / 2, astrHeight / 2, 0);
            rendJob.astrBR = new float3(astrWidth / 2, astrHeight / 2, 0);

            pulse += pulseDelta * Time.deltaTime;
            if (pulse > pulseMax)
            {
                pulse = pulseMax;
                pulseDelta = -pulseDelta;
            }
            else if (pulse < pulseMin)
            {
                pulse = pulseMin;
                pulseDelta = -pulseDelta;
            }

            rendJob.pulse = pulse;
            return rendJob.Schedule(this, inputDeps);
        }
    }
}

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.NetCode;

namespace Asteroids.Client
{
    [UpdateBefore(typeof(LineRenderSystem))]
    [UpdateInGroup(typeof(ClientPresentationSystemGroup))]
    public class BulletRenderSystem : SystemBase
    {
        private EntityQuery m_LineGroup;
        private LineRenderSystem m_LineRenderSystem;

        protected override void OnCreate()
        {
            m_LineGroup = GetEntityQuery(ComponentType.ReadWrite<LineRendererComponentData>());
            m_LineRenderSystem = World.GetOrCreateSystem<LineRenderSystem>();
        }

        override protected void OnUpdate()
        {
            if (m_LineGroup.IsEmptyIgnoreFilter)
                return;

            var lineQueue = m_LineRenderSystem.LineQueue;

            float bulletWidth = 2;
            float bulletLength = 2;
            float trailWidth = 4;
            float trailLength = 4;
            var bulletColor = new float4((float) 0xfc / (float) 255, (float) 0x0f / (float) 255,
                (float) 0xc0 / (float) 255, 1);
            var trailColor = new float4((float) 0xfc / (float) 255, (float) 0x0f / (float) 255,
                (float) 0xc0 / (float) 255, 0.25f);
            var bulletTop = new float3(0, bulletLength / 2, 0);
            var bulletBottom = new float3(0, -bulletLength / 2, 0);
            var trailBottom = new float3(0, -trailLength, 0);
            var lines = lineQueue;

            Entities.WithAll<BulletTagComponent>().ForEach((in Translation position, in Rotation rotation) =>
            {
                float3 pos = position.Value;
                var rot = rotation.Value;
                var rotTop = pos + math.mul(rot, bulletTop);
                var rotBot = pos + math.mul(rot, bulletBottom);
                var rotTrail = pos + math.mul(rot, trailBottom);
                lines.Enqueue(new LineRenderSystem.Line(rotTop.xy, rotBot.xy, bulletColor, bulletWidth));
                lines.Enqueue(new LineRenderSystem.Line(rotTop.xy, rotTrail.xy, trailColor, trailWidth));
            }).ScheduleParallel();
        }
    }
}

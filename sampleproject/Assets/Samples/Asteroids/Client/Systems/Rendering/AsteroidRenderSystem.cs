using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.NetCode;

namespace Asteroids.Client
{
    [UpdateBefore(typeof(LineRenderSystem))]
    [UpdateInGroup(typeof(ClientPresentationSystemGroup))]
    public class AsteroidRenderSystem : SystemBase
    {
        private EntityQuery m_LineGroup;
        private LineRenderSystem m_LineRenderSystem;
        private float m_Pulse = 1;
        private float m_PulseDelta = 1;
        private const float m_PulseMax = 1.2f;
        private const float m_PulseMin = 0.8f;

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

            float astrWidth = 30;
            float astrHeight = 30;
            float astrLineWidth = 2;
            var astrColor = new float4(0.25f, 0.85f, 0.85f, 1);
            var astrTL = new float3(-astrWidth / 2, -astrHeight / 2, 0);
            var astrTR = new float3(astrWidth / 2, -astrHeight / 2, 0);
            var astrBL = new float3(-astrWidth / 2, astrHeight / 2, 0);
            var astrBR = new float3(astrWidth / 2, astrHeight / 2, 0);

            m_Pulse += m_PulseDelta * Time.DeltaTime;
            if (m_Pulse > m_PulseMax)
            {
                m_Pulse = m_PulseMax;
                m_PulseDelta = -m_PulseDelta;
            }
            else if (m_Pulse < m_PulseMin)
            {
                m_Pulse = m_PulseMin;
                m_PulseDelta = -m_PulseDelta;
            }
            var pulse = m_Pulse;
            var lines = lineQueue;

            Entities.WithAll<AsteroidTagComponentData>().ForEach((in Translation position, in Rotation rotation) =>
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
            }).ScheduleParallel();
        }
    }
}

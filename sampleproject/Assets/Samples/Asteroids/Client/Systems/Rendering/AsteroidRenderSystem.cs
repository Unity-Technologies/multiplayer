using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.NetCode;
using Unity.Rendering;

namespace Asteroids.Client
{
    [UpdateBefore(typeof(TransformSystemGroup))]
    [UpdateInGroup(typeof(ClientSimulationSystemGroup))]
    public partial class AsteroidRenderSystem : SystemBase
    {
        private float m_Pulse = 1;
        private float m_PulseDelta = 1;
        private const float m_PulseMax = 1.2f;
        private const float m_PulseMin = 0.8f;

        override protected void OnUpdate()
        {
            // Should ideally not be a hard-coded value
            float astrScale = 30;

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

            var predictedFromEntity = GetComponentDataFromEntity<PredictedGhostComponent>(true);
            Entities.WithReadOnly(predictedFromEntity).WithAll<AsteroidTagComponentData>().ForEach((Entity ent, ref NonUniformScale scale, ref URPMaterialPropertyBaseColor color) =>
            {
                color.Value = predictedFromEntity.HasComponent(ent) ? new float4(0,1,0,1) : new float4(1,1,1,1);
                scale.Value = new float3(astrScale * pulse);
            }).ScheduleParallel();
            Entities.WithNone<URPMaterialPropertyBaseColor>().WithAll<AsteroidTagComponentData>().ForEach((ref NonUniformScale scale) =>
            {
                scale.Value = new float3(astrScale * pulse);
            }).ScheduleParallel();
        }
    }
}

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.NetCode;

namespace Asteroids.Client
{
    [UpdateInGroup(typeof(ClientSimulationSystemGroup))]
    [UpdateAfter(typeof(GhostSimulationSystemGroup))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    public class StaticAsteroidSystem : SystemBase
    {
        ClientSimulationSystemGroup m_ClientSimulationSystemGroup;
        protected override void OnCreate()
        {
            m_ClientSimulationSystemGroup = World.GetExistingSystem<ClientSimulationSystemGroup>();
        }
        protected override void OnUpdate()
        {
            var tickRate = default(ClientServerTickRate);
            if (HasSingleton<ClientServerTickRate>())
            {
                tickRate = GetSingleton<ClientServerTickRate>();
            }

            tickRate.ResolveDefaults();

            var tick = m_ClientSimulationSystemGroup.InterpolationTick;
            var tickFraction = m_ClientSimulationSystemGroup.InterpolationTickFraction;
            var frameTime = 1.0f / (float) tickRate.SimulationTickRate;
            Entities.ForEach((ref Translation position, ref Rotation rotation, in StaticAsteroid staticAsteroid) =>
            {
                position.Value = staticAsteroid.GetPosition(tick, tickFraction, frameTime);
                rotation.Value = staticAsteroid.GetRotation(tick, tickFraction, frameTime);
            }).ScheduleParallel();
        }
    }
}

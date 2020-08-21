using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.NetCode;

namespace Asteroids.Server
{
    [UpdateInGroup(typeof(ServerSimulationSystemGroup))]
    public class AsteroidSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            var deltaTime = Time.DeltaTime;
            Entities.WithNone<StaticAsteroid>().WithAll<AsteroidTagComponentData>().ForEach((ref Translation position, ref Rotation rotation, in Velocity velocity) =>
            {
                position.Value.xy += velocity.Value * deltaTime;
                rotation.Value = math.mul(rotation.Value, quaternion.RotateZ(math.radians(100 * deltaTime)));
            }).ScheduleParallel();
        }
    }
}

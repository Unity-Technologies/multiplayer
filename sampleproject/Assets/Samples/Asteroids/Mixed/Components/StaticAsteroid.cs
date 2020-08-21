using Unity.Entities;
using Unity.NetCode;
using Unity.Mathematics;

[GenerateAuthoringComponent]
public struct StaticAsteroid : IComponentData
{
    [GhostField(Quantization = 100)] public float2 InitialPosition;
    [GhostField(Quantization = 100)] public float2 InitialVelocity;
    [GhostField(Quantization = 100)] public float InitialAngle;
    [GhostField] public uint SpawnTick;

    public float3 GetPosition(uint tick, float fraction, float frameTime)
    {
        float dt = ((tick - SpawnTick) - (1.0f - fraction)) * frameTime;
        return new float3(InitialPosition + InitialVelocity*dt, 0);
    }
    public quaternion GetRotation(uint tick, float fraction, float frameTime)
    {
        float dt = ((tick - SpawnTick) - (1.0f - fraction)) * frameTime;
        float angle = 360.0f;
        math.modf(dt*100 + InitialAngle, out angle);
        return quaternion.RotateZ(math.radians(angle));
    }
}

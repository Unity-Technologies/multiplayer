using Unity.Entities;
using Unity.Mathematics;

public struct ParticleComponentData : IComponentData
{
    public ParticleComponentData(float len, float w, float4 col)
    {
        length = len;
        width = w;
        color = col;
    }

    public float length;
    public float width;
    public float4 color;
}

public struct ParticleAgeComponentData : IComponentData
{
    public ParticleAgeComponentData(float maxAge)
    {
        this.maxAge = maxAge;
        age = 0;
    }

    public float maxAge;
    public float age;
}

public struct ParticleVelocityComponentData : IComponentData
{
    public ParticleVelocityComponentData(float2 velocity)
    {
        this.velocity = velocity;
    }

    public float2 velocity;
}

public struct ParticleColorTransitionComponentData : IComponentData
{
    public ParticleColorTransitionComponentData(float4 start, float4 end)
    {
        startColor = start;
        endColor = end;
    }

    public float4 startColor;
    public float4 endColor;
}

public struct ParticleSizeTransitionComponentData : IComponentData
{
    public ParticleSizeTransitionComponentData(float startL, float startW, float endL, float endW)
    {
        startLength = startL;
        startWidth = startW;
        endLength = endL;
        endWidth = endW;
    }

    public float startLength;
    public float startWidth;
    public float endLength;
    public float endWidth;
}

using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

[System.Serializable]
[GhostDefaultComponent(GhostDefaultComponentAttribute.Type.Client)]
public struct ParticleEmitterComponentData : IComponentData
{
    public float particlesPerSecond;
    public float angleSpread;
    public float velocityBase;
    public float velocityRandom;
    public float2 spawnOffset;
    public float spawnSpread;
    public float particleLifetime;

    public float startLength;
    public float startWidth;
    public float4 startColor;
    public float endLength;
    public float endWidth;
    public float4 endColor;

    public int active;
}

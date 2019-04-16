using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;

[System.Serializable]
public struct ParticleEmitterComponentData : IComponentData
{
    [SerializeField] public float particlesPerSecond;
    [SerializeField] public float angleSpread;
    [SerializeField] public float velocityBase;
    [SerializeField] public float velocityRandom;
    [SerializeField] public float2 spawnOffset;
    [SerializeField] public float spawnSpread;
    [SerializeField] public float particleLifetime;

    [SerializeField] public float startLength;
    [SerializeField] public float startWidth;
    [SerializeField] public float4 startColor;
    [SerializeField] public float endLength;
    [SerializeField] public float endWidth;
    [SerializeField] public float4 endColor;

    [SerializeField] public int active;
}

public class ParticleEmitterComponent : ComponentDataProxy<ParticleEmitterComponentData>
{
}

using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using System.Collections.Generic;

#if !UNITY_SERVER
public class ParticleEmitterAuthoringComponent : MonoBehaviour, IConvertGameObjectToEntity, IDeclareReferencedPrefabs
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
    public bool active;
    public GameObject particlePrefab;
    public void DeclareReferencedPrefabs(List<GameObject> referencedPrefabs)
    {
        if (particlePrefab != null)
            referencedPrefabs.Add(particlePrefab);
    }
    public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
    {
        dstManager.AddComponentData(entity, new ParticleEmitterComponentData
        {
            particlesPerSecond = particlesPerSecond,
            angleSpread = angleSpread,
            velocityBase = velocityBase,
            velocityRandom = velocityRandom,
            spawnOffset = spawnOffset,
            spawnSpread = spawnSpread,
            particleLifetime = particleLifetime,
            startLength = startLength,
            startWidth = startWidth,
            startColor = startColor,
            endLength = endLength,
            endWidth = endWidth,
            endColor = endColor,
            active = active?1:0,
            particlePrefab = conversionSystem.GetPrimaryEntity(particlePrefab)
        });
    }
}
#endif

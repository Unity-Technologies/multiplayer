using UnityEngine;
using Unity.Entities;
using Unity.Transforms;

public struct ClientSettings : IComponentData
{
    public ParticleEmitterComponentData particleEmitter;

    public EntityArchetype shipArchetype;
    public EntityArchetype asteroidClientArchetype;
    public EntityArchetype bulletClientArchetype;

    public ClientSettings(EntityManager manager)
    {
        var playerPrefab = Resources.Load("Prefabs/Ship") as GameObject;
        particleEmitter = playerPrefab.GetComponent<ParticleEmitterComponent>().Value;

        shipArchetype = manager.CreateArchetype(
            typeof(ShipTagComponentData),
            typeof(ShipStateComponentData),
            typeof(ParticleEmitterComponentData),
            typeof(CurrentSimulatedPosition),
            typeof(CurrentSimulatedRotation),
            typeof(Translation),
            typeof(Rotation));

        asteroidClientArchetype = manager.CreateArchetype(
            typeof(CurrentSimulatedPosition),
            typeof(CurrentSimulatedRotation),
            typeof(Translation),
            typeof(Rotation),
            typeof(AsteroidTagComponentData));

        bulletClientArchetype = manager.CreateArchetype(
            typeof(CurrentSimulatedPosition),
            typeof(CurrentSimulatedRotation),
            typeof(Translation),
            typeof(Rotation),
            typeof(BulletTagComponentData));
    }
}

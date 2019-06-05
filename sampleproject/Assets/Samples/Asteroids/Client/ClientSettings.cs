using UnityEngine;
using Unity.Entities;
using Unity.Transforms;

public struct ClientSettings : IComponentData
{
    public ParticleEmitterComponentData particleEmitter;

    public float playerForce;
    public float bulletVelocity;

    public ClientSettings(EntityManager manager)
    {
        var playerPrefab = Resources.Load("Prefabs/Ship") as GameObject;
        particleEmitter = playerPrefab.GetComponent<ParticleEmitterComponent>().Value;

        //TODO: This should come from the server via RPC at startup
        playerForce = 50f;
        bulletVelocity = 500f;
    }
}

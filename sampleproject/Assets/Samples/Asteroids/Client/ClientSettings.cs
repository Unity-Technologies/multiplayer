using Unity.Entities;

public struct ClientSettings : IComponentData
{
    public float playerForce;
    public float bulletVelocity;

    public ClientSettings(EntityManager manager)
    {
        //TODO: This should come from the server via RPC at startup
        playerForce = 50f;
        bulletVelocity = 500f;
    }
}

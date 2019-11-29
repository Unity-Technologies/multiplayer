using Unity.Entities;
using Unity.Transforms;
using Unity.NetCode;

public struct ServerSettings : IComponentData
{
    public float asteroidVelocity;
    public float playerForce;
    public float bulletVelocity;

    public int numAsteroids;
    public int levelWidth;
    public int levelHeight;
    public int damageShips;
}

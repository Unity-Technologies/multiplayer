using Unity.Entities;

[GenerateAuthoringComponent]
public struct ServerSettings : IComponentData
{
    public float asteroidVelocity;
    public float playerForce;
    public float bulletVelocity;

    public int numAsteroids;
    public int levelWidth;
    public int levelHeight;
    public bool damageShips;
    public int relevancyRadius;
    public bool staticAsteroidOptimization;
}

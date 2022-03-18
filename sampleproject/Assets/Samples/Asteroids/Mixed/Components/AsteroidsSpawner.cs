using Unity.Entities;

[GenerateAuthoringComponent]
public struct AsteroidsSpawner : IComponentData
{
    public Entity Ship;
    public Entity Bullet;
    public Entity Asteroid;
    public Entity StaticAsteroid;
}

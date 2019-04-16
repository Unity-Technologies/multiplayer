using Unity.Entities;
using Unity.Transforms;

public struct ServerSettings : IComponentData
{
    public float asteroidRadius;
    public float playerRadius;
    public float bulletRadius;

    public float asteroidVelocity;
    public float playerForce;
    public float bulletVelocity;

    public int numAsteroids;
    public int levelWidth;
    public int levelHeight;
    public int damageShips;

    public EntityArchetype shipArchetype;
    public EntityArchetype asteroidArchetype;
    public EntityArchetype bulletArchetype;

    public void InitArchetypes(EntityManager manager)
    {
        shipArchetype = manager.CreateArchetype(
            typeof(CollisionSphereComponentData),
            typeof(PlayerInputComponentData),
            typeof(Translation),
            typeof(Rotation),
            typeof(ShipTagComponentData),
            typeof(Velocity),
            typeof(PlayerIdComponentData),
            typeof(ShipStateComponentData),
            typeof(GhostComponent));

        asteroidArchetype = manager.CreateArchetype(
            typeof(Translation),
            typeof(Rotation),
            typeof(AsteroidTagComponentData),
            typeof(CollisionSphereComponentData),
            typeof(Velocity),
            typeof(GhostComponent));

        bulletArchetype = manager.CreateArchetype(
            typeof(Translation),
            typeof(Rotation),
            typeof(BulletTagComponentData),
            typeof(BulletAgeComponentData),
            typeof(CollisionSphereComponentData),
            typeof(Velocity),
            typeof(GhostComponent));
    }
}

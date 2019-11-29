using Unity.Entities;

public struct LevelComponent : IComponentData
{
    public int width;
    public int height;

    public float playerForce;
    public float bulletVelocity;
}

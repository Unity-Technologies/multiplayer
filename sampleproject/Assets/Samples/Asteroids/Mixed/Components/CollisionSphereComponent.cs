using Unity.Entities;

public struct CollisionSphereComponentData : IComponentData
{
    public float radius;

    public CollisionSphereComponentData(float radius)
    {
        this.radius = radius;
    }
}

using Unity.Entities;

[GenerateAuthoringComponent]
public struct CollisionSphereComponent : IComponentData
{
    public float radius;

    public CollisionSphereComponent(float radius)
    {
        this.radius = radius;
    }
}

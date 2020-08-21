using Unity.Entities;
using Unity.NetCode;

[GhostComponent(PrefabType = GhostPrefabType.Server)]
[GenerateAuthoringComponent]
public struct CollisionSphereComponent : IComponentData
{
    public float radius;

    public CollisionSphereComponent(float radius)
    {
        this.radius = radius;
    }
}

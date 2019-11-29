using Unity.Entities;

[GenerateAuthoringComponent]
public struct BulletAgeComponent : IComponentData
{
    public BulletAgeComponent(float maxAge)
    {
        this.maxAge = maxAge;
        age = 0;
    }

    public float age;
    public float maxAge;
}

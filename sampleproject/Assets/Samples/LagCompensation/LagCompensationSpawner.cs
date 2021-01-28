using Unity.Entities;

[GenerateAuthoringComponent]
public struct LagCompensationSpawner : IComponentData
{
    public Entity prefab;
}

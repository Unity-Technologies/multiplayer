using Unity.Entities;

public struct GhostClientPrefabComponent : IComponentData
{
    public Entity interpolatedPrefab;
    public Entity predictedPrefab;
}


public struct GhostServerPrefabComponent : IComponentData
{
    public Entity prefab;
}

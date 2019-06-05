using Unity.Entities;

public struct PlayerStateComponentData : IComponentData
{
    public int IsSpawning;
}

public struct PlayerIdComponentData : IComponentData
{
    public int PlayerId;
    public Entity PlayerEntity;
}

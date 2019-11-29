using Unity.Entities;
using Unity.NetCode;

public struct PlayerStateComponentData : IComponentData
{
    public int IsSpawning;
}

public struct PlayerIdComponentData : IComponentData
{
    [GhostDefaultField]
    public int PlayerId;
    public Entity PlayerEntity;
}

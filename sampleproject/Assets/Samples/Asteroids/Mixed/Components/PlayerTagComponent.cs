using Unity.Entities;
using Unity.NetCode;

public struct ShipTagComponentData : IComponentData
{
}

public struct ShipStateComponentData : IComponentData
{
    [GhostDefaultField]
    public int State;
    public uint WeaponCooldown;
}

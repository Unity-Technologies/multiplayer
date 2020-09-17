using Unity.Networking.Transport;
using Unity.NetCode;
using Unity.Burst;
using Unity.Entities;

[GhostComponent(PrefabType = GhostPrefabType.AllPredicted)]
public struct ShipCommandData : ICommandData
{
    public uint Tick {get; set;}
    public byte left;
    public byte right;
    public byte thrust;
    public byte shoot;
}

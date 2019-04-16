using Unity.Entities;

public unsafe struct PlayerInputComponentData : IComponentData
{
    public int mostRecentPos;
    public fixed uint tick[32];
    public fixed byte left[32];
    public fixed byte right[32];
    public fixed byte thrust[32];
    public fixed byte shoot[32];
}

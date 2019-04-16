using Unity.Entities;

public struct ShipTagComponentData : IComponentData
{
}

public struct ShipStateComponentData : IComponentData
{
    public ShipStateComponentData(int state, bool local)
    {
        State = state;
        IsLocalPlayer = local ? 1 : 0;
    }

    public int State;
    public int IsLocalPlayer;
}

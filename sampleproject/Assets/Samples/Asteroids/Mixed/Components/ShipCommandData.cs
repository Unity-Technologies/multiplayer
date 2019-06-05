using Unity.Networking.Transport;

public struct ShipCommandData : ICommandData<ShipCommandData>
{
    public uint Tick => tick;
    public uint tick;
    public byte left;
    public byte right;
    public byte thrust;
    public byte shoot;

    public void Serialize(DataStreamWriter writer)
    {
        writer.Write(left);
        writer.Write(right);
        writer.Write(thrust);
        writer.Write(shoot);
    }

    public void Deserialize(uint inputTick, DataStreamReader reader, ref DataStreamReader.Context ctx)
    {
        tick = inputTick;
        left = reader.ReadByte(ref ctx);
        right = reader.ReadByte(ref ctx);
        thrust = reader.ReadByte(ref ctx);
        shoot = reader.ReadByte(ref ctx);
    }
}

public class AsteroidsCommandReceiveSystem : CommandReceiveSystem<ShipCommandData>
{
}
public class AsteroidsCommandSendSystem : CommandSendSystem<ShipCommandData>
{
}

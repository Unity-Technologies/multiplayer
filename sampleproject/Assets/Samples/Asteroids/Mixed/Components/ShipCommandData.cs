using Unity.Networking.Transport;
using Unity.NetCode;

[GhostDefaultComponent(GhostDefaultComponentAttribute.Type.PredictedClient)]
public struct ShipCommandData : ICommandData<ShipCommandData>
{
    public uint Tick => tick;
    public uint tick;
    public byte left;
    public byte right;
    public byte thrust;
    public byte shoot;

    public void Serialize(ref DataStreamWriter writer)
    {
        writer.WriteByte(left);
        writer.WriteByte(right);
        writer.WriteByte(thrust);
        writer.WriteByte(shoot);
    }

    public void Deserialize(uint inputTick, ref DataStreamReader reader)
    {
        tick = inputTick;
        left = reader.ReadByte();
        right = reader.ReadByte();
        thrust = reader.ReadByte();
        shoot = reader.ReadByte();
    }

    public void Serialize(ref DataStreamWriter writer, ShipCommandData baseline, NetworkCompressionModel compressionModel)
    {
        writer.WriteByte(left);
        writer.WriteByte(right);
        writer.WriteByte(thrust);
        writer.WriteByte(shoot);
    }

    public void Deserialize(uint inputTick, ref DataStreamReader reader, ShipCommandData baseline,
        NetworkCompressionModel compressionModel)
    {
        tick = inputTick;
        left = reader.ReadByte();
        right = reader.ReadByte();
        thrust = reader.ReadByte();
        shoot = reader.ReadByte();
    }
}

public class AsteroidsCommandReceiveSystem : CommandReceiveSystem<ShipCommandData>
{
}
public class AsteroidsCommandSendSystem : CommandSendSystem<ShipCommandData>
{
}

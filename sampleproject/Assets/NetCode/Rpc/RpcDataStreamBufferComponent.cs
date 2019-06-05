using Unity.Entities;

public struct OutgoingRpcDataStreamBufferComponent : IBufferElementData
{
    public byte Value;
}
public struct IncomingRpcDataStreamBufferComponent : IBufferElementData
{
    public byte Value;
}

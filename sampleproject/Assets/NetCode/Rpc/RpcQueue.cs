using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Networking.Transport;
using Unity.Networking.Transport.LowLevel.Unsafe;

public struct RpcQueue<T>  where T : struct, IRpcCommand
{
    internal int rpcType;
    public unsafe void Schedule(DynamicBuffer<OutgoingRpcDataStreamBufferComponent> buffer, T data)
    {
        DataStreamWriter writer = new DataStreamWriter(128, Allocator.Temp);
        if (buffer.Length == 0)
            writer.Write((byte)NetworkStreamProtocol.Rpc);
        writer.Write(rpcType);
        data.Serialize(writer);
        var prevLen = buffer.Length;
        buffer.ResizeUninitialized(buffer.Length + writer.Length);
        byte* ptr = (byte*)buffer.GetUnsafePtr();
        ptr += prevLen;
        UnsafeUtility.MemCpy(ptr, writer.GetUnsafeReadOnlyPtr(), writer.Length);
    }
}

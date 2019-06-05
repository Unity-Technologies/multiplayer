using System;
using Unity.Entities;
using Unity.Networking.Transport;

internal struct InternalRpcCollection
{
    public static RpcQueue<T> GetRpcQueue<T>() where T : struct, IRpcCommand
    {
        int t = GetRpcFromTypeInternal<T>();
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        if (t < 0)
            throw new InvalidOperationException("Trying to get an internal rpc type which is not registered");
#endif
        return new RpcQueue<T>{rpcType = t};
    }
    public int Length => s_RpcTypes.Length;
    static Type[] s_RpcTypes = new Type[] {typeof(RpcSetNetworkId)};
    public void ExecuteRpc(int type, DataStreamReader reader, ref DataStreamReader.Context ctx, Entity connection, EntityCommandBuffer.Concurrent commandBuffer, int jobIndex)
    {
        switch (type)
        {
            case 0:
            {
                var tmp = new RpcSetNetworkId();
                tmp.Deserialize(reader, ref ctx);
                tmp.Execute(connection, commandBuffer, jobIndex);
                break;
            }
        }
    }

    static int GetRpcFromTypeInternal<T>() where T : struct, IRpcCommand
    {
        for (int i = 0; i < s_RpcTypes.Length; ++i)
        {
            if (s_RpcTypes[i] == typeof(T))
                return i;
        }

        return -1;
    }
    public int GetRpcFromType<T>() where T : struct, IRpcCommand
    {
        return GetRpcFromTypeInternal<T>();
    }
}


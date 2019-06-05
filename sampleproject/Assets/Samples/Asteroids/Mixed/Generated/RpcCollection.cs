using System;
using Unity.Entities;
using Unity.Networking.Transport;

public struct RpcCollection : IRpcCollection
{
    static Type[] s_RpcTypes = new Type[]
    {
        typeof(RpcLoadLevel),
        typeof(RpcLevelLoaded),
        typeof(RpcSpawn),

    };
    public void ExecuteRpc(int type, DataStreamReader reader, ref DataStreamReader.Context ctx, Entity connection, EntityCommandBuffer.Concurrent commandBuffer, int jobIndex)
    {
        switch (type)
        {
            case 0:
            {
                var tmp = new RpcLoadLevel();
                tmp.Deserialize(reader, ref ctx);
                tmp.Execute(connection, commandBuffer, jobIndex);
                break;
            }
            case 1:
            {
                var tmp = new RpcLevelLoaded();
                tmp.Deserialize(reader, ref ctx);
                tmp.Execute(connection, commandBuffer, jobIndex);
                break;
            }
            case 2:
            {
                var tmp = new RpcSpawn();
                tmp.Deserialize(reader, ref ctx);
                tmp.Execute(connection, commandBuffer, jobIndex);
                break;
            }

        }
    }

    public int GetRpcFromType<T>() where T : struct, IRpcCommand
    {
        for (int i = 0; i < s_RpcTypes.Length; ++i)
        {
            if (s_RpcTypes[i] == typeof(T))
                return i;
        }

        return -1;
    }
}

public class MultiplayerSampleRpcSystem : RpcSystem<RpcCollection>
{
}

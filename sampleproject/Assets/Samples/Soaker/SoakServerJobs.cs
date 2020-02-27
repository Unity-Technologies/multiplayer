using Unity.Burst;
using Unity.Networking.Transport;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine.Assertions;

struct SoakClientCtx
{
    public NetworkConnection Connection;
    public int NextSequenceId;
}

[BurstCompile]
struct SoakServerAcceptJob : IJob
{
    public int now;
    public NetworkDriver driver;
    public NativeList<SoakClientCtx> connections;

    public void Execute()
    {
        for (int i = 0; i < connections.Length; i++)
        {
            if (!connections[i].Connection.IsCreated)
            {
                connections.RemoveAtSwapBack(i);
                --i;
            }
        }

        NetworkConnection c;
        while ((c = driver.Accept()) != default(NetworkConnection))
        {
            connections.Add(new SoakClientCtx{ Connection = c, NextSequenceId = now});
        }
    }
}

[BurstCompile]
struct SoakServerUpdateClientsJob : IJobParallelForDefer
{
    public NetworkDriver.Concurrent driver;
    public NetworkPipeline pipeline;
    public NativeArray<SoakClientCtx> connections;

    public void Execute(int i)
    {
        SoakMessage inbound = default(SoakMessage);
        SoakMessage outbound = default(SoakMessage);

        if (connections[i].Connection.IsCreated)
        {
            var ctx = connections[i];
            DataStreamReader strm;
            NetworkEvent.Type cmd;
            bool close = false;
            while ((cmd = driver.PopEventForConnection(ctx.Connection, out strm)) != NetworkEvent.Type.Empty)
            {
                if (cmd == NetworkEvent.Type.Data)
                {
                    unsafe
                    {
                        strm.ReadBytes(inbound.data, strm.Length);
                        Assert.AreEqual(strm.Length, inbound.length + SoakMessage.HeaderLength);

                        outbound.id = inbound.id;
                        outbound.sequence = ctx.NextSequenceId++;

                        var soakData = driver.BeginSend(pipeline, connections[i].Connection);
                        soakData.WriteBytes(outbound.data, SoakMessage.HeaderLength);

                        driver.EndSend(soakData);
                    }
                }
                else if (cmd == NetworkEvent.Type.Disconnect)
                    close = true;
            }

            if (close)
            {
                ctx.Connection = default(NetworkConnection);
                ctx.NextSequenceId = -1;
            }
            connections[i] = ctx;
        }
    }
}

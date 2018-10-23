using System;
using Unity.Networking.Transport;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Assertions;

using NetworkConnection = Unity.Networking.Transport.NetworkConnection;
using UdpCNetworkDriver = Unity.Networking.Transport.BasicNetworkDriver<Unity.Networking.Transport.IPv4UDPSocket>;

struct SoakClientCtx
{
    public NetworkConnection Connection;
    public int NextSequenceId;
}

struct SoakServerAcceptJob : IJob
{
    public int now;
    public UdpCNetworkDriver driver;
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

struct SoakServerUpdateClientsJob : IJobParallelFor
{
    public UdpCNetworkDriver.Concurrent driver;
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
                    var readerCtx = default(DataStreamReader.Context);
                    unsafe
                    {
                        if (!strm.IsCreated)
                            Debug.Log("wtf not created.");
                        strm.ReadBytes(ref readerCtx, inbound.data, strm.Length);
                        Assert.AreEqual(strm.Length, inbound.length + SoakMessage.HeaderLength);

                        outbound.id = inbound.id;
                        outbound.sequence = ctx.NextSequenceId++;

                        var soakData = new DataStreamWriter(SoakMessage.HeaderLength, Allocator.Temp);
                        soakData.WriteBytes(outbound.data, SoakMessage.HeaderLength);

                        driver.Send(connections[i].Connection, soakData);
                        soakData.Dispose();
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

using System;
using System.Net;
using System.Net.Sockets;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Networking.Transport;
using UnityEngine.Experimental.PlayerLoop;
using UdpCNetworkDriver = Unity.Networking.Transport.BasicNetworkDriver<Unity.Networking.Transport.IPv4UDPSocket>;

[UpdateAfter(typeof(FixedUpdate))]
[UpdateAfter(typeof(PingBarrierSystem))]
public class PingServerSystem : JobComponentSystem
{
#pragma warning disable 649
    [Inject] private PingDriverSystem m_ServerDriverSystem;

    struct ConnectionList
    {
        public ComponentDataArray<PingServerConnectionComponentData> connections;
    }

    [Inject] private ConnectionList connectionList;
#pragma warning restore 649

    struct PongJob : IJobParallelFor
    {
        public UdpCNetworkDriver.Concurrent driver;
        public ComponentDataArray<PingServerConnectionComponentData> connections;

        public void Execute(int i)
        {
            DataStreamReader strm;
            NetworkEvent.Type cmd;
            while ((cmd = driver.PopEventForConnection(connections[i].connection, out strm)) != NetworkEvent.Type.Empty)
            {
                if (cmd == NetworkEvent.Type.Data)
                {
                    var readerCtx = default(DataStreamReader.Context);
                    int id = strm.ReadInt(ref readerCtx);
                    var pongData = new DataStreamWriter(4, Allocator.Temp);
                    pongData.Write(id);
                    driver.Send(connections[i].connection, pongData);
                    pongData.Dispose();
                }
                else if (cmd == NetworkEvent.Type.Disconnect)
                {
                    connections[i] = new PingServerConnectionComponentData {connection = default(NetworkConnection)};
                }
            }
        }
    }
    protected override JobHandle OnUpdate(JobHandle inputDep)
    {
        var pongJob = new PongJob
        {
            driver = m_ServerDriverSystem.ServerDriver.ToConcurrent(),
            connections = connectionList.connections
        };
        inputDep = pongJob.Schedule(connectionList.connections.Length, 1, inputDep);

        return inputDep;
    }
}

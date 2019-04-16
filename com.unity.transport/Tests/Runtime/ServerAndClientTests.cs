using System.Collections;
using Unity.Collections;
using NUnit.Framework;
using System;
using UnityEngine.TestTools;
using UnityEngine;
using Unity.Networking.Transport;

namespace Tests
{
    public class ServerAndClientTests
    {
        UdpNetworkDriver server_driver;
        NetworkConnection connectionToClient;

        UdpNetworkDriver client_driver;
        NetworkConnection clientToServerConnection;

        NetworkEvent.Type ev;
        DataStreamReader stream;

        public void SetupServerAndClientAndConnectThem(int bufferSize)
        {
            //setup server
            server_driver = new UdpNetworkDriver(new NetworkDataStreamParameter { size = bufferSize });
            NetworkEndPoint server_endpoint = NetworkEndPoint.LoopbackIpv4;
            server_endpoint.Port = 1337;
            server_driver.Bind(server_endpoint);
            server_driver.Listen();

            //setup client
            client_driver = new UdpNetworkDriver(new NetworkDataStreamParameter { size = bufferSize });
            clientToServerConnection = client_driver.Connect(server_endpoint);

            //update drivers
            client_driver.ScheduleUpdate().Complete();
            server_driver.ScheduleUpdate().Complete();

            //accept connection
            connectionToClient = server_driver.Accept();

            server_driver.ScheduleUpdate().Complete();
            ev = server_driver.PopEventForConnection(connectionToClient, out stream);
            Assert.IsTrue(ev == NetworkEvent.Type.Empty, "Not empty NetworkEvent on the server appeared");

            client_driver.ScheduleUpdate().Complete();
            ev = clientToServerConnection.PopEvent(client_driver, out stream);
            Assert.IsTrue(ev == NetworkEvent.Type.Connect, "NetworkEvent should have Type.Connect on the client");            
        }

        public void DisconnectAndCleanup()
        {
            clientToServerConnection.Close(client_driver);

            //update drivers
            client_driver.ScheduleUpdate().Complete();
            server_driver.ScheduleUpdate().Complete();

            ev = server_driver.PopEventForConnection(connectionToClient, out stream);
            Assert.IsTrue(ev == NetworkEvent.Type.Disconnect, "NetworkEvent.Type.Disconnect was expected to appear, but " + ev + "appeared");

            server_driver.Dispose();
            client_driver.Dispose();
        }

        [UnityTest]
        public IEnumerator ServerAndClient_Connect_Successfully()
        {
            SetupServerAndClientAndConnectThem(0);
            DisconnectAndCleanup();
            yield return null;
        }

        [UnityTest]
        public IEnumerator ServerAnd5Clients_Connect_Successfully()
        {
            int numberOfClients = 5;
            NativeArray<NetworkConnection> connectionToClientArray;
            UdpNetworkDriver[] client_driversArray = new UdpNetworkDriver[numberOfClients];
            NativeArray<NetworkConnection> clientToServerConnectionsArray;

            //setup server
            connectionToClientArray = new NativeArray<NetworkConnection>(numberOfClients, Allocator.Persistent);
            server_driver = new UdpNetworkDriver(new NetworkDataStreamParameter { size = 0 });
            NetworkEndPoint server_endpoint = NetworkEndPoint.LoopbackIpv4;
            server_endpoint.Port = 1337;
            server_driver.Bind(server_endpoint);
            server_driver.Listen();

            //setup clients
            clientToServerConnectionsArray = new NativeArray<NetworkConnection>(numberOfClients, Allocator.Persistent);

            for (int i = 0; i < numberOfClients; i++)
            {
                client_driversArray[i] = new UdpNetworkDriver(new NetworkDataStreamParameter { size = 0 });
                clientToServerConnectionsArray[i] = client_driversArray[i].Connect(server_endpoint);
            }

            //update drivers
            for (int i = 0; i < numberOfClients; i++)
                client_driversArray[i].ScheduleUpdate().Complete();
            server_driver.ScheduleUpdate().Complete();

            //accept connections
            for (int i = 0; i < numberOfClients; i++)
            {
                connectionToClientArray[i] = server_driver.Accept();

                server_driver.ScheduleUpdate().Complete();
                ev = server_driver.PopEventForConnection(connectionToClientArray[i], out stream);
                Assert.IsTrue(ev == NetworkEvent.Type.Empty, "Not empty NetworkEvent on the server appeared");

                client_driversArray[i].ScheduleUpdate().Complete();
                ev = clientToServerConnectionsArray[i].PopEvent(client_driversArray[i], out stream);
                Assert.IsTrue(ev == NetworkEvent.Type.Connect, "NetworkEvent should have Type.Connect on the client");
            }
            //close connections
            for (int i = 0; i < numberOfClients; i++)
            {
                clientToServerConnectionsArray[i].Close(client_driversArray[i]);

                //update drivers
                client_driversArray[i].ScheduleUpdate().Complete();
                server_driver.ScheduleUpdate().Complete();

                ev = server_driver.PopEventForConnection(connectionToClientArray[i], out stream);
                Assert.IsTrue(ev == NetworkEvent.Type.Disconnect, "NetworkEvent.Type.Disconnect was expected to appear, but " + ev + "appeared");
            }

            server_driver.Dispose();
            for (int i = 0; i < numberOfClients; i++)
            {
                client_driversArray[i].Dispose();
            }
            connectionToClientArray.Dispose();
            clientToServerConnectionsArray.Dispose();

            yield return null;
        }

        [UnityTest]
        public IEnumerator ServerAndClient_PingPong_Successfully()
        {
            SetupServerAndClientAndConnectThem(0);

            //send data from client
            DataStreamWriter m_OutStream = new DataStreamWriter(16, Allocator.Persistent);
            m_OutStream.Clear();
            m_OutStream.Write(SharedConstants.ping);
            clientToServerConnection.Send(client_driver, m_OutStream);

            //handle sent data
            server_driver.ScheduleUpdate().Complete();
            ev = server_driver.PopEventForConnection(connectionToClient, out stream);
            Assert.IsTrue(ev == NetworkEvent.Type.Data, "Expected to get Type.Data");
            var readerCtx = default(DataStreamReader.Context);
            var msg = stream.ReadBytesAsArray(ref readerCtx, stream.Length);
            if (msg.Length == SharedConstants.ping.Length)
            {
                for (var i = 0; i < msg.Length; i++)
                {
                    if (SharedConstants.ping[i] != msg[i])
                    {
                        Assert.Fail("Data reading error");
                    }
                }
            }

            client_driver.ScheduleUpdate().Complete();

            //send data from server
            m_OutStream.Clear();
            m_OutStream.Write(SharedConstants.pong);
            connectionToClient.Send(server_driver, m_OutStream);
            m_OutStream.Dispose();

            //handle sent data
            server_driver.ScheduleUpdate().Complete();
            client_driver.ScheduleUpdate().Complete();
            ev = clientToServerConnection.PopEvent(client_driver, out stream);
            Assert.IsTrue(ev == NetworkEvent.Type.Data, "Expected to get Type.Data");
            readerCtx = default(DataStreamReader.Context);
            msg = stream.ReadBytesAsArray(ref readerCtx, stream.Length);
            if (msg.Length == SharedConstants.pong.Length)
            {
                for (var i = 0; i < msg.Length; i++)
                {
                    if (SharedConstants.pong[i] != msg[i])
                    {
                        Assert.Fail("Data reading error");
                    }
                }
            }

            DisconnectAndCleanup();
            yield return null;
        }

        //test for buffer overflow
        [UnityTest]
        public IEnumerator ServerAndClient_SendBigMessage_OverflowsIncomingDriverBuffer()
        {
            SetupServerAndClientAndConnectThem(8);

            //send data from client
            DataStreamWriter m_OutStream = new DataStreamWriter(16, Allocator.Persistent);
            m_OutStream.Clear();
            m_OutStream.Write(SharedConstants.ping);
            clientToServerConnection.Send(client_driver, m_OutStream);
                       
            LogAssert.Expect(LogType.Error, "Error on receive 10040");            
            m_OutStream.Dispose();

            //handle sent data
            server_driver.ScheduleUpdate().Complete();
            client_driver.ScheduleUpdate().Complete();

            Assert.AreEqual(10040, server_driver.ReceiveErrorCode);

            DisconnectAndCleanup();
            yield return null;
        }

        [UnityTest]
        public IEnumerator ServerAndClient_SendMessageWithMaxLength_SentAndReceivedWithoutErrors()
        {
            SetupServerAndClientAndConnectThem(0);

            //send data from client
            DataStreamWriter m_OutStream = new DataStreamWriter(1500, Allocator.Persistent);
            m_OutStream.Clear();
            int messageLength = 1400;
            byte[] messageToSend = new byte[messageLength];
            for (int i = 0; i < messageLength; i++)
            {
                messageToSend[i] = (byte)(33 + (i % 93));
            }

            m_OutStream.Write(messageToSend);
            clientToServerConnection.Send(client_driver, m_OutStream);
            m_OutStream.Dispose();

            server_driver.ScheduleUpdate().Complete();
            ev = server_driver.PopEventForConnection(connectionToClient, out stream);
            Assert.IsTrue(ev == NetworkEvent.Type.Data, "Expected to get Type.Data");
            var readerCtx = default(DataStreamReader.Context);
            var msg = stream.ReadBytesAsArray(ref readerCtx, stream.Length);
            Assert.IsTrue(msg.Length == messageLength, "Lenghts of sent and received messages are different");

            DisconnectAndCleanup();
            yield return null;
        }

        [UnityTest]
        public IEnumerator ServerAndClient_SendMessageWithMoreThenMaxLength_OverflowsIncomingDriverBuffer()
        {
            SetupServerAndClientAndConnectThem(0);

            //send data from client
            DataStreamWriter m_OutStream = new DataStreamWriter(1500, Allocator.Persistent);
            m_OutStream.Clear();
            int messageLength = 1401;
            byte[] messageToSend = new byte[messageLength];
            for (int i = 0; i < messageLength; i++)
            {
                messageToSend[i] = (byte)(33 + (i % 93));
            }

            m_OutStream.Write(messageToSend);
            clientToServerConnection.Send(client_driver, m_OutStream);
            LogAssert.Expect(LogType.Error, "Error on receive 10040");
            m_OutStream.Dispose();

            //handle sent data
            server_driver.ScheduleUpdate().Complete();
            client_driver.ScheduleUpdate().Complete();

            Assert.AreEqual(10040, server_driver.ReceiveErrorCode);

            DisconnectAndCleanup();
            yield return null;
        }

        [UnityTest]
        public IEnumerator ServerAndClient_SendMessageWithoutReadingIt_GivesErrorOnDriverUpdate()
        {
            SetupServerAndClientAndConnectThem(0);

            //send data from client
            DataStreamWriter m_OutStream = new DataStreamWriter(16, Allocator.Persistent);
            m_OutStream.Clear();
            m_OutStream.Write(SharedConstants.ping);
            clientToServerConnection.Send(client_driver, m_OutStream);
            m_OutStream.Dispose();

            server_driver.ScheduleUpdate().Complete();
            client_driver.ScheduleUpdate().Complete();

            LogAssert.Expect(LogType.Error, "Resetting event queue with pending events (Count=1, ConnectionID=0) Listening: 1");
            server_driver.ScheduleUpdate().Complete();

            DisconnectAndCleanup();
            yield return null;
        }
    }
}

public class SharedConstants
{
    public static byte[] ping = {
        (byte)'f',
        (byte)'r',
        (byte)'o',
        (byte)'m',
        (byte)'s',
        (byte)'e',
        (byte)'r',
        (byte)'v',
        (byte)'e',
        (byte)'r'
    };

    public static byte[] pong = {
        (byte)'c',
        (byte)'l',
        (byte)'i',
        (byte)'e',
        (byte)'n',
        (byte)'t'
    };
}

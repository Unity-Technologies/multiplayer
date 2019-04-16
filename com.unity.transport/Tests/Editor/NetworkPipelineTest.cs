using System;
using System.Diagnostics;
using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Networking.Transport.Utilities;

namespace Unity.Networking.Transport.Tests
{
    public struct TestPipelineStageWithHeader : INetworkPipelineStage
    {
        public NativeSlice<byte> Receive(NetworkPipelineContext ctx, NativeSlice<byte> inboundBuffer, ref bool needsResume, ref bool needsUpdate, ref bool needsSendUpdate)
        {
            unsafe
            {
                var headerData = (int*)inboundBuffer.GetUnsafeReadOnlyPtr();
                if (*headerData != 1)
                    throw new InvalidOperationException("Header data invalid, got " + *headerData);
            }
            return new NativeSlice<byte>(inboundBuffer, 4, inboundBuffer.Length - 4);
        }

        public InboundBufferVec Send(NetworkPipelineContext ctx, InboundBufferVec inboundBuffer, ref bool needsResume, ref bool needsUpdate)
        {
            ctx.header.Write((int) 1);
            return inboundBuffer;
        }

        public void InitializeConnection(NativeSlice<byte> sendProcessBuffer, NativeSlice<byte> recvProcessBuffer, NativeSlice<byte> sharedProcessBuffer)
        {
        }

        public int ReceiveCapacity => 0;
        public int SendCapacity => 0;
        public int HeaderCapacity => 4;
        public int SharedStateCapacity { get; }
    }

    public struct TestPipelineStageWithHeaderTwo : INetworkPipelineStage
    {
        public unsafe NativeSlice<byte> Receive(NetworkPipelineContext ctx, NativeSlice<byte> inboundBuffer, ref bool needsResume, ref bool needsUpdate, ref bool needsSendUpdate)
        {
            var headerData = (int*)inboundBuffer.GetUnsafeReadOnlyPtr();
            if (*headerData != 2)
                throw new InvalidOperationException("Header data invalid, got " + *headerData);

            return new NativeSlice<byte>(inboundBuffer, 4, inboundBuffer.Length - 4);
        }

        public InboundBufferVec Send(NetworkPipelineContext ctx, InboundBufferVec inboundBuffer, ref bool needsResume, ref bool needsUpdate)
        {
            ctx.header.Write((int) 2);
            return inboundBuffer;
        }

        public void InitializeConnection(NativeSlice<byte> sendProcessBuffer, NativeSlice<byte> recvProcessBuffer, NativeSlice<byte> sharedProcessBuffer)
        {
        }

        public int ReceiveCapacity => 0;
        public int SendCapacity => 0;
        public int HeaderCapacity => 4;
        public int SharedStateCapacity { get; }
    }

    public struct TestEncryptPipelineStage : INetworkPipelineStage
    {
        private const int k_MaxPacketSize = 64;

        public NativeSlice<byte> Receive(NetworkPipelineContext ctx, NativeSlice<byte> inboundBuffer, ref bool needsResume, ref bool needsUpdate, ref bool needsSendUpdate)
        {
            for (int i = 0; i < inboundBuffer.Length; ++i)
                ctx.internalProcessBuffer[i] = (byte)(inboundBuffer[i] ^ 0xff);
            return new NativeSlice<byte>(ctx.internalProcessBuffer, 0, inboundBuffer.Length);
        }

        public InboundBufferVec Send(NetworkPipelineContext ctx, InboundBufferVec inboundBuffer, ref bool needsResume, ref bool needsUpdate)
        {
            var len1 = inboundBuffer.buffer1.Length;
            var len2 = inboundBuffer.buffer2.Length;
            for (int i = 0; i < len1; ++i)
                ctx.internalProcessBuffer[i] = (byte)(inboundBuffer.buffer1[i] ^ 0xff);
            for (int i = 0; i < len2; ++i)
                ctx.internalProcessBuffer[len1+i] = (byte)(inboundBuffer.buffer2[i] ^ 0xff);
            var nextInbound = default(InboundBufferVec);
            nextInbound.buffer1 =  new NativeSlice<byte>(ctx.internalProcessBuffer, 0, len1+len2);
            return nextInbound;
        }

        public void InitializeConnection(NativeSlice<byte> sendProcessBuffer, NativeSlice<byte> recvProcessBuffer, NativeSlice<byte> sharedProcessBuffer)
        {
        }

        public int ReceiveCapacity => k_MaxPacketSize;
        public int SendCapacity => k_MaxPacketSize;
        public int HeaderCapacity => 0;
        public int SharedStateCapacity { get; }
    }
    public struct TestEncryptInPlacePipelineStage : INetworkPipelineStage
    {
        public NativeSlice<byte> Receive(NetworkPipelineContext ctx, NativeSlice<byte> inboundBuffer, ref bool needsResume, ref bool needsUpdate, ref bool needsSendUpdate)
        {
            for (int i = 0; i < inboundBuffer.Length; ++i)
                inboundBuffer[i] = (byte)(inboundBuffer[i] ^ 0xff);
            return inboundBuffer;
        }

        public InboundBufferVec Send(NetworkPipelineContext ctx, InboundBufferVec inboundBuffer, ref bool needsResume, ref bool needsUpdate)
        {
            var len1 = inboundBuffer.buffer1.Length;
            var len2 = inboundBuffer.buffer2.Length;
            for (int i = 0; i < len1; ++i)
                ctx.internalProcessBuffer[i] = (byte)(inboundBuffer.buffer1[i] ^ 0xff);
            for (int i = 0; i < len2; ++i)
                ctx.internalProcessBuffer[len1+i] = (byte)(inboundBuffer.buffer2[i] ^ 0xff);
            var nextInbound = default(InboundBufferVec);
            nextInbound.buffer1 =  new NativeSlice<byte>(ctx.internalProcessBuffer, 0, len1+len2);
            return nextInbound;
        }

        public void InitializeConnection(NativeSlice<byte> sendProcessBuffer, NativeSlice<byte> recvProcessBuffer, NativeSlice<byte> sharedProcessBuffer)
        {
        }

        public int ReceiveCapacity => 0;
        public int SendCapacity => NetworkParameterConstants.MTU;
        public int HeaderCapacity => 0;
        public int SharedStateCapacity { get; }
    }
    public struct TestInvertPipelineStage : INetworkPipelineStage
    {
        public NativeSlice<byte> Receive(NetworkPipelineContext ctx, NativeSlice<byte> inboundBuffer, ref bool needsResume, ref bool needsUpdate, ref bool needsSendUpdate)
        {
            return inboundBuffer;
        }

        public unsafe InboundBufferVec Send(NetworkPipelineContext ctx, InboundBufferVec inboundBuffer, ref bool needsResume, ref bool needsUpdate)
        {
            var len1 = inboundBuffer.buffer1.Length;
            var len2 = inboundBuffer.buffer2.Length;
            for (int i = 0; i < len1; ++i)
                ctx.internalProcessBuffer[i] = (byte)(inboundBuffer.buffer1[i] ^ 0xff);
            for (int i = 0; i < len2; ++i)
                ctx.internalProcessBuffer[len1+i] = (byte)(inboundBuffer.buffer2[i] ^ 0xff);
            var nextInbound = default(InboundBufferVec);
            nextInbound.buffer1 =  new NativeSlice<byte>(ctx.internalProcessBuffer, 0, len1+len2);
            return nextInbound;
        }

        public void InitializeConnection(NativeSlice<byte> sendProcessBuffer, NativeSlice<byte> recvProcessBuffer, NativeSlice<byte> sharedProcessBuffer)
        {
        }

        public int ReceiveCapacity => 0;
        public int SendCapacity => NetworkParameterConstants.MTU;
        public int HeaderCapacity => 0;
        public int SharedStateCapacity { get; }
    }

    public unsafe struct TestPipelineWithInitializers : INetworkPipelineStage
    {
        public NativeSlice<byte> Receive(NetworkPipelineContext ctx, NativeSlice<byte> inboundBuffer, ref bool needsResume, ref bool needsUpdate, ref bool needsSendUpdate)
        {
            var receiveData = (int*)ctx.internalProcessBuffer.GetUnsafePtr();
            for (int i = 4; i <= 6; ++i)
            {
                Assert.AreEqual(*receiveData, i);
                receiveData++;
            }
            var sharedData = (int*)ctx.internalSharedProcessBuffer.GetUnsafePtr();
            for (int i = 7; i <= 8; ++i)
            {
                Assert.AreEqual(*sharedData, i);
                sharedData++;
            }
            return inboundBuffer;
        }

        public unsafe InboundBufferVec Send(NetworkPipelineContext ctx, InboundBufferVec inboundBuffer, ref bool needsResume, ref bool needsUpdate)
        {
            var sendData = (int*)ctx.internalProcessBuffer.GetUnsafePtr();
            for (int i = 1; i <= 3; ++i)
            {
                Assert.AreEqual(*sendData, i);
                sendData++;
            }
            var sharedData = (int*)ctx.internalSharedProcessBuffer.GetUnsafePtr();
            for (int i = 7; i <= 8; ++i)
            {
                Assert.AreEqual(*sharedData, i);
                sharedData++;
            }
            return inboundBuffer;
        }

        public unsafe void InitializeConnection(NativeSlice<byte> sendProcessBuffer, NativeSlice<byte> recvProcessBuffer, NativeSlice<byte> sharedProcessBuffer)
        {
            var sendData = (int*)sendProcessBuffer.GetUnsafePtr();
            *sendData = 1;
            sendData++;
            *sendData = 2;
            sendData++;
            *sendData = 3;
            var receiveData = (int*)recvProcessBuffer.GetUnsafePtr();
            *receiveData = 4;
            receiveData++;
            *receiveData = 5;
            receiveData++;
            *receiveData = 6;
            var sharedData = (int*) sharedProcessBuffer.GetUnsafePtr();
            *sharedData = 7;
            sharedData++;
            *sharedData = 8;
            sharedData++;
            *sharedData = 9;
        }

        public int ReceiveCapacity => 3*sizeof(int);
        public int SendCapacity => 3*sizeof(int);
        public int HeaderCapacity => 0;
        public int SharedStateCapacity => 3*sizeof(int);
    }

    public unsafe struct TestPipelineWithInitializersTwo : INetworkPipelineStage
    {
        public NativeSlice<byte> Receive(NetworkPipelineContext ctx, NativeSlice<byte> inboundBuffer, ref bool needsResume, ref bool needsUpdate, ref bool needsSendUpdate)
        {
            var receiveData = (int*)ctx.internalProcessBuffer.GetUnsafePtr();
            for (int i = 4; i <= 6; ++i)
            {
                Assert.AreEqual(*receiveData, i*10);
                receiveData++;
            }
            var sharedData = (int*)ctx.internalSharedProcessBuffer.GetUnsafePtr();
            for (int i = 7; i <= 8; ++i)
            {
                Assert.AreEqual(*sharedData, i*10);
                sharedData++;
            }
            return inboundBuffer;
        }

        public unsafe InboundBufferVec Send(NetworkPipelineContext ctx, InboundBufferVec inboundBuffer, ref bool needsResume, ref bool needsUpdate)
        {
            var sendData = (int*)ctx.internalProcessBuffer.GetUnsafePtr();
            for (int i = 1; i <= 3; ++i)
            {
                Assert.AreEqual(*sendData, i*10);
                sendData++;
            }
            var sharedData = (int*)ctx.internalSharedProcessBuffer.GetUnsafePtr();
            for (int i = 7; i <= 8; ++i)
            {
                Assert.AreEqual(*sharedData, i*10);
                sharedData++;
            }
            return inboundBuffer;
        }

        public unsafe void InitializeConnection(NativeSlice<byte> sendProcessBuffer, NativeSlice<byte> recvProcessBuffer, NativeSlice<byte> sharedProcessBuffer)
        {
            var sendData = (int*)sendProcessBuffer.GetUnsafePtr();
            *sendData = 10;
            sendData++;
            *sendData = 20;
            sendData++;
            *sendData = 30;
            var receiveData = (int*)recvProcessBuffer.GetUnsafePtr();
            *receiveData = 40;
            receiveData++;
            *receiveData = 50;
            receiveData++;
            *receiveData = 60;
            var sharedData = (int*) sharedProcessBuffer.GetUnsafePtr();
            *sharedData = 70;
            sharedData++;
            *sharedData = 80;
            sharedData++;
            *sharedData = 90;
        }

        public int ReceiveCapacity => 3*sizeof(int);
        public int SendCapacity => 3*sizeof(int);
        public int HeaderCapacity => 0;
        public int SharedStateCapacity => 3*sizeof(int);
    }

    public struct TestNetworkPipelineStageCollection : INetworkPipelineStageCollection
    {
        private TestPipelineStageWithHeader testPipelineStageWithHeader;
        private TestPipelineStageWithHeaderTwo testPipelineStageWithHeaderTwo;
        private TestEncryptPipelineStage testEncryptPipelineStage;
        private TestEncryptInPlacePipelineStage testEncryptInPlacePipelineStage;
        private TestInvertPipelineStage testInvertPipelineStage;
        private TestPipelineWithInitializers testPipelineWithInitializers;
        private TestPipelineWithInitializersTwo testPipelineWithInitializersTwo;
        private SimulatorPipelineStage testDelayedReadPipelineStage;
        private SimulatorPipelineStageInSend testDelayedSendPipelineStage;
        private UnreliableSequencedPipelineStage testUnreliableSequencedPipelineStage;

        public int GetStageId(Type type)
        {
            if (type == typeof(TestPipelineStageWithHeader))
                return 0;
            if (type == typeof(TestEncryptPipelineStage))
                return 1;
            if (type == typeof(TestEncryptInPlacePipelineStage))
                return 2;
            if (type == typeof(TestInvertPipelineStage))
                return 3;
            if (type == typeof(SimulatorPipelineStage))
                return 4;
            if (type == typeof(SimulatorPipelineStageInSend))
                return 5;
            if (type == typeof(UnreliableSequencedPipelineStage))
                return 6;
            if (type == typeof(TestPipelineStageWithHeaderTwo))
                return 7;
            if (type == typeof(TestPipelineWithInitializers))
                return 8;
            if (type == typeof(TestPipelineWithInitializersTwo))
                return 9;

            return -1;
        }

        public void Initialize(params INetworkParameter[] param)
        {
            for (int i = 0; i < param.Length; ++i)
            {
                if (param[i] is SimulatorUtility.Parameters)
                {
                    testDelayedReadPipelineStage.Initialize((SimulatorUtility.Parameters)param[i]);
                    testDelayedSendPipelineStage.Initialize((SimulatorUtility.Parameters)param[i]);
                }
            }
        }

        public void InvokeInitialize(int pipelineStageId, NativeSlice<byte> sendProcessBuffer, NativeSlice<byte> recvProcessBuffer,
            NativeSlice<byte> sharedStateBuffer)
        {
            switch (pipelineStageId)
            {
                case 4:
                    testDelayedReadPipelineStage.InitializeConnection(sendProcessBuffer, recvProcessBuffer, sharedStateBuffer);
                    break;
                case 5:
                    testDelayedSendPipelineStage.InitializeConnection(sendProcessBuffer, recvProcessBuffer, sharedStateBuffer);
                    break;
                case 6:
                    testUnreliableSequencedPipelineStage.InitializeConnection(sendProcessBuffer, recvProcessBuffer, sharedStateBuffer);
                    break;
                case 8:
                    testPipelineWithInitializers.InitializeConnection(sendProcessBuffer, recvProcessBuffer, sharedStateBuffer);
                    break;
                case 9:
                    testPipelineWithInitializersTwo.InitializeConnection(sendProcessBuffer, recvProcessBuffer, sharedStateBuffer);
                    break;
            }
        }

        public InboundBufferVec InvokeSend(int pipelineStageId, NetworkPipelineContext ctx, InboundBufferVec inboundBuffer, ref bool needsResume, ref bool needsUpdate)
        {
            switch (pipelineStageId)
            {
                case 0:
                    return testPipelineStageWithHeader.Send(ctx, inboundBuffer, ref needsResume, ref needsUpdate);
                case 1:
                    return testEncryptPipelineStage.Send(ctx, inboundBuffer, ref needsResume, ref needsUpdate);
                case 2:
                    return testEncryptInPlacePipelineStage.Send(ctx, inboundBuffer, ref needsResume, ref needsUpdate);
                case 3:
                    return testInvertPipelineStage.Send(ctx, inboundBuffer, ref needsResume, ref needsUpdate);
                case 4:
                    return testDelayedReadPipelineStage.Send(ctx, inboundBuffer, ref needsResume, ref needsUpdate);
                case 5:
                    return testDelayedSendPipelineStage.Send(ctx, inboundBuffer, ref needsResume, ref needsUpdate);
                case 6:
                    return testUnreliableSequencedPipelineStage.Send(ctx, inboundBuffer, ref needsResume, ref needsUpdate);
                case 7:
                    return testPipelineStageWithHeaderTwo.Send(ctx, inboundBuffer, ref needsResume, ref needsUpdate);
                case 8:
                    return testPipelineWithInitializers.Send(ctx, inboundBuffer, ref needsResume, ref needsUpdate);
                case 9:
                    return testPipelineWithInitializersTwo.Send(ctx, inboundBuffer, ref needsResume, ref needsUpdate);
            }
            return inboundBuffer;
        }

        public NativeSlice<byte> InvokeReceive(int pipelineStageId, NetworkPipelineContext ctx, NativeSlice<byte> inboundBuffer, ref bool needsResume, ref bool needsUpdate, ref bool needsSendUpdate)
        {
            switch (pipelineStageId)
            {
                case 0:
                    return testPipelineStageWithHeader.Receive(ctx, inboundBuffer, ref needsResume, ref needsUpdate, ref needsSendUpdate);
                case 1:
                    return testEncryptPipelineStage.Receive(ctx, inboundBuffer, ref needsResume, ref needsUpdate, ref needsSendUpdate);
                case 2:
                    return testEncryptInPlacePipelineStage.Receive(ctx, inboundBuffer, ref needsResume, ref needsUpdate, ref needsSendUpdate);
                case 3:
                    return testInvertPipelineStage.Receive(ctx, inboundBuffer, ref needsResume, ref needsUpdate, ref needsSendUpdate);
                case 4:
                    return testDelayedReadPipelineStage.Receive(ctx, inboundBuffer, ref needsResume, ref needsUpdate, ref needsSendUpdate);
                case 5:
                    return testDelayedSendPipelineStage.Receive(ctx, inboundBuffer, ref needsResume, ref needsUpdate, ref needsSendUpdate);
                case 6:
                    return testUnreliableSequencedPipelineStage.Receive(ctx, inboundBuffer, ref needsResume, ref needsUpdate, ref needsSendUpdate);
                case 7:
                    return testPipelineStageWithHeaderTwo.Receive(ctx, inboundBuffer, ref needsResume, ref needsUpdate, ref needsSendUpdate);
                case 8:
                    return testPipelineWithInitializers.Receive(ctx, inboundBuffer, ref needsResume, ref needsUpdate, ref needsSendUpdate);
                case 9:
                    return testPipelineWithInitializersTwo.Receive(ctx, inboundBuffer, ref needsResume, ref needsUpdate, ref needsSendUpdate);
            }
            return inboundBuffer;
        }

        public int GetReceiveCapacity(int pipelineStageId)
        {
            switch (pipelineStageId)
            {
                case 0:
                    return testPipelineStageWithHeader.ReceiveCapacity;
                case 1:
                    return testEncryptPipelineStage.ReceiveCapacity;
                case 2:
                    return testEncryptInPlacePipelineStage.ReceiveCapacity;
                case 3:
                    return testInvertPipelineStage.ReceiveCapacity;
                case 4:
                    return testDelayedReadPipelineStage.ReceiveCapacity;
                case 5:
                    return testDelayedSendPipelineStage.ReceiveCapacity;
                case 6:
                    return testUnreliableSequencedPipelineStage.ReceiveCapacity;
                case 7:
                    return testPipelineStageWithHeaderTwo.ReceiveCapacity;
                case 8:
                    return testPipelineWithInitializers.ReceiveCapacity;
                case 9:
                    return testPipelineWithInitializersTwo.ReceiveCapacity;
            }
            return 0;
        }

        public int GetSendCapacity(int pipelineStageId)
        {
            switch (pipelineStageId)
            {
                case 0:
                    return testPipelineStageWithHeader.SendCapacity;
                case 1:
                    return testEncryptPipelineStage.SendCapacity;
                case 2:
                    return testEncryptInPlacePipelineStage.SendCapacity;
                case 3:
                    return testInvertPipelineStage.SendCapacity;
                case 4:
                    return testDelayedReadPipelineStage.SendCapacity;
                case 5:
                    return testDelayedSendPipelineStage.SendCapacity;
                case 6:
                    return testUnreliableSequencedPipelineStage.SendCapacity;
                case 7:
                    return testPipelineStageWithHeaderTwo.SendCapacity;
                case 8:
                    return testPipelineWithInitializers.SendCapacity;
                case 9:
                    return testPipelineWithInitializersTwo.SendCapacity;
            }
            return 0;
        }

        public int GetHeaderCapacity(int pipelineStageId)
        {
            switch (pipelineStageId)
            {
                case 0:
                    return testPipelineStageWithHeader.HeaderCapacity;
                case 1:
                    return testEncryptPipelineStage.HeaderCapacity;
                case 2:
                    return testEncryptInPlacePipelineStage.HeaderCapacity;
                case 3:
                    return testInvertPipelineStage.HeaderCapacity;
                case 4:
                    return testDelayedReadPipelineStage.HeaderCapacity;
                case 5:
                    return testDelayedSendPipelineStage.HeaderCapacity;
                case 6:
                    return testUnreliableSequencedPipelineStage.HeaderCapacity;
                case 7:
                    return testPipelineStageWithHeaderTwo.HeaderCapacity;
                case 8:
                    return testPipelineWithInitializers.HeaderCapacity;
                case 9:
                    return testPipelineWithInitializersTwo.HeaderCapacity;
            }
            return 0;
        }

        public int GetSharedStateCapacity(int pipelineStageId)
        {
            switch (pipelineStageId)
            {
                case 0:
                    return testPipelineStageWithHeader.SharedStateCapacity;
                case 1:
                    return testEncryptPipelineStage.SharedStateCapacity;
                case 2:
                    return testEncryptInPlacePipelineStage.SharedStateCapacity;
                case 3:
                    return testInvertPipelineStage.SharedStateCapacity;
                case 4:
                    return testDelayedReadPipelineStage.SharedStateCapacity;
                case 5:
                    return testDelayedSendPipelineStage.SharedStateCapacity;
                case 6:
                    return testUnreliableSequencedPipelineStage.SharedStateCapacity;
                case 7:
                    return testPipelineStageWithHeaderTwo.SharedStateCapacity;
                case 8:
                    return testPipelineWithInitializers.SharedStateCapacity;
                case 9:
                    return testPipelineWithInitializersTwo.SharedStateCapacity;
            }
            return 0;
        }
    }

    public class NetworkPipelineTest
    {
        private GenericNetworkDriver<IPCSocket, TestNetworkPipelineStageCollection> m_ServerDriver;
        private GenericNetworkDriver<IPCSocket, TestNetworkPipelineStageCollection> m_ClientDriver;
        private GenericNetworkDriver<IPCSocket, TestNetworkPipelineStageCollection> m_ClientDriver2;

        [SetUp]
        public void IPC_Setup()
        {
            IPCManager.Instance.Initialize(100);
            // NOTE: MaxPacketSize should be 64 for all the tests using simulator except needs to account for header size as well (one test has 2x2B headers)
            var simulatorParams = new SimulatorUtility.Parameters()
                {MaxPacketSize = 68, MaxPacketCount = 30, PacketDelayMs = 100};
            m_ServerDriver = new GenericNetworkDriver<IPCSocket, TestNetworkPipelineStageCollection>(simulatorParams);
            m_ServerDriver.Bind(IPCManager.Instance.CreateEndPoint());
            m_ServerDriver.Listen();
            m_ClientDriver = new GenericNetworkDriver<IPCSocket, TestNetworkPipelineStageCollection>(simulatorParams);
            m_ClientDriver2 = new GenericNetworkDriver<IPCSocket, TestNetworkPipelineStageCollection>(simulatorParams);
        }

        [TearDown]
        public void IPC_TearDown()
        {
            m_ClientDriver.Dispose();
            m_ClientDriver2.Dispose();
            m_ServerDriver.Dispose();
            IPCManager.Instance.Destroy();
        }
        [Test]
        public void NetworkPipeline_CreatePipelineIsSymetrical()
        {
            var clientPipe = m_ClientDriver.CreatePipeline(typeof(TestPipelineStageWithHeader));
            var serverPipe = m_ServerDriver.CreatePipeline(typeof(TestPipelineStageWithHeader));
            Assert.AreEqual(clientPipe, serverPipe);
        }
        [Test]
        public void NetworkPipeline_CreatePipelineAfterConnectFails()
        {
            m_ClientDriver.Connect(m_ServerDriver.LocalEndPoint());
            Assert.Throws<InvalidOperationException>(() => { m_ClientDriver.CreatePipeline(typeof(TestPipelineStageWithHeader)); });
        }
        [Test]
        public void NetworkPipeline_CreatePipelineWithInvalidStageFails()
        {
            Assert.Throws<InvalidOperationException>(() => { m_ClientDriver.CreatePipeline(typeof(NetworkPipelineTest)); });
        }

        [Test]
        public void NetworkPipeline_CanExtendHeader()
        {
            // Create pipeline
            var clientPipe = m_ClientDriver.CreatePipeline(typeof(TestPipelineStageWithHeader));
            var serverPipe = m_ServerDriver.CreatePipeline(typeof(TestPipelineStageWithHeader));
            Assert.AreEqual(clientPipe, serverPipe);

            // Connect to server
            var clientToServer = m_ClientDriver.Connect(m_ServerDriver.LocalEndPoint());
            Assert.AreNotEqual(default(NetworkConnection), clientToServer);
            m_ClientDriver.ScheduleUpdate().Complete();

            // Handle incoming connection from client
            m_ServerDriver.ScheduleUpdate().Complete();
            var serverToClient = m_ServerDriver.Accept();
            Assert.AreNotEqual(default(NetworkConnection), serverToClient);

            // Send message to client
            var strm = new DataStreamWriter(4, Allocator.Temp);
            strm.Write((int) 42);
            m_ServerDriver.Send(serverPipe, serverToClient, strm);
            m_ServerDriver.ScheduleUpdate().Complete();

            // Receive incoming message from server
            m_ClientDriver.ScheduleUpdate().Complete();
            DataStreamReader readStrm;
            Assert.AreEqual(NetworkEvent.Type.Connect, clientToServer.PopEvent(m_ClientDriver, out readStrm));
            Assert.AreEqual(NetworkEvent.Type.Data, clientToServer.PopEvent(m_ClientDriver, out readStrm));
            Assert.AreEqual(4, readStrm.Length);
            var readCtx = default(DataStreamReader.Context);
            Assert.AreEqual(42, readStrm.ReadInt(ref readCtx));
        }
        [Test]
        public void NetworkPipeline_CanModifyAndRestoreData()
        {
            // Create pipeline
            var clientPipe = m_ClientDriver.CreatePipeline(typeof(TestEncryptPipelineStage));
            var serverPipe = m_ServerDriver.CreatePipeline(typeof(TestEncryptPipelineStage));
            Assert.AreEqual(clientPipe, serverPipe);

            // Connect to server
            var clientToServer = m_ClientDriver.Connect(m_ServerDriver.LocalEndPoint());
            Assert.AreNotEqual(default(NetworkConnection), clientToServer);
            m_ClientDriver.ScheduleUpdate().Complete();

            // Handle incoming connection from client
            m_ServerDriver.ScheduleUpdate().Complete();
            var serverToClient = m_ServerDriver.Accept();
            Assert.AreNotEqual(default(NetworkConnection), serverToClient);

            // Send message to client
            var strm = new DataStreamWriter(4, Allocator.Temp);
            strm.Write((int) 42);
            m_ServerDriver.Send(serverPipe, serverToClient, strm);
            m_ServerDriver.ScheduleUpdate().Complete();

            // Receive incoming message from server
            m_ClientDriver.ScheduleUpdate().Complete();
            DataStreamReader readStrm;
            Assert.AreEqual(NetworkEvent.Type.Connect, clientToServer.PopEvent(m_ClientDriver, out readStrm));
            Assert.AreEqual(NetworkEvent.Type.Data, clientToServer.PopEvent(m_ClientDriver, out readStrm));
            Assert.AreEqual(4, readStrm.Length);
            var readCtx = default(DataStreamReader.Context);
            Assert.AreEqual(42, readStrm.ReadInt(ref readCtx));
        }
        [Test]
        public void NetworkPipeline_CanModifyAndRestoreDataInPlace()
        {
            // Create pipeline
            var clientPipe = m_ClientDriver.CreatePipeline(typeof(TestEncryptInPlacePipelineStage));
            var serverPipe = m_ServerDriver.CreatePipeline(typeof(TestEncryptInPlacePipelineStage));
            Assert.AreEqual(clientPipe, serverPipe);

            // Connect to server
            var clientToServer = m_ClientDriver.Connect(m_ServerDriver.LocalEndPoint());
            Assert.AreNotEqual(default(NetworkConnection), clientToServer);
            m_ClientDriver.ScheduleUpdate().Complete();

            // Handle incoming connection from client
            m_ServerDriver.ScheduleUpdate().Complete();
            var serverToClient = m_ServerDriver.Accept();
            Assert.AreNotEqual(default(NetworkConnection), serverToClient);

            // Send message to client
            var strm = new DataStreamWriter(4, Allocator.Temp);
            strm.Write((int) 42);
            m_ServerDriver.Send(serverPipe, serverToClient, strm);
            m_ServerDriver.ScheduleUpdate().Complete();

            // Receive incoming message from server
            m_ClientDriver.ScheduleUpdate().Complete();
            DataStreamReader readStrm;
            Assert.AreEqual(NetworkEvent.Type.Connect, clientToServer.PopEvent(m_ClientDriver, out readStrm));
            Assert.AreEqual(NetworkEvent.Type.Data, clientToServer.PopEvent(m_ClientDriver, out readStrm));
            Assert.AreEqual(4, readStrm.Length);
            var readCtx = default(DataStreamReader.Context);
            Assert.AreEqual(42, readStrm.ReadInt(ref readCtx));
        }
        [Test]
        public void NetworkPipeline_CanModifyData()
        {
            // Create pipeline
            var clientPipe = m_ClientDriver.CreatePipeline(typeof(TestInvertPipelineStage));
            var serverPipe = m_ServerDriver.CreatePipeline(typeof(TestInvertPipelineStage));
            Assert.AreEqual(clientPipe, serverPipe);

            // Connect to server
            var clientToServer = m_ClientDriver.Connect(m_ServerDriver.LocalEndPoint());
            Assert.AreNotEqual(default(NetworkConnection), clientToServer);
            m_ClientDriver.ScheduleUpdate().Complete();

            // Handle incoming connection from client
            m_ServerDriver.ScheduleUpdate().Complete();
            var serverToClient = m_ServerDriver.Accept();
            Assert.AreNotEqual(default(NetworkConnection), serverToClient);

            // Send message to client
            var strm = new DataStreamWriter(4, Allocator.Temp);
            strm.Write((int) 42);
            m_ServerDriver.Send(serverPipe, serverToClient, strm);
            m_ServerDriver.ScheduleUpdate().Complete();

            // Receive incoming message from server
            m_ClientDriver.ScheduleUpdate().Complete();
            DataStreamReader readStrm;
            Assert.AreEqual(NetworkEvent.Type.Connect, clientToServer.PopEvent(m_ClientDriver, out readStrm));
            Assert.AreEqual(NetworkEvent.Type.Data, clientToServer.PopEvent(m_ClientDriver, out readStrm));
            Assert.AreEqual(4, readStrm.Length);
            var readCtx = default(DataStreamReader.Context);
            Assert.AreEqual(-1^42, readStrm.ReadInt(ref readCtx));
        }

        [Test]
        public void NetworkPipeline_MultiplePipelinesWork()
        {
            var clientPipe = m_ClientDriver.CreatePipeline(typeof(TestPipelineStageWithHeaderTwo), typeof(TestEncryptPipelineStage));
            var serverPipe = m_ServerDriver.CreatePipeline(typeof(TestPipelineStageWithHeaderTwo), typeof(TestEncryptPipelineStage));
            Assert.AreEqual(clientPipe, serverPipe);

            // Connect to server
            var clientToServer = m_ClientDriver.Connect(m_ServerDriver.LocalEndPoint());
            Assert.AreNotEqual(default(NetworkConnection), clientToServer);
            m_ClientDriver.ScheduleUpdate().Complete();

            // Handle incoming connection from client
            m_ServerDriver.ScheduleUpdate().Complete();
            var serverToClient = m_ServerDriver.Accept();
            Assert.AreNotEqual(default(NetworkConnection), serverToClient);

            // Send message to client
            var strm = new DataStreamWriter(4, Allocator.Temp);
            strm.Write((int) 42);
            m_ServerDriver.Send(serverPipe, serverToClient, strm);
            m_ServerDriver.ScheduleUpdate().Complete();

            // Receive incoming message from server
            m_ClientDriver.ScheduleUpdate().Complete();
            DataStreamReader readStrm;
            Assert.AreEqual(NetworkEvent.Type.Connect, clientToServer.PopEvent(m_ClientDriver, out readStrm));
            Assert.AreEqual(NetworkEvent.Type.Data, clientToServer.PopEvent(m_ClientDriver, out readStrm));
            Assert.AreEqual(4, readStrm.Length);
            var readCtx = default(DataStreamReader.Context);
            Assert.AreEqual(42, readStrm.ReadInt(ref readCtx));
        }

        [Test]
        public void NetworkPipeline_CanStorePacketsForLaterDeliveryInReceiveLastStage()
        {
            var clientPipe1 = m_ClientDriver.CreatePipeline(typeof(TestEncryptPipelineStage), typeof(SimulatorPipelineStage));
            var clientPipe2 = m_ClientDriver2.CreatePipeline(typeof(TestEncryptPipelineStage), typeof(SimulatorPipelineStage));
            var serverPipe = m_ServerDriver.CreatePipeline(typeof(TestEncryptPipelineStage), typeof(SimulatorPipelineStage));
            Assert.AreEqual(clientPipe1, serverPipe);
            Assert.AreEqual(clientPipe2, serverPipe);

            TestPipeline(30, serverPipe);
        }

        [Test]
        public void NetworkPipeline_CanStorePacketsForLaterDeliveryInReceiveFirstStage()
        {
            var clientPipe1 = m_ClientDriver.CreatePipeline(typeof(SimulatorPipelineStage), typeof(TestEncryptPipelineStage));
            var clientPipe2 = m_ClientDriver2.CreatePipeline(typeof(SimulatorPipelineStage), typeof(TestEncryptPipelineStage));
            var serverPipe = m_ServerDriver.CreatePipeline(typeof(SimulatorPipelineStage), typeof(TestEncryptPipelineStage));
            Assert.AreEqual(clientPipe1, serverPipe);
            Assert.AreEqual(clientPipe2, serverPipe);

            TestPipeline(30, serverPipe);
        }

        [Test]
        public void NetworkPipeline_CanStorePacketsForLaterDeliveryInSendLastStage()
        {
            var clientPipe1 = m_ClientDriver.CreatePipeline(typeof(TestEncryptPipelineStage), typeof(SimulatorPipelineStageInSend));
            var clientPipe2 = m_ClientDriver2.CreatePipeline(typeof(TestEncryptPipelineStage), typeof(SimulatorPipelineStageInSend));
            var serverPipe = m_ServerDriver.CreatePipeline(typeof(TestEncryptPipelineStage), typeof(SimulatorPipelineStageInSend));
            Assert.AreEqual(clientPipe1, serverPipe);
            Assert.AreEqual(clientPipe2, serverPipe);

            TestPipeline(30, serverPipe);
        }

        [Test]
        public void NetworkPipeline_CanStorePacketsForLaterDeliveryInSendFirstStage()
        {
            var clientPipe1 = m_ClientDriver.CreatePipeline(typeof(SimulatorPipelineStageInSend), typeof(TestEncryptPipelineStage));
            var clientPipe2 = m_ClientDriver2.CreatePipeline(typeof(SimulatorPipelineStageInSend), typeof(TestEncryptPipelineStage));
            var serverPipe = m_ServerDriver.CreatePipeline(typeof(SimulatorPipelineStageInSend), typeof(TestEncryptPipelineStage));
            Assert.AreEqual(clientPipe1, serverPipe);
            Assert.AreEqual(clientPipe2, serverPipe);

            TestPipeline(30, serverPipe);
        }

        [Test]
        public void NetworkPipeline_CanStoreSequencedPacketsForLaterDeliveryInSendLastStage()
        {
            // Server needs the simulator as it's the only one sending
            var clientPipe1 = m_ClientDriver.CreatePipeline(typeof(UnreliableSequencedPipelineStage));
            var clientPipe2 = m_ClientDriver2.CreatePipeline(typeof(UnreliableSequencedPipelineStage));
            var serverPipe = m_ServerDriver.CreatePipeline(typeof(UnreliableSequencedPipelineStage), typeof(SimulatorPipelineStageInSend));
            Assert.AreEqual(clientPipe1, serverPipe);
            Assert.AreEqual(clientPipe2, serverPipe);

            TestPipeline(30, serverPipe);
        }

        [Test]
        public void NetworkPipeline_CanStoreSequencedPacketsForLaterDeliveryInSendFirstStage()
        {
            // Server needs the simulator as it's the only one sending
            var clientPipe1 = m_ClientDriver.CreatePipeline(typeof(UnreliableSequencedPipelineStage));
            var clientPipe2 = m_ClientDriver2.CreatePipeline(typeof(UnreliableSequencedPipelineStage));
            var serverPipe = m_ServerDriver.CreatePipeline(typeof(SimulatorPipelineStageInSend), typeof(UnreliableSequencedPipelineStage));
            Assert.AreEqual(clientPipe1, serverPipe);
            Assert.AreEqual(clientPipe2, serverPipe);

            TestPipeline(30, serverPipe);
        }

        [Test]
        public void NetworkPipeline_CanStoreSequencedPacketsForLaterDeliveryInReceiveLastStage()
        {
            var clientPipe1 = m_ClientDriver.CreatePipeline(typeof(UnreliableSequencedPipelineStage), typeof(SimulatorPipelineStage));
            var clientPipe2 = m_ClientDriver2.CreatePipeline(typeof(UnreliableSequencedPipelineStage), typeof(SimulatorPipelineStage));
            var serverPipe = m_ServerDriver.CreatePipeline(typeof(UnreliableSequencedPipelineStage));
            Assert.AreEqual(clientPipe1, serverPipe);
            Assert.AreEqual(clientPipe2, serverPipe);

            TestPipeline(30, serverPipe);
        }

        [Test]
        public void NetworkPipeline_CanStoreSequencedPacketsForLaterDeliveryInReceiveFirstStage()
        {
            var clientPipe1 = m_ClientDriver.CreatePipeline(typeof(SimulatorPipelineStage), typeof(UnreliableSequencedPipelineStage));
            var clientPipe2 = m_ClientDriver2.CreatePipeline(typeof(SimulatorPipelineStage), typeof(UnreliableSequencedPipelineStage));
            var serverPipe = m_ServerDriver.CreatePipeline(typeof(UnreliableSequencedPipelineStage));
            Assert.AreEqual(clientPipe1, serverPipe);
            Assert.AreEqual(clientPipe2, serverPipe);

            TestPipeline(30, serverPipe);
        }

        [Test]
        public void NetworkPipeline_MultiplePipelinesWithHeadersWork()
        {
            m_ClientDriver.CreatePipeline(typeof(TestPipelineStageWithHeader), typeof(TestPipelineStageWithHeaderTwo));
            m_ClientDriver2.CreatePipeline(typeof(TestPipelineStageWithHeader), typeof(TestPipelineStageWithHeaderTwo));
            var serverPipe = m_ServerDriver.CreatePipeline(typeof(TestPipelineStageWithHeader), typeof(TestPipelineStageWithHeaderTwo));
            TestPipeline(30, serverPipe, 0);
        }

        [Test]
        public void NetworkPipeline_MultiplePipelinesWithHeadersWorkWithSimulator()
        {
            //m_ClientDriver.CreatePipeline(typeof(TestPipelineStageWithHeader), typeof(SimulatorPipelineStage), typeof(TestPipelineStageWithHeaderTwo));
            //m_ClientDriver2.CreatePipeline(typeof(SimulatorPipelineStage), typeof(TestPipelineStageWithHeader), typeof(TestPipelineStageWithHeaderTwo));
            m_ClientDriver.CreatePipeline(typeof(TestPipelineStageWithHeader), typeof(TestPipelineStageWithHeaderTwo));
            m_ClientDriver2.CreatePipeline(typeof(TestPipelineStageWithHeader), typeof(TestPipelineStageWithHeaderTwo));
            var serverPipe = m_ServerDriver.CreatePipeline(typeof(SimulatorPipelineStageInSend), typeof(TestPipelineStageWithHeader), typeof(TestPipelineStageWithHeaderTwo));
            TestPipeline(30, serverPipe);
        }

        [Test]
        public void NetworkPipeline_MuliplePipelinesWithInitializers()
        {
            m_ClientDriver.CreatePipeline(typeof(TestPipelineWithInitializers), typeof(TestPipelineWithInitializersTwo));
            m_ClientDriver2.CreatePipeline(typeof(TestPipelineWithInitializers), typeof(TestPipelineWithInitializersTwo));
            var serverPipe = m_ServerDriver.CreatePipeline(typeof(TestPipelineWithInitializers), typeof(TestPipelineWithInitializersTwo));
            TestPipeline(30, serverPipe, 0);
        }

        [Test]
        public void NetworkPipeline_MuliplePipelinesWithInitializersAndSimulator()
        {
            m_ClientDriver.CreatePipeline(typeof(TestPipelineWithInitializers), typeof(SimulatorPipelineStage), typeof(TestPipelineWithInitializersTwo));
            m_ClientDriver2.CreatePipeline(typeof(TestPipelineWithInitializers), typeof(TestPipelineWithInitializersTwo), typeof(SimulatorPipelineStage));
            var serverPipe = m_ServerDriver.CreatePipeline(typeof(SimulatorPipelineStageInSend), typeof(TestPipelineWithInitializers), typeof(TestPipelineWithInitializersTwo));
            TestPipeline(30, serverPipe);
        }

        private void TestPipeline(int packetCount, NetworkPipeline serverPipe, int packetDelay = 100)
        {
            // Connect to server
            var clientToServer = m_ClientDriver.Connect(m_ServerDriver.LocalEndPoint());
            var clientToServer2 = m_ClientDriver2.Connect(m_ServerDriver.LocalEndPoint());
            Assert.AreNotEqual(default(NetworkConnection), clientToServer);
            Assert.AreNotEqual(default(NetworkConnection), clientToServer2);
            m_ClientDriver.ScheduleUpdate().Complete();
            m_ClientDriver2.ScheduleUpdate().Complete();

            // Driver only updates time in update, so must read start time before update
            var startTime = Stopwatch.GetTimestamp() / TimeSpan.TicksPerMillisecond;
            // Handle incoming connection from client
            m_ServerDriver.ScheduleUpdate().Complete();
            var serverToClient = m_ServerDriver.Accept();
            Assert.AreNotEqual(default(NetworkConnection), serverToClient);
            var serverToClient2 = m_ServerDriver.Accept();
            Assert.AreNotEqual(default(NetworkConnection), serverToClient2);

            // Send given packetCount number of packets in a row in one update
            // Write 1's for packet 1, 2's for packet 2 and so on and verify they're received in same order
            var strm = new DataStreamWriter(64, Allocator.Temp);
            for (int i = 0; i < packetCount; i++)
            {
                strm.Clear();
                for (int j = 0; j < 16; j++)
                    strm.Write((int) i + 1);
                m_ServerDriver.Send(serverPipe, serverToClient, strm);
                m_ServerDriver.Send(serverPipe, serverToClient2, strm);
            }

            m_ServerDriver.ScheduleUpdate().Complete();

            // Receive incoming message from server
            m_ClientDriver.ScheduleUpdate().Complete();
            m_ClientDriver2.ScheduleUpdate().Complete();
            DataStreamReader readStrm;
            Assert.AreEqual(NetworkEvent.Type.Connect, clientToServer.PopEvent(m_ClientDriver, out readStrm));
            Assert.AreEqual(NetworkEvent.Type.Connect, clientToServer.PopEvent(m_ClientDriver2, out readStrm));

            ClientReceivePackets(m_ClientDriver, packetCount, clientToServer, startTime, packetDelay);
            ClientReceivePackets(m_ClientDriver2, packetCount, clientToServer2, startTime, packetDelay);
        }

        private void ClientReceivePackets(GenericNetworkDriver<IPCSocket, TestNetworkPipelineStageCollection> clientDriver, int packetCount, NetworkConnection clientToServer, long startTime, int minDelay)
        {
            DataStreamReader readStrm;
            NetworkEvent.Type netEvent;
            var abortTimer = new Timer();
            while (true)
            {
                if (abortTimer.ElapsedMilliseconds > 2000)
                    Assert.Fail("Did not receive first delayed packet");
                netEvent = clientToServer.PopEvent(clientDriver, out readStrm);
                if (netEvent == NetworkEvent.Type.Data)
                    break;
                m_ServerDriver.ScheduleUpdate().Complete();
                clientDriver.ScheduleUpdate().Complete();
            }

            // All delayed packets (from first patch) should be poppable now
            for (int i = 0; i < packetCount; i++)
            {
                var delay = Stopwatch.GetTimestamp() / TimeSpan.TicksPerMillisecond - startTime;
                Assert.AreEqual(NetworkEvent.Type.Data, netEvent);
                Assert.That(delay >= minDelay, "Delay too low on packet " + i + ": " + delay);
                Assert.AreEqual(64, readStrm.Length);
                var readCtx = default(DataStreamReader.Context);
                for (int j = 0; j < 16; j++)
                {
                    var read = readStrm.ReadInt(ref readCtx);
                    Assert.AreEqual(i + 1, read);
                    Assert.True(read > 0 && read <= packetCount, "read incorrect value: " + read);
                }

                // Test done when all packets have been verified
                if (i == packetCount - 1)
                    break;

                // It could be not all patch of packets were processed in one update (depending on how the timers land)
                abortTimer = new Timer();
                while ((netEvent = clientToServer.PopEvent(clientDriver, out readStrm)) == NetworkEvent.Type.Empty)
                {
                    if (abortTimer.ElapsedMilliseconds > 1000)
                        Assert.Fail("Didn't receive all delayed packets");
                    clientDriver.ScheduleUpdate().Complete();
                    m_ServerDriver.ScheduleUpdate().Complete();
                }
            }
        }
    }
}

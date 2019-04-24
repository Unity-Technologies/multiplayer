using System;
using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Networking.Transport.Utilities;
using UnityEngine;

namespace Unity.Networking.Transport.Tests
{
    public class ReliablePipelineTests
    {
        [Test]
        public unsafe void ReliableUtility_ValidationScenarios()
        {
            // Receive a Packet Newer still gapped. [0, 1, Lost, 3, 4]
            // Massage the resend flow using the Received Mask. [0, 1, Resend, 3, 4]
            // Receive the missing packet '2' and massage the receive flow
            
            ReliableUtility.Parameters parameters = new ReliableUtility.Parameters
            {
                WindowSize = 32
            };

            var processCapacity = ReliableUtility.ProcessCapacityNeeded(parameters);
            var sharedCapacity = ReliableUtility.SharedCapacityNeeded(parameters);
            
            
            // ep1
            var ep1SharedBuffer = new NativeArray<byte>(sharedCapacity, Allocator.Persistent);
            var ep1SendBuffer = new NativeArray<byte>(processCapacity, Allocator.Persistent);
            var ep1RecvBuffer = new NativeArray<byte>(processCapacity, Allocator.Persistent);
            
            // ep2
            var ep2SharedBuffer = new NativeArray<byte>(sharedCapacity, Allocator.Persistent);
            var ep2SendBuffer = new NativeArray<byte>(processCapacity, Allocator.Persistent);
            var ep2RecvBuffer = new NativeArray<byte>(processCapacity, Allocator.Persistent);
            
            // packet
            var packet = new NativeArray<byte>(UnsafeUtility.SizeOf<ReliableUtility.Packet>(), Allocator.Persistent);
            packet[0] = 100;

            var header = new DataStreamWriter(UnsafeUtility.SizeOf<ReliableUtility.PacketHeader>(), Allocator.Persistent);
            
            ReliableSequencedPipelineStage ep1 = new ReliableSequencedPipelineStage();
            ReliableSequencedPipelineStage ep2 = new ReliableSequencedPipelineStage();

            ep1.Initialize(parameters);
            ep2.Initialize(parameters);
            ep1.InitializeConnection(ep1SendBuffer, ep1RecvBuffer, ep1SharedBuffer);
            ep2.InitializeConnection(ep2SendBuffer, ep2RecvBuffer, ep2SharedBuffer);

            var ep1sendContext = (ReliableUtility.Context*) ep1SendBuffer.GetUnsafePtr();
            //var ep1recvContext = (ReliableUtility.Context*) ep1RecvBuffer.GetUnsafePtr();
            //var ep1sharedContext = (ReliableUtility.SharedContext*) ep1SharedBuffer.GetUnsafePtr();

            var ep2recvContext = (ReliableUtility.Context*) ep2RecvBuffer.GetUnsafePtr();
            //var ep2sendContext = (ReliableUtility.Context*) ep2SendBuffer.GetUnsafePtr();
            //var ep2sharedContext = (ReliableUtility.SharedContext*) ep2SharedBuffer.GetUnsafePtr();
            
            // Send a Packet - Receive a Packet
            var currentId = 0;

            var inboundSend = default(InboundBufferVec);
            inboundSend.buffer1 = packet;

            bool needsUpdate = false;
            bool needsResume = false;
            bool needsSendUpdate = false;
            var slice = default(NativeSlice<byte>);
            var output = default(InboundBufferVec);
            {
                output = ep1.Send(new NetworkPipelineContext
                {
                    header = header,
                    internalProcessBuffer = ep1SendBuffer,
                    internalSharedProcessBuffer = ep1SharedBuffer
                }, inboundSend, ref needsResume, ref needsUpdate);
                Assert.True(output.buffer1[0] == packet[0]);
                Assert.True(!needsResume);
            }
            {
                var info = ReliableUtility.GetPacketInformation(ep1SendBuffer, currentId);
                var offset = ep1sendContext->DataPtrOffset; // + (index * ctx->DataStride);
                NativeSlice<byte> data = new NativeSlice<byte>(ep1SendBuffer, offset, info->Size);


                slice = ep2.Receive(new NetworkPipelineContext
                {
                    internalProcessBuffer = ep2RecvBuffer,
                    internalSharedProcessBuffer = ep2SharedBuffer
                }, data, ref needsResume, ref needsUpdate, ref needsSendUpdate);
                
                if (slice.Length > 0)
                    Assert.True(slice[0] == packet[0]);
            } 
            Assert.True(!needsResume);
            Assert.True(ep2recvContext->Delivered == currentId);
            
            // Scenario: Receive a Packet Newer then expected [0, 1, Lost, 3]
            
            // Start by "sending" 1, 2, 3;
            for (int seq = currentId + 1; seq < 4; seq++)
            {
                packet[0] = (byte) (100 + seq);
                
                header.Clear();
                output = ep1.Send(new NetworkPipelineContext
                {
                    header = header,
                    internalProcessBuffer = ep1SendBuffer,
                    internalSharedProcessBuffer = ep1SharedBuffer
                }, inboundSend, ref needsResume, ref needsUpdate);
                
                Assert.True(!needsResume);
                Assert.True(output.buffer1[0] == packet[0]);
            }
            
            for (int seq = currentId + 1; seq < 4; seq++)
            {
                if (seq == 2)
                    continue;
                
                var info = ReliableUtility.GetPacketInformation(ep1SendBuffer, seq);
                var offset = ep1sendContext->DataPtrOffset + ((seq % ep1sendContext->Capacity) * ep1sendContext->DataStride);
                var inspectPacket = ReliableUtility.GetPacket(ep1SendBuffer, seq);
                
                NativeSlice<byte> data = new NativeSlice<byte>(ep1SendBuffer, offset, info->Size);
                Assert.True(inspectPacket->Header.SequenceId == info->SequenceId);
                
                header.Clear();
                slice = ep2.Receive(new NetworkPipelineContext
                {
                    header = header,
                    internalProcessBuffer = ep2RecvBuffer,
                    internalSharedProcessBuffer = ep2SharedBuffer
                }, data, ref needsResume, ref needsUpdate, ref needsSendUpdate);

                if (slice.Length > 0)
                {
                    Assert.True(slice[0] == seq + 100);
                }
            }
            
            // Receive packet number 2 and resume received packets.
            bool first = true;
            do
            {
                var data = default(NativeSlice<byte>);
                if (first)
                {
                    var seq = 2;
                    var info = ReliableUtility.GetPacketInformation(ep1SendBuffer, seq);
                    var offset = ep1sendContext->DataPtrOffset +
                                 ((seq % ep1sendContext->Capacity) * ep1sendContext->DataStride);
                    var inspectPacket = ReliableUtility.GetPacket(ep1SendBuffer, seq);

                    data = new NativeSlice<byte>(ep1SendBuffer, offset, info->Size);
                    Assert.True(inspectPacket->Header.SequenceId == info->SequenceId);

                    first = false;
                }

                slice = ep2.Receive(new NetworkPipelineContext
                {
                    internalProcessBuffer = ep2RecvBuffer,
                    internalSharedProcessBuffer = ep2SharedBuffer
                }, data, ref needsResume, ref needsUpdate, ref needsSendUpdate);

                if (slice.Length > 0)
                {
                    Assert.True(slice[0] == ep2recvContext->Delivered + 100);
                }
            } while (needsResume);
            
            
            packet.Dispose();
            header.Dispose();
            ep1SharedBuffer.Dispose();
            ep1SendBuffer.Dispose();
            ep1RecvBuffer.Dispose();
            ep2SharedBuffer.Dispose();
            ep2SendBuffer.Dispose();
            ep2RecvBuffer.Dispose();
        }
        
        
        [Test]
        public unsafe void ReliableUtility_Validation()
        {
            int capacity = 5;
            NativeArray<byte> buffer = new NativeArray<byte>(1, Allocator.Persistent);
            ReliableUtility.Parameters parameters = new ReliableUtility.Parameters
            {
                WindowSize = capacity
            };
            
            int result = ReliableUtility.ProcessCapacityNeeded(parameters);
            NativeArray<byte> processBuffer = new NativeArray<byte>(result, Allocator.Persistent);

            ReliableUtility.InitializeProcessContext(processBuffer, parameters);
            
            Assert.IsTrue(ReliableUtility.TryAquire(processBuffer, 0));
            Assert.IsTrue(ReliableUtility.TryAquire(processBuffer, 1));
            Assert.IsTrue(ReliableUtility.TryAquire(processBuffer, 2));
            Assert.IsTrue(ReliableUtility.TryAquire(processBuffer, 3));
            Assert.IsTrue(ReliableUtility.TryAquire(processBuffer, 4));
            Assert.IsFalse(ReliableUtility.TryAquire(processBuffer, 5));

            ReliableUtility.Release(processBuffer, 0, 5);
            
            Assert.IsTrue(ReliableUtility.TryAquire(processBuffer, 0));
            Assert.IsTrue(ReliableUtility.TryAquire(processBuffer, 1));
            Assert.IsTrue(ReliableUtility.TryAquire(processBuffer, 2));
            Assert.IsTrue(ReliableUtility.TryAquire(processBuffer, 3));
            Assert.IsTrue(ReliableUtility.TryAquire(processBuffer, 4));

            buffer[0] = (byte)(1);

            ReliableUtility.SetPacket(processBuffer, 0, buffer);
            
            
            var slice = ReliableUtility.GetPacket(processBuffer, 0);
            Assert.IsTrue(slice->Buffer[0] == buffer[0]);
            
            for (int i = 0; i < capacity * 5; i++)
            {
                ReliableUtility.SetPacket(processBuffer, i, buffer);
                slice = ReliableUtility.GetPacket(processBuffer, i);
                Assert.IsTrue(slice->Buffer[0] == buffer[0]);
            }
            ReliableUtility.Release(processBuffer, 0, 5);
            
            processBuffer.Dispose();
            buffer.Dispose();
        }

        [Test]
        public unsafe void ReliableUtility_AckPackets_SeqIdBeginAt0()
        {
            ReliableUtility.Parameters parameters = new ReliableUtility.Parameters
            {
                WindowSize = 10
            };

            int processCapacity = ReliableUtility.ProcessCapacityNeeded(parameters);
            int sharedCapacity = ReliableUtility.SharedCapacityNeeded(parameters);
            NativeArray<byte> recvBuffer = new NativeArray<byte>(processCapacity, Allocator.Persistent);
            NativeArray<byte> sendBuffer = new NativeArray<byte>(processCapacity, Allocator.Persistent);
            NativeArray<byte> sharedBuffer = new NativeArray<byte>(sharedCapacity, Allocator.Persistent);

            ReliableUtility.InitializeContext(sharedBuffer, sendBuffer, recvBuffer, parameters);

            var pipelineContext = new NetworkPipelineContext
                {internalProcessBuffer = sendBuffer, internalSharedProcessBuffer = sharedBuffer, timestamp = 1000};

            // Sending seqId 3, last received ID 0 (1 is not yet acked, 2 was dropped)
            var sharedContext = (ReliableUtility.SharedContext*) sharedBuffer.GetUnsafePtr();
            sharedContext->SentPackets.Sequence = 0;    // Last sent is initialized to what you are sending next
            sharedContext->SentPackets.Acked = -1;
            sharedContext->SentPackets.AckMask = 0x1;
            sharedContext->ReceivedPackets.Sequence = sharedContext->SentPackets.Acked;
            sharedContext->ReceivedPackets.AckMask = sharedContext->SentPackets.AckMask;
            var receiveContext = (ReliableUtility.Context*) recvBuffer.GetUnsafePtr();
            receiveContext->Delivered = sharedContext->SentPackets.Acked;

            using (var stream = new DataStreamWriter(4, Allocator.Temp))
            {
                // Add buffers in resend queue
                stream.Write((int) 10);
                ReliableUtility.SetPacket(sendBuffer, 65535, stream.GetNativeSlice(0, stream.Length));
                ReliableUtility.GetPacketInformation(sendBuffer, 65535)->SendTime = 980;
                ReliableUtility.StoreTimestamp(pipelineContext.internalSharedProcessBuffer, 65535, 980);
                ReliableUtility.StoreReceiveTimestamp(pipelineContext.internalSharedProcessBuffer, 65535, 990, 16);
                stream.Clear();
                stream.Write((int) 11);
                ReliableUtility.SetPacket(sendBuffer, 0, stream.GetNativeSlice(0, stream.Length));
                ReliableUtility.GetPacketInformation(sendBuffer, 0)->SendTime = 990;
                ReliableUtility.StoreTimestamp(pipelineContext.internalSharedProcessBuffer, 0, 990);

                ReliableUtility.ReleaseOrResumePackets(pipelineContext);
                Assert.AreEqual((ReliableUtility.ErrorCodes)0, sharedContext->errorCode);

                // Validate that packet tracking state is correct, 65535 should be released, 0 should still be there
                Assert.AreEqual(-1, ReliableUtility.GetPacketInformation(sendBuffer, 65535)->SequenceId);
                Assert.AreEqual(0, ReliableUtility.GetPacketInformation(sendBuffer, 0)->SequenceId);
            }
            recvBuffer.Dispose();
            sendBuffer.Dispose();
            sharedBuffer.Dispose();
        }

        [Test]
        public unsafe void ReliableUtility_AckPackets_SeqIdWrap1()
        {
            ReliableUtility.Parameters parameters = new ReliableUtility.Parameters
            {
                WindowSize = 10
            };

            int processCapacity = ReliableUtility.ProcessCapacityNeeded(parameters);
            int sharedCapacity = ReliableUtility.SharedCapacityNeeded(parameters);
            NativeArray<byte> recvBuffer = new NativeArray<byte>(processCapacity, Allocator.Persistent);
            NativeArray<byte> sendBuffer = new NativeArray<byte>(processCapacity, Allocator.Persistent);
            NativeArray<byte> sharedBuffer = new NativeArray<byte>(sharedCapacity, Allocator.Persistent);

            ReliableUtility.InitializeContext(sharedBuffer, sendBuffer, recvBuffer, parameters);

            var pipelineContext = new NetworkPipelineContext
                {internalProcessBuffer = sendBuffer, internalSharedProcessBuffer = sharedBuffer, timestamp = 1000};

            // Sending seqId 3, last received ID 2 (same as last sent packet)
            var sharedContext = (ReliableUtility.SharedContext*) sharedBuffer.GetUnsafePtr();
            sharedContext->SentPackets.Sequence = 3;
            sharedContext->SentPackets.Acked = 2;
            sharedContext->SentPackets.AckMask = 0xFFFFFFFF;
            sharedContext->ReceivedPackets.Sequence = sharedContext->SentPackets.Acked;
            sharedContext->ReceivedPackets.AckMask = sharedContext->SentPackets.AckMask;
            var receiveContext = (ReliableUtility.Context*) recvBuffer.GetUnsafePtr();
            receiveContext->Delivered = sharedContext->SentPackets.Acked;

            using (var stream = new DataStreamWriter(4, Allocator.Temp))
            {
                // Add buffers in resend queue
                stream.Write((int) 10);
                ReliableUtility.SetPacket(sendBuffer, 1, stream.GetNativeSlice(0, stream.Length));
                ReliableUtility.GetPacketInformation(sendBuffer, 1)->SendTime = 980;
                ReliableUtility.StoreTimestamp(pipelineContext.internalSharedProcessBuffer, 1, 980);
                ReliableUtility.StoreReceiveTimestamp(pipelineContext.internalSharedProcessBuffer, 1, 990, 16);
                stream.Clear();
                stream.Write((int) 11);
                ReliableUtility.SetPacket(sendBuffer, 2, stream.GetNativeSlice(0, stream.Length));
                ReliableUtility.GetPacketInformation(sendBuffer, 2)->SendTime = 990;
                ReliableUtility.StoreTimestamp(pipelineContext.internalSharedProcessBuffer, 2, 990);
                ReliableUtility.StoreReceiveTimestamp(pipelineContext.internalSharedProcessBuffer, 2, 1000, 16);

                ReliableUtility.ReleaseOrResumePackets(pipelineContext);
                Assert.AreEqual((ReliableUtility.ErrorCodes)0, sharedContext->errorCode);

                // Validate that packet tracking state is correct, both packets should be released
                Assert.AreEqual(-1, ReliableUtility.GetPacketInformation(sendBuffer, 1)->SequenceId);
                Assert.AreEqual(-1, ReliableUtility.GetPacketInformation(sendBuffer, 2)->SequenceId);
            }
            recvBuffer.Dispose();
            sendBuffer.Dispose();
            sharedBuffer.Dispose();
        }

        [Test]
        public unsafe void ReliableUtility_AckPackets_SeqIdWrap2()
        {
            ReliableUtility.Parameters parameters = new ReliableUtility.Parameters
            {
                WindowSize = 10
            };

            int processCapacity = ReliableUtility.ProcessCapacityNeeded(parameters);
            int sharedCapacity = ReliableUtility.SharedCapacityNeeded(parameters);
            NativeArray<byte> recvBuffer = new NativeArray<byte>(processCapacity, Allocator.Persistent);
            NativeArray<byte> sendBuffer = new NativeArray<byte>(processCapacity, Allocator.Persistent);
            NativeArray<byte> sharedBuffer = new NativeArray<byte>(sharedCapacity, Allocator.Persistent);

            ReliableUtility.InitializeContext(sharedBuffer, sendBuffer, recvBuffer, parameters);

            var pipelineContext = new NetworkPipelineContext
                {internalProcessBuffer = sendBuffer, internalSharedProcessBuffer = sharedBuffer};

            // Sending seqId 3, last received ID 65535 (same as last sent)
            var sharedContext = (ReliableUtility.SharedContext*) sharedBuffer.GetUnsafePtr();
            sharedContext->SentPackets.Sequence = 0;
            sharedContext->SentPackets.Acked = 65535;
            sharedContext->SentPackets.AckMask = 0xFFFFFFFF;
            sharedContext->ReceivedPackets.Sequence = sharedContext->SentPackets.Acked;
            sharedContext->ReceivedPackets.AckMask = sharedContext->SentPackets.AckMask;
            var receiveContext = (ReliableUtility.Context*) recvBuffer.GetUnsafePtr();
            receiveContext->Delivered = sharedContext->SentPackets.Acked;

            using (var stream = new DataStreamWriter(4, Allocator.Temp))
            {
                // Add buffers in resend queue
                stream.Write((int) 10);
                ReliableUtility.SetPacket(sendBuffer, 65535, stream.GetNativeSlice(0, stream.Length));
                ReliableUtility.GetPacketInformation(sendBuffer, 65535)->SendTime = 980;
                ReliableUtility.StoreTimestamp(pipelineContext.internalSharedProcessBuffer, 65535, 980);
                ReliableUtility.StoreReceiveTimestamp(pipelineContext.internalSharedProcessBuffer, 65535, 990, 16);

                ReliableUtility.ReleaseOrResumePackets(pipelineContext);
                Assert.AreEqual((ReliableUtility.ErrorCodes)0, sharedContext->errorCode);

                // Validate that packet tracking state is correct, 65535 should be released
                Assert.AreEqual(-1, ReliableUtility.GetPacketInformation(sendBuffer, 65535)->SequenceId);
            }
            recvBuffer.Dispose();
            sendBuffer.Dispose();
            sharedBuffer.Dispose();
        }

        [Test]
        public unsafe void ReliableUtility_AckPackets_SeqIdWrap3()
        {
            ReliableUtility.Parameters parameters = new ReliableUtility.Parameters
            {
                WindowSize = 10
            };

            int processCapacity = ReliableUtility.ProcessCapacityNeeded(parameters);
            int sharedCapacity = ReliableUtility.SharedCapacityNeeded(parameters);
            NativeArray<byte> recvBuffer = new NativeArray<byte>(processCapacity, Allocator.Persistent);
            NativeArray<byte> sendBuffer = new NativeArray<byte>(processCapacity, Allocator.Persistent);
            NativeArray<byte> sharedBuffer = new NativeArray<byte>(sharedCapacity, Allocator.Persistent);

            ReliableUtility.InitializeContext(sharedBuffer, sendBuffer, recvBuffer, parameters);

            var pipelineContext = new NetworkPipelineContext
                {internalProcessBuffer = sendBuffer, internalSharedProcessBuffer = sharedBuffer};

            // Sending seqId 3, last received ID 0 (1 is not yet acked, 2 was dropped)
            var sharedContext = (ReliableUtility.SharedContext*) sharedBuffer.GetUnsafePtr();
            sharedContext->SentPackets.Sequence = 17;
            sharedContext->SentPackets.Acked = 16;
            sharedContext->SentPackets.AckMask = 0xFFFFDBB7;
            sharedContext->ReceivedPackets.Sequence = sharedContext->SentPackets.Acked;
            sharedContext->ReceivedPackets.AckMask = sharedContext->SentPackets.AckMask;
            var receiveContext = (ReliableUtility.Context*) recvBuffer.GetUnsafePtr();
            receiveContext->Delivered = sharedContext->SentPackets.Acked;

            using (var stream = new DataStreamWriter(4, Allocator.Temp))
            {
                // Add buffers in resend queue
                stream.Write((int) 10);
                ReliableUtility.SetPacket(sendBuffer, 16, stream.GetNativeSlice(0, stream.Length));

                ReliableUtility.ReleaseOrResumePackets(pipelineContext);
                Assert.AreEqual((ReliableUtility.ErrorCodes)0, sharedContext->errorCode);

                // Validate that packet tracking state is correct, packet 16 should be released
                Assert.AreEqual(-1, ReliableUtility.GetPacketInformation(sendBuffer, 16)->SequenceId);
            }
            recvBuffer.Dispose();
            sendBuffer.Dispose();
            sharedBuffer.Dispose();
        }

        [Test]
        public unsafe void ReliableUtility_AckPackets_ReleaseSlotWithWrappedSeqId()
        {
            ReliableUtility.Parameters parameters = new ReliableUtility.Parameters
            {
                WindowSize = 10
            };

            int processCapacity = ReliableUtility.ProcessCapacityNeeded(parameters);
            int sharedCapacity = ReliableUtility.SharedCapacityNeeded(parameters);
            NativeArray<byte> recvBuffer = new NativeArray<byte>(processCapacity, Allocator.Persistent);
            NativeArray<byte> sendBuffer = new NativeArray<byte>(processCapacity, Allocator.Persistent);
            NativeArray<byte> sharedBuffer = new NativeArray<byte>(sharedCapacity, Allocator.Persistent);

            ReliableUtility.InitializeContext(sharedBuffer, sendBuffer, recvBuffer, parameters);

            var pipelineContext = new NetworkPipelineContext
                {internalProcessBuffer = sendBuffer, internalSharedProcessBuffer = sharedBuffer};

            // Sending seqId 3, last received ID 0 (1 is not yet acked, 2 was dropped)
            var sharedContext = (ReliableUtility.SharedContext*) sharedBuffer.GetUnsafePtr();
            sharedContext->SentPackets.Sequence = 1;
            sharedContext->SentPackets.Acked = 0;
            sharedContext->SentPackets.AckMask = 0xFFFFFFFF;
            sharedContext->ReceivedPackets.Sequence = sharedContext->SentPackets.Acked;
            sharedContext->ReceivedPackets.AckMask = sharedContext->SentPackets.AckMask;
            var receiveContext = (ReliableUtility.Context*) recvBuffer.GetUnsafePtr();
            receiveContext->Delivered = sharedContext->SentPackets.Acked;

            using (var stream = new DataStreamWriter(4, Allocator.Temp))
            {
                // Add buffers in resend queue
                stream.Write((int) 10);
                ReliableUtility.SetPacket(sendBuffer, 0, stream.GetNativeSlice(0, stream.Length));
                stream.Clear();
                stream.Write((int) 11);
                ReliableUtility.SetPacket(sendBuffer, 65535, stream.GetNativeSlice(0, stream.Length));

                ReliableUtility.ReleaseOrResumePackets(pipelineContext);
                Assert.AreEqual((ReliableUtility.ErrorCodes)0, sharedContext->errorCode);

                // Validate that packet tracking state is correct, slot with seqId 0 and 65535 should have been released
                Assert.AreEqual(-1, ReliableUtility.GetPacketInformation(sendBuffer, 0)->SequenceId);
                Assert.AreEqual(-1, ReliableUtility.GetPacketInformation(sendBuffer, 65535)->SequenceId);
            }
            recvBuffer.Dispose();
            sendBuffer.Dispose();
            sharedBuffer.Dispose();
        }

        [Test]
        public unsafe void ReliableUtility_AckPackets_AckMaskShiftsProperly1()
        {
            ReliableUtility.Parameters parameters = new ReliableUtility.Parameters
            {
                WindowSize = 10
            };

            int processCapacity = ReliableUtility.ProcessCapacityNeeded(parameters);
            int sharedCapacity = ReliableUtility.SharedCapacityNeeded(parameters);
            NativeArray<byte> recvBuffer = new NativeArray<byte>(processCapacity, Allocator.Persistent);
            NativeArray<byte> sendBuffer = new NativeArray<byte>(processCapacity, Allocator.Persistent);
            NativeArray<byte> sharedBuffer = new NativeArray<byte>(sharedCapacity, Allocator.Persistent);

            ReliableUtility.InitializeContext(sharedBuffer, sendBuffer, recvBuffer, parameters);

            var pipelineContext = new NetworkPipelineContext
                {internalProcessBuffer = sendBuffer, internalSharedProcessBuffer = sharedBuffer, timestamp = 1000};

            // Sending seqId 3, last received ID 0 (1 is not yet acked, 2 was dropped)
            var sharedContext = (ReliableUtility.SharedContext*) sharedBuffer.GetUnsafePtr();
            sharedContext->SentPackets.Sequence = 4;
            sharedContext->SentPackets.Acked = 3;
            sharedContext->SentPackets.AckMask = 0xFFFFFFFD;    // bit 0 = seqId 3 (1), bit 1 = seqId 2 (0)
            sharedContext->ReceivedPackets.Sequence = sharedContext->SentPackets.Acked;
            sharedContext->ReceivedPackets.AckMask = sharedContext->SentPackets.AckMask;
            var receiveContext = (ReliableUtility.Context*) recvBuffer.GetUnsafePtr();
            receiveContext->Delivered = sharedContext->SentPackets.Acked;

            using (var stream = new DataStreamWriter(4, Allocator.Temp))
            {
                // Add buffers in resend queue
                // SeqId 3 is received and ready to be released
                stream.Write((int) 10);
                ReliableUtility.SetPacket(sendBuffer, 3, stream.GetNativeSlice(0, stream.Length));
                ReliableUtility.GetPacketInformation(sendBuffer, 3)->SendTime = 990;
                ReliableUtility.StoreTimestamp(pipelineContext.internalSharedProcessBuffer, 3, 980);
                ReliableUtility.StoreReceiveTimestamp(pipelineContext.internalSharedProcessBuffer, 3, 990, 16);
                stream.Clear();
                // SeqId 2 is not yet received so it should stick around
                stream.Write((int) 11);
                ReliableUtility.SetPacket(sendBuffer, 2, stream.GetNativeSlice(0, stream.Length));
                ReliableUtility.GetPacketInformation(sendBuffer, 2)->SendTime = 1000;
                ReliableUtility.StoreTimestamp(pipelineContext.internalSharedProcessBuffer, 2, 1000);

                ReliableUtility.ReleaseOrResumePackets(pipelineContext);
                Assert.AreEqual((ReliableUtility.ErrorCodes)0, sharedContext->errorCode);

                // Validate that packet tracking state is correct, packet 3 should be released (has been acked), 2 should stick around
                Assert.AreEqual(-1, ReliableUtility.GetPacketInformation(sendBuffer, 3)->SequenceId);
                Assert.AreEqual(2, ReliableUtility.GetPacketInformation(sendBuffer, 2)->SequenceId);
            }
            recvBuffer.Dispose();
            sendBuffer.Dispose();
            sharedBuffer.Dispose();
        }

        [Test]
        public unsafe void ReliableUtility_AckPackets_AckMaskShiftsProperly2()
        {
            ReliableUtility.Parameters parameters = new ReliableUtility.Parameters
            {
                WindowSize = 10
            };

            int processCapacity = ReliableUtility.ProcessCapacityNeeded(parameters);
            int sharedCapacity = ReliableUtility.SharedCapacityNeeded(parameters);
            NativeArray<byte> recvBuffer = new NativeArray<byte>(processCapacity, Allocator.Persistent);
            NativeArray<byte> sendBuffer = new NativeArray<byte>(processCapacity, Allocator.Persistent);
            NativeArray<byte> sharedBuffer = new NativeArray<byte>(sharedCapacity, Allocator.Persistent);

            ReliableUtility.InitializeContext(sharedBuffer, sendBuffer, recvBuffer, parameters);

            var pipelineContext = new NetworkPipelineContext
                {internalProcessBuffer = sendBuffer, internalSharedProcessBuffer = sharedBuffer, timestamp =  1000};

            // Sending seqId 3, last received ID 0 (1 is not yet acked, 2 was dropped)
            var sharedContext = (ReliableUtility.SharedContext*) sharedBuffer.GetUnsafePtr();
            sharedContext->SentPackets.Sequence = 5;
            sharedContext->SentPackets.Acked = 4;
            sharedContext->SentPackets.AckMask = 0xFFFFFFFD;    // bit 0 = seqId 4 (1), bit 1 = seqId 3 (0)
            sharedContext->ReceivedPackets.Sequence = sharedContext->SentPackets.Acked;
            sharedContext->ReceivedPackets.AckMask = sharedContext->SentPackets.AckMask;
            var receiveContext = (ReliableUtility.Context*) recvBuffer.GetUnsafePtr();
            receiveContext->Delivered = sharedContext->SentPackets.Acked;

            using (var stream = new DataStreamWriter(4, Allocator.Temp))
            {
                // Add buffers in resend queue
                // SeqId 4 is received and ready to be released
                stream.Write((int) 10);
                ReliableUtility.SetPacket(sendBuffer, 4, stream.GetNativeSlice(0, stream.Length));
                ReliableUtility.GetPacketInformation(sendBuffer, 4)->SendTime = 980;
                ReliableUtility.StoreTimestamp(pipelineContext.internalSharedProcessBuffer, 4, 980);
                ReliableUtility.StoreReceiveTimestamp(pipelineContext.internalSharedProcessBuffer, 4, 990, 16);
                stream.Clear();
                stream.Write((int) 11);
                ReliableUtility.SetPacket(sendBuffer, 3, stream.GetNativeSlice(0, stream.Length));
                ReliableUtility.GetPacketInformation(sendBuffer, 3)->SendTime = 1000;
                ReliableUtility.StoreTimestamp(pipelineContext.internalSharedProcessBuffer, 3, 1000);

                ReliableUtility.ReleaseOrResumePackets(pipelineContext);
                Assert.AreEqual((ReliableUtility.ErrorCodes)0, sharedContext->errorCode);

                // Validate that packet tracking state is correct, packet 3 should be released (has been acked), 2 should stick around
                Assert.AreEqual(-1, ReliableUtility.GetPacketInformation(sendBuffer, 4)->SequenceId);
                Assert.AreEqual(3, ReliableUtility.GetPacketInformation(sendBuffer, 3)->SequenceId);
            }
            recvBuffer.Dispose();
            sendBuffer.Dispose();
            sharedBuffer.Dispose();
        }

        [Test]
        public unsafe void ReliableUtility_TimestampHandling()
        {
            ReliableUtility.Parameters parameters = new ReliableUtility.Parameters
            {
                WindowSize = 3
            };

            int processCapacity = ReliableUtility.ProcessCapacityNeeded(parameters);
            int sharedCapacity = ReliableUtility.SharedCapacityNeeded(parameters);
            NativeArray<byte> ep1RecvBuffer = new NativeArray<byte>(processCapacity, Allocator.Persistent);
            NativeArray<byte> ep1SendBuffer = new NativeArray<byte>(processCapacity, Allocator.Persistent);
            NativeArray<byte> ep1SharedBuffer = new NativeArray<byte>(sharedCapacity, Allocator.Persistent);
            NativeArray<byte> ep2RecvBuffer = new NativeArray<byte>(processCapacity, Allocator.Persistent);
            NativeArray<byte> ep2SendBuffer = new NativeArray<byte>(processCapacity, Allocator.Persistent);
            NativeArray<byte> ep2SharedBuffer = new NativeArray<byte>(sharedCapacity, Allocator.Persistent);

            ReliableUtility.InitializeContext(ep1SharedBuffer, ep1SendBuffer, ep1RecvBuffer, parameters);
            ReliableUtility.InitializeContext(ep2SharedBuffer, ep2SendBuffer, ep2RecvBuffer, parameters);

            // When sending we store the send timestamp of the sequence ID (EP1 -> EP2)
            ushort ep1SeqId = 10;
            ReliableUtility.StoreTimestamp(ep1SharedBuffer, ep1SeqId, 900);

            // EP2 also sends something to EP1
            ushort ep2SeqId = 100;
            ReliableUtility.StoreTimestamp(ep2SharedBuffer, ep2SeqId, 910);

            // When EP2 receives the packet the receive time is stored
            ReliableUtility.StoreRemoteReceiveTimestamp(ep2SharedBuffer, ep1SeqId, 920);

            // EP2 also stores the timing information in the EP1 packet (processing time for the packet it sent earlier)
            ReliableUtility.StoreReceiveTimestamp(ep2SharedBuffer, ep2SeqId, 920, 10);

            // When EP2 sends another packet to EP1 it calculates ep1SeqId processing time
            int processTime = ReliableUtility.CalculateProcessingTime(ep2SharedBuffer, ep1SeqId, 930);

            // ep1SeqId processing time should be 10 ms (930 - 920)
            Assert.AreEqual(10, processTime);

            // Verify information written so far (send/receive times + processing time)
            var timerData = ReliableUtility.GetLocalPacketTimer(ep2SharedBuffer, ep2SeqId);
            Assert.IsTrue(timerData != null, "Packet timing data not found");
            Assert.AreEqual(ep2SeqId, timerData->SequenceId);
            Assert.AreEqual(10, timerData->ProcessingTime);
            Assert.AreEqual(910, timerData->SentTime);
            Assert.AreEqual(920, timerData->ReceiveTime);

            var ep2SharedCtx = (ReliableUtility.SharedContext*) ep2SharedBuffer.GetUnsafePtr();
            Debug.Log("LastRtt=" + ep2SharedCtx->RttInfo.LastRtt);
            Debug.Log("SmoothedRTT=" + ep2SharedCtx->RttInfo.SmoothedRtt);
            Debug.Log("ResendTimeout=" + ep2SharedCtx->RttInfo.ResendTimeout);
            Debug.Log("SmoothedVariance=" + ep2SharedCtx->RttInfo.SmoothedVariance);

            ep1RecvBuffer.Dispose();
            ep1SendBuffer.Dispose();
            ep1SharedBuffer.Dispose();
            ep2RecvBuffer.Dispose();
            ep2SendBuffer.Dispose();
            ep2SharedBuffer.Dispose();
        }

        [Test]
        public unsafe void Receive_ResumesMultipleStoredPacketsAroundWrapPoint1()
        {
            ReliableUtility.Parameters parameters = new ReliableUtility.Parameters
            {
                WindowSize = 10
            };

            int processCapacity = ReliableUtility.ProcessCapacityNeeded(parameters);
            int sharedCapacity = ReliableUtility.SharedCapacityNeeded(parameters);
            NativeArray<byte> recvBuffer = new NativeArray<byte>(processCapacity, Allocator.Persistent);
            NativeArray<byte> sendBuffer = new NativeArray<byte>(processCapacity, Allocator.Persistent);
            NativeArray<byte> sharedBuffer = new NativeArray<byte>(sharedCapacity, Allocator.Persistent);

            ReliableUtility.InitializeContext(new NativeSlice<byte>(sharedBuffer), new NativeSlice<byte>(sendBuffer), new NativeSlice<byte>(recvBuffer), parameters);

            var pipelineContext = new NetworkPipelineContext
                {internalProcessBuffer = recvBuffer, internalSharedProcessBuffer = sharedBuffer};

            var sharedContext = (ReliableUtility.SharedContext*) sharedBuffer.GetUnsafePtr();
            sharedContext->SentPackets.Sequence = 3; // what was last sent doesn't matter here
            sharedContext->SentPackets.Acked = 2;
            sharedContext->SentPackets.AckMask = 0xFFFFFFF7;    // bit 0,1,2 maps to seqId 2,1,0 all delivered, bit 3 is seqId 65535 which is not yet delivered
            sharedContext->ReceivedPackets.Sequence = sharedContext->SentPackets.Acked;
            sharedContext->ReceivedPackets.AckMask = sharedContext->SentPackets.AckMask;
            var receiveContext = (ReliableUtility.Context*) recvBuffer.GetUnsafePtr();
            receiveContext->Delivered = 65534;    // latest in sequence delivered packet, one less than what unclogs the packet jam

            var reliablePipeline = new ReliableSequencedPipelineStage();

            using (var stream = new DataStreamWriter(4, Allocator.Temp))
            using (var inboundStream = new DataStreamWriter(4, Allocator.Temp))
            {
                // Add buffers to receive queue, packets which should be resume received after packet jam is unclogged
                stream.Clear();
                stream.Write((int) 100);
                ReliableUtility.SetPacket(recvBuffer, 0, stream.GetNativeSlice(0, stream.Length));
                stream.Clear();
                stream.Write((int) 200);
                ReliableUtility.SetPacket(recvBuffer, 1, stream.GetNativeSlice(0, stream.Length));
                stream.Clear();
                stream.Write((int) 300);
                ReliableUtility.SetPacket(recvBuffer, 2, stream.GetNativeSlice(0, stream.Length));

                // Generate the packet which will be handled in receive
                NativeSlice<byte> packet = default;
                GeneratePacket(9000, 2, 0xFFFFFFFF, 65535, ref sendBuffer, out packet);

                bool needsResume = false;
                bool needsUpdate = false;
                bool needsSendUpdate = false;
                // Process 65535, 0 should then be next in line on the resume field
                reliablePipeline.Receive(pipelineContext, packet, ref needsResume, ref needsUpdate, ref needsSendUpdate);
                Assert.AreEqual((ReliableUtility.ErrorCodes)0, sharedContext->errorCode);
                Assert.AreEqual(0, receiveContext->Resume);
                Assert.IsTrue(needsResume);
                // Process 0, after that 1 is up
                reliablePipeline.Receive(pipelineContext, packet, ref needsResume, ref needsUpdate, ref needsSendUpdate);
                Assert.AreEqual((ReliableUtility.ErrorCodes)0, sharedContext->errorCode);
                Assert.AreEqual(1, receiveContext->Resume);
                Assert.IsTrue(needsResume);
                // Process 1, after that 2 is up
                reliablePipeline.Receive(pipelineContext, packet, ref needsResume, ref needsUpdate, ref needsSendUpdate);
                Assert.AreEqual((ReliableUtility.ErrorCodes)0, sharedContext->errorCode);
                Assert.AreEqual(2, receiveContext->Resume);
                Assert.IsTrue(needsResume);
                // Process 2, and we are done
                reliablePipeline.Receive(pipelineContext, packet, ref needsResume, ref needsUpdate, ref needsSendUpdate);
                Assert.AreEqual((ReliableUtility.ErrorCodes)0, sharedContext->errorCode);
                Assert.AreEqual(-1, receiveContext->Resume);
                Assert.IsFalse(needsResume);
            }
            recvBuffer.Dispose();
            sendBuffer.Dispose();
            sharedBuffer.Dispose();
        }

        [Test]
        public unsafe void Receive_ResumesMultipleStoredPacketsAroundWrapPoint2()
        {
            ReliableUtility.Parameters parameters = new ReliableUtility.Parameters
            {
                WindowSize = 10
            };

            int processCapacity = ReliableUtility.ProcessCapacityNeeded(parameters);
            int sharedCapacity = ReliableUtility.SharedCapacityNeeded(parameters);
            NativeArray<byte> recvBuffer = new NativeArray<byte>(processCapacity, Allocator.Persistent);
            NativeArray<byte> sendBuffer = new NativeArray<byte>(processCapacity, Allocator.Persistent);
            NativeArray<byte> sharedBuffer = new NativeArray<byte>(sharedCapacity, Allocator.Persistent);

            ReliableUtility.InitializeContext(new NativeSlice<byte>(sharedBuffer), new NativeSlice<byte>(sendBuffer), new NativeSlice<byte>(recvBuffer), parameters);

            var pipelineContext = new NetworkPipelineContext
                {internalProcessBuffer = recvBuffer, internalSharedProcessBuffer = sharedBuffer};

            var sharedContext = (ReliableUtility.SharedContext*) sharedBuffer.GetUnsafePtr();
            sharedContext->SentPackets.Sequence = 2; // what was last sent doesn't matter here
            sharedContext->SentPackets.Acked = 1;
            sharedContext->SentPackets.AckMask = 0xFFFFFFF7;    // bit 0,1,2 maps to seqId 1,0,65535 all delivered, bit 3 is seqId 65534 which is not yet delivered
            sharedContext->ReceivedPackets.Sequence = 1;
            sharedContext->ReceivedPackets.AckMask = 0xFFFFFFF7;
            var receiveContext = (ReliableUtility.Context*) recvBuffer.GetUnsafePtr();
            receiveContext->Delivered = 65533;    // latest in sequence delivered packet, one less than what unclogs the packet jam

            var reliablePipeline = new ReliableSequencedPipelineStage();

            using (var stream = new DataStreamWriter(4, Allocator.Temp))
            {
                // Add buffers to receive queue, packets which should be resume received after packet jam is unclogged
                stream.Clear();
                stream.Write((int) 100);
                ReliableUtility.SetPacket(recvBuffer, 65535, stream.GetNativeSlice(0, stream.Length));
                stream.Clear();
                stream.Write((int) 200);
                ReliableUtility.SetPacket(recvBuffer, 0, stream.GetNativeSlice(0, stream.Length));
                stream.Clear();
                stream.Write((int) 300);
                ReliableUtility.SetPacket(recvBuffer, 1, stream.GetNativeSlice(0, stream.Length));

                // Generate the packet which will be handled in receive
                NativeSlice<byte> packet = default;
                GeneratePacket(9000, 65533, 0xFFFFFFFF, 65534, ref sendBuffer, out packet);

                bool needsResume = false;
                bool needsUpdate = false;
                bool needsSendUpdate = false;
                // Process 65534, 65535 should then be next in line on the resume field
                reliablePipeline.Receive(pipelineContext, packet, ref needsResume, ref needsUpdate, ref needsSendUpdate);
                Assert.AreEqual((ReliableUtility.ErrorCodes)0, sharedContext->errorCode);
                Assert.AreEqual(65535, receiveContext->Resume);
                Assert.IsTrue(needsResume);
                // Process 65535, after that 0 is up
                reliablePipeline.Receive(pipelineContext, packet, ref needsResume, ref needsUpdate, ref needsSendUpdate);
                Assert.AreEqual((ReliableUtility.ErrorCodes)0, sharedContext->errorCode);
                Assert.AreEqual(0, receiveContext->Resume);
                Assert.IsTrue(needsResume);
                // Process 0, after that 1 is up
                reliablePipeline.Receive(pipelineContext, packet, ref needsResume, ref needsUpdate, ref needsSendUpdate);
                Assert.AreEqual((ReliableUtility.ErrorCodes)0, sharedContext->errorCode);
                Assert.AreEqual(1, receiveContext->Resume);
                Assert.IsTrue(needsResume);
                // Process 1, and we are done
                reliablePipeline.Receive(pipelineContext, packet, ref needsResume, ref needsUpdate, ref needsSendUpdate);
                Assert.AreEqual((ReliableUtility.ErrorCodes)0, sharedContext->errorCode);
                Assert.AreEqual(-1, receiveContext->Resume);
                Assert.IsFalse(needsResume);
            }
            recvBuffer.Dispose();
            sendBuffer.Dispose();
            sharedBuffer.Dispose();
        }

        [Test]
        public unsafe void Receive_ResumesMultipleStoredPacketsAndSetsAckedAckMaskProperly()
        {
            ReliableUtility.Parameters parameters = new ReliableUtility.Parameters
            {
                WindowSize = 10
            };

            int processCapacity = ReliableUtility.ProcessCapacityNeeded(parameters);
            int sharedCapacity = ReliableUtility.SharedCapacityNeeded(parameters);
            NativeArray<byte> recvBuffer = new NativeArray<byte>(processCapacity, Allocator.Persistent);
            NativeArray<byte> sendBuffer = new NativeArray<byte>(processCapacity, Allocator.Persistent);
            NativeArray<byte> sharedBuffer = new NativeArray<byte>(sharedCapacity, Allocator.Persistent);

            ReliableUtility.InitializeContext(new NativeSlice<byte>(sharedBuffer), new NativeSlice<byte>(sendBuffer), new NativeSlice<byte>(recvBuffer), parameters);

            var pipelineContext = new NetworkPipelineContext
                {internalProcessBuffer = recvBuffer, internalSharedProcessBuffer = sharedBuffer};

            var sharedContext = (ReliableUtility.SharedContext*) sharedBuffer.GetUnsafePtr();
            sharedContext->SentPackets.Sequence = 99;           // what was last sent doesn't matter here
            sharedContext->SentPackets.Acked = 97;
            sharedContext->SentPackets.AckMask = 0xFFFFFFFF;
            sharedContext->ReceivedPackets.Sequence = 98;
            sharedContext->ReceivedPackets.AckMask = 0xFFFFFFF7;
            var receiveContext = (ReliableUtility.Context*) recvBuffer.GetUnsafePtr();
            receiveContext->Delivered = 94;    // latest in sequence delivered packet, one less than what unclogs the packet jam

            var reliablePipeline = new ReliableSequencedPipelineStage();

            using (var stream = new DataStreamWriter(4, Allocator.Temp))
            {
                // Add buffers to receive queue, packets which should be resume received after packet jam is unclogged
                stream.Clear();
                stream.Write((int) 200);
                ReliableUtility.SetPacket(recvBuffer, 96, stream.GetNativeSlice(0, stream.Length));
                stream.Clear();
                stream.Write((int) 300);
                ReliableUtility.SetPacket(recvBuffer, 97, stream.GetNativeSlice(0, stream.Length));
                stream.Clear();
                stream.Write((int) 300);
                ReliableUtility.SetPacket(recvBuffer, 98, stream.GetNativeSlice(0, stream.Length));

                bool needsResume = false;
                bool needsUpdate = false;
                bool needsSendUpdate = false;
                NativeSlice<byte> packet = default;
                GeneratePacket(9000, 98, 0xFFFFFFFF, 99, ref sendBuffer, out packet);

                // Receive 99, it's out of order so should be queued for later (waiting for 95)
                reliablePipeline.Receive(pipelineContext, packet, ref needsResume, ref needsUpdate, ref needsSendUpdate);
                Assert.AreEqual((ReliableUtility.ErrorCodes)0, sharedContext->errorCode);
                Assert.AreEqual(-1, receiveContext->Resume);
                Assert.IsFalse(needsResume);

                GeneratePacket(10000, 98, 0xFFFFFFFF, 95, ref sendBuffer, out packet);

                // First 95 is received and then receive resume runs up to 99
                reliablePipeline.Receive(pipelineContext, packet, ref needsResume, ref needsUpdate, ref needsSendUpdate);
                Assert.AreEqual((ReliableUtility.ErrorCodes)0, sharedContext->errorCode);
                Assert.AreEqual(96, receiveContext->Resume);
                Assert.IsTrue(needsResume);
                reliablePipeline.Receive(pipelineContext, packet, ref needsResume, ref needsUpdate, ref needsSendUpdate);
                Assert.AreEqual((ReliableUtility.ErrorCodes)0, sharedContext->errorCode);
                Assert.AreEqual(97, receiveContext->Resume);
                Assert.IsTrue(needsResume);
                reliablePipeline.Receive(pipelineContext, packet, ref needsResume, ref needsUpdate, ref needsSendUpdate);
                Assert.AreEqual((ReliableUtility.ErrorCodes)0, sharedContext->errorCode);
                Assert.AreEqual(98, receiveContext->Resume);
                Assert.IsTrue(needsResume);
                reliablePipeline.Receive(pipelineContext, packet, ref needsResume, ref needsUpdate, ref needsSendUpdate);
                Assert.AreEqual((ReliableUtility.ErrorCodes)0, sharedContext->errorCode);
                Assert.AreEqual(99, receiveContext->Resume);
                Assert.IsTrue(needsResume);
                reliablePipeline.Receive(pipelineContext, packet, ref needsResume, ref needsUpdate, ref needsSendUpdate);
                Assert.AreEqual((ReliableUtility.ErrorCodes)0, sharedContext->errorCode);
                Assert.AreEqual(-1, receiveContext->Resume);
                Assert.IsFalse(needsResume);

                // Verify that the ReceivePackets state is correct, 99 should be latest received and ackmask 0xFFFFF
                Assert.AreEqual(99, sharedContext->ReceivedPackets.Sequence);
                Assert.AreEqual(0xFFFFFFFF, sharedContext->ReceivedPackets.AckMask);
            }
            recvBuffer.Dispose();
            sendBuffer.Dispose();
            sharedBuffer.Dispose();
        }

        [Test]
        public unsafe void Send_PacketsAreAcked_SendingPacket()
        {
            ReliableUtility.Parameters parameters = new ReliableUtility.Parameters
            {
                WindowSize = 3
            };

            int processCapacity = ReliableUtility.ProcessCapacityNeeded(parameters);
            int sharedCapacity = ReliableUtility.SharedCapacityNeeded(parameters);
            NativeArray<byte> recvBuffer = new NativeArray<byte>(processCapacity, Allocator.Persistent);
            NativeArray<byte> sendBuffer = new NativeArray<byte>(processCapacity, Allocator.Persistent);
            NativeArray<byte> sharedBuffer = new NativeArray<byte>(sharedCapacity, Allocator.Persistent);

            ReliableUtility.InitializeContext(new NativeSlice<byte>(sharedBuffer), new NativeSlice<byte>(sendBuffer), new NativeSlice<byte>(recvBuffer), parameters);

            var pipelineContext = new NetworkPipelineContext
                {internalProcessBuffer = sendBuffer, internalSharedProcessBuffer = sharedBuffer};

            var sharedContext = (ReliableUtility.SharedContext*) sharedBuffer.GetUnsafePtr();
            sharedContext->SentPackets.Sequence = 3;
            sharedContext->SentPackets.Acked = 2;
            sharedContext->SentPackets.AckMask = 0xFFFFFFFF;
            sharedContext->ReceivedPackets.Sequence = 2;
            sharedContext->ReceivedPackets.AckMask = 0xFFFFFFFF;
            var receiveContext = (ReliableUtility.Context*) recvBuffer.GetUnsafePtr();
            receiveContext->Delivered = 1;

            var reliablePipeline = new ReliableSequencedPipelineStage();

            using (var stream = new DataStreamWriter(4, Allocator.Temp))
            using (pipelineContext.header = new DataStreamWriter(UnsafeUtility.SizeOf<ReliableUtility.PacketHeader>(), Allocator.Persistent))
            {
                // Fill window capacity, next send should then clear everything
                stream.Clear();
                stream.Write((int) 100);
                ReliableUtility.SetPacket(sendBuffer, 0, stream.GetNativeSlice(0, stream.Length));
                stream.Clear();
                stream.Write((int) 200);
                ReliableUtility.SetPacket(sendBuffer, 1, stream.GetNativeSlice(0, stream.Length));
                stream.Clear();
                stream.Write((int) 300);
                ReliableUtility.SetPacket(sendBuffer, 2, stream.GetNativeSlice(0, stream.Length));

                // Set input buffer and send, this will be seqId 3
                stream.Clear();
                stream.Write((int) 9000);
                var inboundBuffer = new InboundBufferVec();
                inboundBuffer.buffer1 = stream.GetNativeSlice(0, stream.Length);
                inboundBuffer.buffer2 = default;

                bool needsResume = false;
                bool needsUpdate = false;
                reliablePipeline.Send(pipelineContext, inboundBuffer, ref needsResume, ref needsUpdate);

                // seqId 3 should now be stored in slot 0
                Assert.AreEqual(3, ReliableUtility.GetPacketInformation(sendBuffer, 3)->SequenceId);

                // slots 1 and 2 should be cleared
                Assert.AreEqual(-1, ReliableUtility.GetPacketInformation(sendBuffer, 1)->SequenceId);
                Assert.AreEqual(-1, ReliableUtility.GetPacketInformation(sendBuffer, 2)->SequenceId);

                Assert.IsFalse(needsResume);
                Assert.IsTrue(needsUpdate);

                // Verify ack packet is written correctly
                ReliableUtility.PacketHeader header = default;
                ReliableUtility.WriteAckPacket(pipelineContext, ref header);
                Assert.AreEqual(header.AckedSequenceId, 2);
                Assert.AreEqual(header.AckMask, 0xFFFFFFFF);
            }
            recvBuffer.Dispose();
            sendBuffer.Dispose();
            sharedBuffer.Dispose();
        }

        [Test]
        public unsafe void Send_PacketsAreAcked_UpdateAckState()
        {
            ReliableUtility.Parameters parameters = new ReliableUtility.Parameters
            {
                WindowSize = 3
            };

            int processCapacity = ReliableUtility.ProcessCapacityNeeded(parameters);
            int sharedCapacity = ReliableUtility.SharedCapacityNeeded(parameters);
            NativeArray<byte> recvBuffer = new NativeArray<byte>(processCapacity, Allocator.Persistent);
            NativeArray<byte> sendBuffer = new NativeArray<byte>(processCapacity, Allocator.Persistent);
            NativeArray<byte> sharedBuffer = new NativeArray<byte>(sharedCapacity, Allocator.Persistent);

            ReliableUtility.InitializeContext(new NativeSlice<byte>(sharedBuffer), new NativeSlice<byte>(sendBuffer), new NativeSlice<byte>(recvBuffer), parameters);

            var pipelineContext = new NetworkPipelineContext
                {internalProcessBuffer = sendBuffer, internalSharedProcessBuffer = sharedBuffer, timestamp = 1000};

            var sharedContext = (ReliableUtility.SharedContext*) sharedBuffer.GetUnsafePtr();
            sharedContext->SentPackets.Sequence = 3;
            sharedContext->SentPackets.Acked = 2;
            sharedContext->SentPackets.AckMask = 0xFFFFFFFF;
            sharedContext->ReceivedPackets.Sequence = 2;
            sharedContext->ReceivedPackets.AckMask = 0xFFFFFFFF;
            var receiveContext = (ReliableUtility.Context*) recvBuffer.GetUnsafePtr();
            receiveContext->Delivered = 1;

            // Set last send time to something a long time ago so the ack state is sent in Send
            var sendContext = (ReliableUtility.Context*) sendBuffer.GetUnsafePtr();
            sendContext->LastSentTime = 500;
            sendContext->PreviousTimestamp = 980;    // 20 ms ago

            var reliablePipeline = new ReliableSequencedPipelineStage();

            using (var stream = new DataStreamWriter(4, Allocator.Temp))
            using (pipelineContext.header = new DataStreamWriter(UnsafeUtility.SizeOf<ReliableUtility.PacketHeader>(), Allocator.Persistent))
            {
                // Fill window capacity, next send should then clear everything
                stream.Clear();
                stream.Write((int) 100);
                ReliableUtility.SetPacket(sendBuffer, 0, stream.GetNativeSlice(0, stream.Length));
                stream.Clear();
                stream.Write((int) 200);
                ReliableUtility.SetPacket(sendBuffer, 1, stream.GetNativeSlice(0, stream.Length));
                stream.Clear();
                stream.Write((int) 300);
                ReliableUtility.SetPacket(sendBuffer, 2, stream.GetNativeSlice(0, stream.Length));

                var inboundBuffer = new InboundBufferVec();
                inboundBuffer.buffer1 = default;
                inboundBuffer.buffer2 = default;

                bool needsResume = false;
                bool needsUpdate = false;
                reliablePipeline.Send(pipelineContext, inboundBuffer, ref needsResume, ref needsUpdate);

                Assert.AreEqual(-1, ReliableUtility.GetPacketInformation(sendBuffer, 0)->SequenceId);
                Assert.AreEqual(-1, ReliableUtility.GetPacketInformation(sendBuffer, 1)->SequenceId);
                Assert.AreEqual(-1, ReliableUtility.GetPacketInformation(sendBuffer, 2)->SequenceId);

                Assert.IsFalse(needsResume);
                Assert.IsTrue(needsUpdate);
            }
            recvBuffer.Dispose();
            sendBuffer.Dispose();
            sharedBuffer.Dispose();
        }

        unsafe void GeneratePacket(int payload, ushort headerAckedId, uint headerAckMask, ushort headerSeqId, ref NativeArray<byte> sendBuffer, out NativeSlice<byte> packet)
        {
            DataStreamWriter inboundStream = new DataStreamWriter(4, Allocator.Temp);

            inboundStream.Write((int) payload);
            InboundBufferVec data = default;
            data.buffer1 = inboundStream.GetNativeSlice(0, inboundStream.Length);
            ReliableUtility.PacketHeader header = new ReliableUtility.PacketHeader()
            {
                AckedSequenceId = headerAckedId,
                AckMask = headerAckMask,
                SequenceId = headerSeqId
            };
            ReliableUtility.SetHeaderAndPacket(sendBuffer, headerSeqId, header, data, 1000);

            // Extract raw packet from the send buffer so it can be passed directly to receive
            var sendCtx = (ReliableUtility.Context*) sendBuffer.GetUnsafePtr();
            var index = headerSeqId % sendCtx->Capacity;
            var offset = sendCtx->DataPtrOffset + (index * sendCtx->DataStride);
            packet = new NativeSlice<byte>(sendBuffer, offset, sendCtx->DataStride);
            inboundStream.Dispose();
        }
    }

    public class QoSNetworkPipelineTest
    {
        private LocalNetworkDriver m_ServerDriver;
        private LocalNetworkDriver m_ClientDriver;

        [SetUp]
        public void IPC_Setup()
        {
            IPCManager.Instance.Initialize(100);
            m_ServerDriver =
                new LocalNetworkDriver(new NetworkDataStreamParameter
                    {size = 0},
                    new ReliableUtility.Parameters { WindowSize = 32});
            m_ServerDriver.Bind(IPCManager.Instance.CreateEndPoint());
            m_ServerDriver.Listen();
            m_ClientDriver =
                new LocalNetworkDriver(new NetworkDataStreamParameter
                    {size = 0},
                    new ReliableUtility.Parameters { WindowSize = 32},
                    new SimulatorUtility.Parameters { MaxPacketCount = 30, MaxPacketSize = 16, PacketDelayMs = 0, /*PacketDropInterval = 8,*/ PacketDropPercentage = 10});
        }

        [TearDown]
        public void IPC_TearDown()
        {
            m_ClientDriver.Dispose();
            m_ServerDriver.Dispose();
            IPCManager.Instance.Destroy();
        }

        [Test]
        public void NetworkPipeline_ReliableSequenced_SendRecvOnce()
        {
            var clientPipe = m_ClientDriver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
            var serverPipe = m_ServerDriver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
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
        public unsafe void NetworkPipeline_ReliableSequenced_SendRecvWithRTTCalculation()
        {
            var clientPipe = m_ClientDriver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
            var serverPipe = m_ServerDriver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
            var clientToServer = m_ClientDriver.Connect(m_ServerDriver.LocalEndPoint());
            m_ClientDriver.ScheduleUpdate().Complete();
            m_ServerDriver.ScheduleUpdate().Complete();
            var serverToClient = m_ServerDriver.Accept();

            NativeSlice<byte> serverReceiveBuffer = default;
            NativeSlice<byte> serverSendBuffer = default;
            NativeSlice<byte> serverSharedBuffer = default;
            m_ServerDriver.GetPipelineBuffers(serverPipe, 4, serverToClient, ref serverReceiveBuffer, ref serverSendBuffer, ref serverSharedBuffer);
            var sharedContext = (ReliableUtility.SharedContext*) serverSharedBuffer.GetUnsafePtr();

            NativeSlice<byte> clientReceiveBuffer = default;
            NativeSlice<byte> clientSendBuffer = default;
            NativeSlice<byte> clientSharedBuffer = default;
            m_ClientDriver.GetPipelineBuffers(clientPipe, 4, clientToServer, ref clientReceiveBuffer, ref clientSendBuffer, ref clientSharedBuffer);

            // First the server sends a packet to the client
            var strm = new DataStreamWriter(4, Allocator.Temp);
            strm.Write((int) 42);
            m_ServerDriver.Send(serverPipe, serverToClient, strm);
            m_ServerDriver.ScheduleUpdate().Complete();

            // Server sent time for the packet with seqId=0 is set
            var serverPacketTimer = ReliableUtility.GetLocalPacketTimer(serverSharedBuffer, 0);
            Assert.IsTrue(serverPacketTimer->SentTime > 0);

            m_ClientDriver.ScheduleUpdate().Complete();

            // Client received seqId=0 from server and sets the receive time
            var clientPacketTimer = ReliableUtility.GetRemotePacketTimer(clientSharedBuffer, 0);
            Assert.IsTrue(clientPacketTimer->ReceiveTime >= serverPacketTimer->SentTime);

            DataStreamReader readStrm;
            Assert.AreEqual(NetworkEvent.Type.Connect, clientToServer.PopEvent(m_ClientDriver, out readStrm));
            Assert.AreEqual(NetworkEvent.Type.Data, clientToServer.PopEvent(m_ClientDriver, out readStrm));

            // Force processing time to be at least 20 ms,
            var timer = new Timer();
            while (timer.ElapsedMilliseconds < 20) { }
            // Now update client, if it's updated in the while loop it will automatically send ack packets to the server
            // so processing time will actually be recorded as almost 0
            m_ClientDriver.ScheduleUpdate().Complete();

            // Now client sends packet to the server, this should contain the ackedSeqId=0 for the servers initial packet
            strm.Clear();
            strm.Write((int) 9000);
            m_ClientDriver.Send(clientPipe, clientToServer, strm);
            m_ClientDriver.ScheduleUpdate().Complete();

            // Receive time for the server packet is 0 at this point
            Assert.AreEqual(serverPacketTimer->ReceiveTime, 0);

            // Packet is now processed, receive+processing time recorded
            m_ServerDriver.ScheduleUpdate().Complete();

            // Server has now received a packet from the client with ackedSeqId=0 in the header and timing info for that
            Assert.IsTrue(serverPacketTimer->ReceiveTime >= clientPacketTimer->ReceiveTime);
            Assert.IsTrue(serverPacketTimer->ProcessingTime >= 20);
        }

        [Test]
        public void NetworkPipeline_ReliableSequenced_SendRecvMany()
        {
            var clientPipe = m_ClientDriver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
            var serverPipe = m_ServerDriver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
            Assert.AreEqual(clientPipe, serverPipe);

            // Connect to server
            var clientToServer = m_ClientDriver.Connect(m_ServerDriver.LocalEndPoint());
            Assert.AreNotEqual(default(NetworkConnection), clientToServer);
            m_ClientDriver.ScheduleUpdate().Complete();

            // Handle incoming connection from client
            m_ServerDriver.ScheduleUpdate().Complete();
            var serverToClient = m_ServerDriver.Accept();
            Assert.AreNotEqual(default(NetworkConnection), serverToClient);

            m_ClientDriver.ScheduleUpdate().Complete();
            DataStreamReader readStrm;
            Assert.AreEqual(NetworkEvent.Type.Connect, clientToServer.PopEvent(m_ClientDriver, out readStrm));

            for (int i = 0; i < 30; ++i)
            {
                // Send message to client
                var strm = new DataStreamWriter(4, Allocator.Temp);
                strm.Write((int) i);
                m_ServerDriver.Send(serverPipe, serverToClient, strm);
                m_ServerDriver.ScheduleUpdate().Complete();

                // Receive incoming message from server
                m_ClientDriver.ScheduleUpdate().Complete();

                var readCtx = default(DataStreamReader.Context);
                var result = clientToServer.PopEvent(m_ClientDriver, out readStrm);

                Assert.AreEqual(NetworkEvent.Type.Data, result);
                Assert.AreEqual(4, readStrm.Length);
                Assert.AreEqual(i, readStrm.ReadInt(ref readCtx));

                // Send back a message to server
                strm.Clear();
                strm.Write((int) i*100);
                m_ClientDriver.Send(clientPipe, clientToServer, strm);
                m_ClientDriver.ScheduleUpdate().Complete();

                // Receive incoming message from client
                var timer = new Timer();
                while (true)
                {
                    m_ServerDriver.ScheduleUpdate().Complete();
                    readCtx = default(DataStreamReader.Context);
                    result = serverToClient.PopEvent(m_ServerDriver, out readStrm);
                    if (result != NetworkEvent.Type.Empty)
                        break;
                    if (timer.ElapsedMilliseconds > 1000)
                        break;
                }
                Assert.AreEqual(NetworkEvent.Type.Data, result);
                Assert.AreEqual(4, readStrm.Length);
                Assert.AreEqual(i*100, readStrm.ReadInt(ref readCtx));
                strm.Dispose();
            }
        }

        [Test]
        public unsafe void NetworkPipeline_ReliableSequenced_SendRecvManyWithPacketDropHighSeqId()
        {
            var clientPipe = m_ClientDriver.CreatePipeline(typeof(ReliableSequencedPipelineStage), typeof(SimulatorPipelineStage));
            var serverPipe = m_ServerDriver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
            Assert.AreEqual(clientPipe, serverPipe);

            // Connect to server
            var clientToServer = m_ClientDriver.Connect(m_ServerDriver.LocalEndPoint());
            Assert.AreNotEqual(default(NetworkConnection), clientToServer);
            m_ClientDriver.ScheduleUpdate().Complete();

            // Set sequence ID to a value just below wrapping over 0, also need to set last received seqId value to one
            // less or the first packet will be considered out of order and stored for later use
            NativeSlice<byte> receiveBuffer = default;
            NativeSlice<byte> sendBuffer = default;
            NativeSlice<byte> sharedBuffer = default;
            m_ClientDriver.GetPipelineBuffers(typeof(ReliableSequencedPipelineStage), clientToServer, ref receiveBuffer, ref sendBuffer, ref sharedBuffer);
            var sharedContext = (ReliableUtility.SharedContext*) sharedBuffer.GetUnsafePtr();
            sharedContext->SentPackets.Sequence = ushort.MaxValue - 1;
            sharedContext->SentPackets.Acked = ushort.MaxValue - 2;
            sharedContext->SentPackets.AckMask = 0xFFFFFFFF;
            sharedContext->ReceivedPackets.Sequence = sharedContext->SentPackets.Acked;
            sharedContext->ReceivedPackets.AckMask = sharedContext->SentPackets.AckMask;
            var receiveContext = (ReliableUtility.Context*) receiveBuffer.GetUnsafePtr();
            receiveContext->Delivered = sharedContext->SentPackets.Acked;

            // Handle incoming connection from client
            m_ServerDriver.ScheduleUpdate().Complete();
            var serverToClient = m_ServerDriver.Accept();
            Assert.AreNotEqual(default(NetworkConnection), serverToClient);

            // This test runs fast so the minimum resend times needs to be lower (assumes 1 ms update rate)
            ReliableUtility.SetMinimumResendTime(4, m_ClientDriver, clientPipe, 4, clientToServer);
            ReliableUtility.SetMinimumResendTime(4, m_ServerDriver, serverPipe, 4, serverToClient);

            m_ServerDriver.GetPipelineBuffers(typeof(ReliableSequencedPipelineStage), serverToClient, ref receiveBuffer, ref sendBuffer, ref sharedBuffer);
            sharedContext = (ReliableUtility.SharedContext*) sharedBuffer.GetUnsafePtr();
            sharedContext->SentPackets.Sequence = ushort.MaxValue - 1;
            sharedContext->SentPackets.Acked = ushort.MaxValue - 2;
            sharedContext->SentPackets.AckMask = 0xFFFFFFFF;
            sharedContext->ReceivedPackets.Sequence = sharedContext->SentPackets.Acked;
            sharedContext->ReceivedPackets.AckMask = sharedContext->SentPackets.AckMask;
            receiveContext = (ReliableUtility.Context*) receiveBuffer.GetUnsafePtr();
            receiveContext->Delivered = sharedContext->SentPackets.Acked;

            // Receive incoming message from server
            m_ClientDriver.ScheduleUpdate().Complete();

            SendAndReceiveMessages(clientToServer, serverToClient, clientPipe, serverPipe);
        }
        
        [Test]
        public void NetworkPipeline_ReliableSequenced_SendRecvManyWithPacketDrop()
        {
            var clientPipe = m_ClientDriver.CreatePipeline(typeof(ReliableSequencedPipelineStage), typeof(SimulatorPipelineStage));
            var serverPipe = m_ServerDriver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
            Assert.AreEqual(clientPipe, serverPipe);
            
            // Connect to server
            var clientToServer = m_ClientDriver.Connect(m_ServerDriver.LocalEndPoint());
            Assert.AreNotEqual(default(NetworkConnection), clientToServer);
            m_ClientDriver.ScheduleUpdate().Complete();

            // Handle incoming connection from client
            m_ServerDriver.ScheduleUpdate().Complete();
            var serverToClient = m_ServerDriver.Accept();
            Assert.AreNotEqual(default(NetworkConnection), serverToClient);

            // This test runs fast so the minimum resend times needs to be lower (assumes 1 ms update rate)
            ReliableUtility.SetMinimumResendTime(4, m_ClientDriver, clientPipe, 4, clientToServer);
            ReliableUtility.SetMinimumResendTime(4, m_ServerDriver, serverPipe, 4, serverToClient);

            // Receive incoming message from server
            m_ClientDriver.ScheduleUpdate().Complete();

            SendAndReceiveMessages(clientToServer, serverToClient, clientPipe, serverPipe);
        }

        unsafe void SendAndReceiveMessages(NetworkConnection clientToServer, NetworkConnection serverToClient, NetworkPipeline clientPipe, NetworkPipeline serverPipe)
        {
            DataStreamReader readStrm;

            Assert.AreEqual(NetworkEvent.Type.Connect, clientToServer.PopEvent(m_ClientDriver, out readStrm));
            // Next packet should be Empty and not Data as the packet was dropped
            Assert.AreEqual(NetworkEvent.Type.Empty, clientToServer.PopEvent(m_ClientDriver, out readStrm));

            var totalMessageCount = 100;
            var sendMessageCount = 0;
            var lastClientReceivedNumber = 0;
            var lastServerReceivedNumber = 0;
            var timer = new Timer();
            NativeSlice<byte> tmpReceiveBuffer = default;
            NativeSlice<byte> tmpSendBuffer = default;
            NativeSlice<byte> serverReliableBuffer = default;
            NativeSlice<byte> clientReliableBuffer = default;
            NativeSlice<byte> clientSimulatorBuffer = default;
            m_ServerDriver.GetPipelineBuffers(typeof(ReliableSequencedPipelineStage), serverToClient, ref tmpReceiveBuffer, ref tmpSendBuffer, ref serverReliableBuffer);
            var serverReliableCtx = (ReliableUtility.SharedContext*) serverReliableBuffer.GetUnsafePtr();
            m_ClientDriver.GetPipelineBuffers(typeof(ReliableSequencedPipelineStage), clientToServer, ref tmpReceiveBuffer, ref tmpSendBuffer, ref clientReliableBuffer);
            var clientReliableCtx = (ReliableUtility.SharedContext*) clientReliableBuffer.GetUnsafePtr();
            m_ClientDriver.GetPipelineBuffers(typeof(SimulatorPipelineStage), clientToServer, ref tmpReceiveBuffer, ref tmpSendBuffer, ref clientSimulatorBuffer);
            var clientSimulatorCtx = (SimulatorUtility.Context*) clientSimulatorBuffer.GetUnsafePtr();
            // Client is the one dropping packets, so wait for that count to reach total, server receive count will be higher
            while (lastClientReceivedNumber < totalMessageCount)
            {
                // Send message to client
                sendMessageCount++;
                var strm = new DataStreamWriter(4, Allocator.Temp);
                strm.Write((int) sendMessageCount);
                m_ServerDriver.Send(serverPipe, serverToClient, strm);
                if (serverReliableCtx->errorCode != 0)
                {
                    UnityEngine.Debug.Log("Reliability stats\nPacketsDropped: " + serverReliableCtx->stats.PacketsDropped + "\n" +
                                          "PacketsDuplicated: " + serverReliableCtx->stats.PacketsDuplicated + "\n" +
                                          "PacketsOutOfOrder: " + serverReliableCtx->stats.PacketsOutOfOrder + "\n" +
                                          "PacketsReceived: " + serverReliableCtx->stats.PacketsReceived + "\n" +
                                          "PacketsResent: " + serverReliableCtx->stats.PacketsResent + "\n" +
                                          "PacketsSent: " + serverReliableCtx->stats.PacketsSent + "\n" +
                                          "PacketsStale: " + serverReliableCtx->stats.PacketsStale + "\n");
                    Assert.AreEqual((ReliableUtility.ErrorCodes)0, serverReliableCtx->errorCode);
                }
                m_ServerDriver.ScheduleUpdate().Complete();

                var readCtx = default(DataStreamReader.Context);
                NetworkEvent.Type result;
                // Receive incoming message from server, might be empty but we still need to keep
                // sending or else a resend for a dropped packet will not happen
                m_ClientDriver.ScheduleUpdate().Complete();
                readCtx = default(DataStreamReader.Context);
                result = clientToServer.PopEvent(m_ClientDriver, out readStrm);
                Assert.AreEqual(m_ClientDriver.ReceiveErrorCode, 0);
                Assert.AreEqual((ReliableUtility.ErrorCodes)0, clientReliableCtx->errorCode);
                while (result != NetworkEvent.Type.Empty)
                {
                    Assert.AreEqual(4, readStrm.Length);
                    var read = readStrm.ReadInt(ref readCtx);
                    // We should be receiving in order, so last payload should be one more than the current receive count
                    Assert.AreEqual(lastClientReceivedNumber + 1, read);
                    lastClientReceivedNumber = read;
                    // Pop all events which might be pending (in case of dropped packet it should contain all the other packets already up to latest)
                    readCtx = default(DataStreamReader.Context);
                    result = clientToServer.PopEvent(m_ClientDriver, out readStrm);
                    Assert.AreEqual((ReliableUtility.ErrorCodes)0, clientReliableCtx->errorCode);
                }

                // Send back a message to server
                strm.Clear();
                strm.Write((int) sendMessageCount * 100);
                m_ClientDriver.Send(clientPipe, clientToServer, strm);
                Assert.AreEqual((ReliableUtility.ErrorCodes)0, clientReliableCtx->errorCode);
                m_ClientDriver.ScheduleUpdate().Complete();

                // Receive incoming message from client
                m_ServerDriver.ScheduleUpdate().Complete();
                readCtx = default(DataStreamReader.Context);
                result = serverToClient.PopEvent(m_ServerDriver, out readStrm);
                Assert.AreEqual(m_ServerDriver.ReceiveErrorCode, 0);
                Assert.AreEqual((ReliableUtility.ErrorCodes)0, serverReliableCtx->errorCode);
                while (result != NetworkEvent.Type.Empty)
                {
                    Assert.AreEqual(4, readStrm.Length);
                    var read = readStrm.ReadInt(ref readCtx);
                    Assert.AreEqual(lastServerReceivedNumber + 100, read);
                    lastServerReceivedNumber = read;
                    readCtx = default(DataStreamReader.Context);
                    result = clientToServer.PopEvent(m_ClientDriver, out readStrm);
                    Assert.AreEqual((ReliableUtility.ErrorCodes)0, serverReliableCtx->errorCode);
                }

                //Assert.AreEqual(0, serverReliableCtx->stats.PacketsDuplicated);
                Assert.AreEqual(0, serverReliableCtx->stats.PacketsStale);
                //Assert.AreEqual(0, clientReliableCtx->stats.PacketsDuplicated);
                Assert.AreEqual(0, clientReliableCtx->stats.PacketsStale);

                if (timer.ElapsedMilliseconds > 1000)
                    Assert.Fail("Test timeout, didn't receive all messages (" + totalMessageCount + ")");

                strm.Dispose();
            }

            var stats = serverReliableCtx->stats;
            // You can get legtimate duplicated packets in the test, if the ack was just not received in time for the resend timer expired
            //Assert.AreEqual(stats.PacketsResent, clientSimulatorCtx->PacketDropCount);
            //Assert.AreEqual(stats.PacketsDuplicated, 0);
            Assert.AreEqual(stats.PacketsStale, 0);
            UnityEngine.Debug.Log("Server Reliability stats\nPacketsDropped: " + serverReliableCtx->stats.PacketsDropped + "\n" +
                                  "PacketsDuplicated: " + serverReliableCtx->stats.PacketsDuplicated + "\n" +
                                  "PacketsOutOfOrder: " + serverReliableCtx->stats.PacketsOutOfOrder + "\n" +
                                  "PacketsReceived: " + serverReliableCtx->stats.PacketsReceived + "\n" +
                                  "PacketsResent: " + serverReliableCtx->stats.PacketsResent + "\n" +
                                  "PacketsSent: " + serverReliableCtx->stats.PacketsSent + "\n" +
                                  "PacketsStale: " + serverReliableCtx->stats.PacketsStale + "\n");
            UnityEngine.Debug.Log("Client Reliability stats\nPacketsDropped: " + clientReliableCtx->stats.PacketsDropped + "\n" +
                                  "PacketsDuplicated: " + clientReliableCtx->stats.PacketsDuplicated + "\n" +
                                  "PacketsOutOfOrder: " + clientReliableCtx->stats.PacketsOutOfOrder + "\n" +
                                  "PacketsReceived: " + clientReliableCtx->stats.PacketsReceived + "\n" +
                                  "PacketsResent: " + clientReliableCtx->stats.PacketsResent + "\n" +
                                  "PacketsSent: " + clientReliableCtx->stats.PacketsSent + "\n" +
                                  "PacketsStale: " + clientReliableCtx->stats.PacketsStale + "\n");
            UnityEngine.Debug.Log("Client Simulator stats\n" +
                                  "PacketDropCount: " + clientSimulatorCtx->PacketDropCount + "\n" +
                                  "PacketCount: " + clientSimulatorCtx->PacketCount);
        }

        [Test]
        public void NetworkPipeline_UnreliableSequenced_SendRecvOnce()
        {
            var clientPipe = m_ClientDriver.CreatePipeline(typeof(UnreliableSequencedPipelineStage));
            var serverPipe = m_ServerDriver.CreatePipeline(typeof(UnreliableSequencedPipelineStage));
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
        public unsafe void NetworkPipeline_ReliableSequenced_ClientSendsNothing()
        {
            var clientPipe = m_ClientDriver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
            var serverPipe = m_ServerDriver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
            Assert.AreEqual(clientPipe, serverPipe);

            // Connect to server
            var clientToServer = m_ClientDriver.Connect(m_ServerDriver.LocalEndPoint());
            Assert.AreNotEqual(default(NetworkConnection), clientToServer);
            m_ClientDriver.ScheduleUpdate().Complete();

            // Handle incoming connection from client
            m_ServerDriver.ScheduleUpdate().Complete();
            var serverToClient = m_ServerDriver.Accept();
            Assert.AreNotEqual(default(NetworkConnection), serverToClient);

            // Receive incoming message from server
            m_ClientDriver.ScheduleUpdate().Complete();
            DataStreamReader readStrm;
            Assert.AreEqual(NetworkEvent.Type.Connect, clientToServer.PopEvent(m_ClientDriver, out readStrm));

            // Do a loop where server sends to client but client sends nothing back, it should send empty ack packets back
            // so the servers queue will not get full
            var totalMessageCount = 100;
            var sendMessageCount = 0;
            var lastClientReceivedNumber = 0;
            var timer = new Timer();
            NativeSlice<byte> tmpReceiveBuffer = default;
            NativeSlice<byte> tmpSendBuffer = default;
            NativeSlice<byte> serverReliableBuffer = default;
            NativeSlice<byte> clientReliableBuffer = default;

            m_ServerDriver.GetPipelineBuffers(typeof(ReliableSequencedPipelineStage), serverToClient, ref tmpReceiveBuffer, ref tmpSendBuffer, ref serverReliableBuffer);
            var serverReliableCtx = (ReliableUtility.SharedContext*) serverReliableBuffer.GetUnsafePtr();
            m_ClientDriver.GetPipelineBuffers(typeof(ReliableSequencedPipelineStage), clientToServer, ref tmpReceiveBuffer, ref tmpSendBuffer, ref clientReliableBuffer);
            var clientReliableCtx = (ReliableUtility.SharedContext*) clientReliableBuffer.GetUnsafePtr();

            // Finish when client has received all messages from server without errors
            while (lastClientReceivedNumber < totalMessageCount)
            {
                // Send message to client
                sendMessageCount++;
                var strm = new DataStreamWriter(4, Allocator.Temp);
                strm.Write((int) sendMessageCount);
                m_ServerDriver.Send(serverPipe, serverToClient, strm);
                Assert.AreEqual((ReliableUtility.ErrorCodes)0, serverReliableCtx->errorCode);
                m_ServerDriver.ScheduleUpdate().Complete();

                var readCtx = default(DataStreamReader.Context);
                NetworkEvent.Type result;
                // Receive incoming message from server, might be empty or might be more than one message
                m_ClientDriver.ScheduleUpdate().Complete();
                readCtx = default(DataStreamReader.Context);
                result = clientToServer.PopEvent(m_ClientDriver, out readStrm);
                Assert.AreEqual(m_ClientDriver.ReceiveErrorCode, 0);
                Assert.AreEqual((ReliableUtility.ErrorCodes)0, clientReliableCtx->errorCode);
                while (result != NetworkEvent.Type.Empty)
                {
                    Assert.AreEqual(4, readStrm.Length);
                    var read = readStrm.ReadInt(ref readCtx);
                    // We should be receiving in order, so last payload should be one more than the current receive count
                    Assert.AreEqual(lastClientReceivedNumber + 1, read);
                    lastClientReceivedNumber = read;
                    // Pop all events which might be pending (in case of dropped packet it should contain all the other packets already up to latest)
                    readCtx = default(DataStreamReader.Context);
                    result = clientToServer.PopEvent(m_ClientDriver, out readStrm);
                    Assert.AreEqual((ReliableUtility.ErrorCodes)0, clientReliableCtx->errorCode);
                }

                // no-op
                m_ClientDriver.ScheduleUpdate().Complete();

                // Make sure no event has arrived on server and no errors seen
                m_ServerDriver.ScheduleUpdate().Complete();
                readCtx = default(DataStreamReader.Context);
                Assert.AreEqual(serverToClient.PopEvent(m_ServerDriver, out readStrm), NetworkEvent.Type.Empty);
                Assert.AreEqual(m_ServerDriver.ReceiveErrorCode, 0);
                Assert.AreEqual((ReliableUtility.ErrorCodes)0, serverReliableCtx->errorCode);

                if (timer.ElapsedMilliseconds > 1000)
                    Assert.Fail("Test timeout, didn't receive all messages (" + totalMessageCount + ")");

                strm.Dispose();
            }

            // The empty ack packets will bump the PacketsSent count, also in this test it can happen that a duplicate
            // packet is sent because the timers are tight
            //Assert.AreEqual(totalMessageCount, serverReliableCtx->stats.PacketsSent);
        }

        [Test]
        public unsafe void NetworkPipeline_ReliableSequenced_NothingIsSentAfterPingPong()
        {
            // Use simulator pipeline here just to count packets, need to reset the drivers for this setup
            m_ServerDriver.Dispose();
            m_ClientDriver.Dispose();
            m_ServerDriver =
                new LocalNetworkDriver(new NetworkDataStreamParameter
                        {size = 0},
                    new ReliableUtility.Parameters { WindowSize = 32},
                    new SimulatorUtility.Parameters { MaxPacketCount = 30, MaxPacketSize = 16, PacketDelayMs = 0, PacketDropPercentage = 0});
            m_ServerDriver.Bind(IPCManager.Instance.CreateEndPoint());
            m_ServerDriver.Listen();
            m_ClientDriver =
                new LocalNetworkDriver(new NetworkDataStreamParameter
                        {size = 0},
                    new ReliableUtility.Parameters { WindowSize = 32},
                    new SimulatorUtility.Parameters { MaxPacketCount = 30, MaxPacketSize = 16, PacketDelayMs = 0, PacketDropPercentage = 0});

            var clientPipe = m_ClientDriver.CreatePipeline(typeof(ReliableSequencedPipelineStage), typeof(SimulatorPipelineStage));
            var serverPipe = m_ServerDriver.CreatePipeline(typeof(ReliableSequencedPipelineStage), typeof(SimulatorPipelineStage));
            var clientToServer = m_ClientDriver.Connect(m_ServerDriver.LocalEndPoint());
            m_ClientDriver.ScheduleUpdate().Complete();
            m_ServerDriver.ScheduleUpdate().Complete();
            var serverToClient = m_ServerDriver.Accept();

            m_ClientDriver.ScheduleUpdate().Complete();
            DataStreamReader readStrm;
            Assert.AreEqual(NetworkEvent.Type.Connect, clientToServer.PopEvent(m_ClientDriver, out readStrm));

            // Perform ping pong transmision
            var strm = new DataStreamWriter(4, Allocator.Temp);
            strm.Write((int) 100);
            Console.WriteLine("Server send");
            m_ServerDriver.Send(serverPipe, serverToClient, strm);
            m_ServerDriver.ScheduleUpdate().Complete();
            Console.WriteLine("Client update");
            m_ClientDriver.ScheduleUpdate().Complete();
            Assert.AreEqual(NetworkEvent.Type.Data, clientToServer.PopEvent(m_ClientDriver, out readStrm));
            strm.Clear();
            strm.Write((int) 200);
            Console.WriteLine("Client send");
            m_ClientDriver.Send(clientPipe, clientToServer, strm);
            m_ClientDriver.ScheduleUpdate().Complete();
            Console.WriteLine("Server update");
            m_ServerDriver.ScheduleUpdate().Complete();
            Assert.AreEqual(NetworkEvent.Type.Data, serverToClient.PopEvent(m_ServerDriver, out readStrm));

            // Check how many packets have been sent so far
            NativeSlice<byte> tmpReceiveBuffer = default;
            NativeSlice<byte> tmpSendBuffer = default;
            NativeSlice<byte> simulatorBuffer = default;
            m_ClientDriver.GetPipelineBuffers(typeof(SimulatorPipelineStage), clientToServer, ref tmpReceiveBuffer, ref tmpSendBuffer, ref simulatorBuffer);
            var simulatorCtx = (SimulatorUtility.Context*) simulatorBuffer.GetUnsafePtr();

            // Do a loop and make sure nothing is being sent between client and server
            var timer = new Timer();
            while (timer.ElapsedMilliseconds < 1000)
            {
                m_ServerDriver.ScheduleUpdate().Complete();
                m_ClientDriver.ScheduleUpdate().Complete();
            }

            // The client simulator counts all packets which pass through the pipeline so will catch anything the
            // reliability pipeline might send, only 2 packets (data + ack packet) should have been received on client
            Assert.AreEqual(2, simulatorCtx->PacketCount);

            // Check server side as well, server only has one packet as the client included it's ack in the pong packet it sent
            m_ServerDriver.GetPipelineBuffers(typeof(SimulatorPipelineStage), clientToServer, ref tmpReceiveBuffer, ref tmpSendBuffer, ref simulatorBuffer);
            simulatorCtx = (SimulatorUtility.Context*) simulatorBuffer.GetUnsafePtr();
            Assert.AreEqual(1, simulatorCtx->PacketCount);
        }

        [Test]
        public unsafe void NetworkPipeline_ReliableSequenced_IdleAfterPacketDrop()
        {
            // Use simulator drop interval, then first packet will be dropped
            m_ClientDriver.Dispose();
            m_ClientDriver =
                new LocalNetworkDriver(new NetworkDataStreamParameter
                        {size = 0},
                    new ReliableUtility.Parameters { WindowSize = 32},
                    new SimulatorUtility.Parameters { MaxPacketCount = 30, MaxPacketSize = 16, PacketDelayMs = 0, PacketDropInterval = 10});

            m_ClientDriver.CreatePipeline(typeof(ReliableSequencedPipelineStage), typeof(SimulatorPipelineStage));
            var serverPipe = m_ServerDriver.CreatePipeline(typeof(ReliableSequencedPipelineStage), typeof(SimulatorPipelineStage));
            var clientToServer = m_ClientDriver.Connect(m_ServerDriver.LocalEndPoint());
            m_ClientDriver.ScheduleUpdate().Complete();
            m_ServerDriver.ScheduleUpdate().Complete();
            var serverToClient = m_ServerDriver.Accept();

            m_ClientDriver.ScheduleUpdate().Complete();
            DataStreamReader readStrm;
            Assert.AreEqual(NetworkEvent.Type.Connect, clientToServer.PopEvent(m_ClientDriver, out readStrm));

            // Server sends one packet, this will be dropped, client has empty event
            var strm = new DataStreamWriter(4, Allocator.Temp);
            strm.Write((int) 100);
            m_ServerDriver.Send(serverPipe, serverToClient, strm);
            m_ServerDriver.ScheduleUpdate().Complete();
            m_ClientDriver.ScheduleUpdate().Complete();
            Assert.AreEqual(NetworkEvent.Type.Empty, clientToServer.PopEvent(m_ClientDriver, out readStrm));

            // Wait until client receives the server packet resend
            var timer = new Timer();
            var clientEvent = NetworkEvent.Type.Empty;
            while (timer.ElapsedMilliseconds < 1000)
            {
                m_ClientDriver.ScheduleUpdate().Complete();
                m_ServerDriver.ScheduleUpdate().Complete();
                clientEvent = clientToServer.PopEvent(m_ClientDriver, out readStrm);
                if (clientEvent != NetworkEvent.Type.Empty)
                    break;
            }
            Assert.AreEqual(NetworkEvent.Type.Data, clientEvent);

            // Verify exactly one packet has been dropped
            NativeSlice<byte> tmpReceiveBuffer = default;
            NativeSlice<byte> tmpSendBuffer = default;
            NativeSlice<byte> simulatorBuffer = default;
            m_ClientDriver.GetPipelineBuffers(typeof(SimulatorPipelineStage), clientToServer, ref tmpReceiveBuffer, ref tmpSendBuffer, ref simulatorBuffer);
            var simulatorCtx = (SimulatorUtility.Context*) simulatorBuffer.GetUnsafePtr();
            Assert.AreEqual(simulatorCtx->PacketDropCount, 1);
        }

    }
}

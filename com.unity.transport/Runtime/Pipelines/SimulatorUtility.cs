using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Random = Unity.Mathematics.Random;

namespace Unity.Networking.Transport.Utilities
{
    public struct SimulatorUtility
    {
        private int m_PacketCount;
        private int m_MaxPacketSize;
        private int m_PacketDelayMs;

        /// <summary>
        /// Configuration parameters for the simulator pipeline stage.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct Parameters : INetworkParameter
        {
            /// <summary>
            /// The maximum amount of packets the pipeline can keep track of. This used when a
            /// packet is delayed, the packet is stored in the pipeline processing buffer and can
            /// be later brought back.
            /// </summary>
            public int MaxPacketCount;
            /// <summary>
            /// The maximum size of a packet which the simulator stores. If a packet exceeds this size it will
            /// bypass the simulator.
            /// </summary>
            public int MaxPacketSize;
            /// <summary>
            /// Fixed delay to apply to all packets which pass through.
            /// </summary>
            public int PacketDelayMs;
            /// <summary>
            /// Fixed interval to drop packets on. This is most suitable for tests where predictable
            /// behaviour is desired, every Xth packet will be dropped. If PacketDropInterval is 5
            /// every 5th packet is dropped.
            /// </summary>
            public int PacketDropInterval;
            /// <summary>
            /// Use a drop percentage when deciding when to drop packet. For every packet
            /// a random number generator is used to determine if the packet should be dropped or not.
            /// A percentage of 5 means approximately every 20th packet will be dropped.
            /// </summary>
            public int PacketDropPercentage;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Context
        {
            public int MaxPacketCount;
            public int MaxPacketSize;
            public int PacketDelayMs;
            public int PacketDrop;

            public Random Random;

            // Statistics
            public int PacketCount;
            public int PacketDropCount;
            public int ReadyPackets;
            public int WaitingPackets;
            public long NextPacketTime;
            public long StatsTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DelayedPacket
        {
            public int processBufferOffset;
            public int packetSize;
            public long delayUntil;
        }

        public SimulatorUtility(int packetCount, int maxPacketSize, int packetDelayMs)
        {
            m_PacketCount = packetCount;
            m_MaxPacketSize = maxPacketSize;
            m_PacketDelayMs = packetDelayMs;
        }

        public static unsafe void InitializeContext(Parameters param, NativeSlice<byte> sharedProcessBuffer)
        {
            // Store parameters in the shared buffer space
            Context* ctx = (Context*) sharedProcessBuffer.GetUnsafePtr();
            ctx->MaxPacketCount = param.MaxPacketCount;
            ctx->MaxPacketSize = param.MaxPacketSize;
            ctx->PacketDelayMs = param.PacketDelayMs;
            ctx->PacketDrop = param.PacketDropInterval;
            ctx->PacketCount = 0;
            ctx->PacketDropCount = 0;
            ctx->Random = new Random();
            ctx->Random.InitState();
        }

        public unsafe bool GetEmptyDataSlot(byte* processBufferPtr, ref int packetPayloadOffset,
            ref int packetDataOffset)
        {
            var dataSize = UnsafeUtility.SizeOf<DelayedPacket>();
            var packetPayloadStartOffset = m_PacketCount * dataSize;

            bool foundSlot = false;
            for (int i = 0; i < m_PacketCount; i++)
            {
                packetDataOffset = dataSize * i;
                DelayedPacket* packetData = (DelayedPacket*) (processBufferPtr + packetDataOffset);

                // Check if this slot is empty
                if (packetData->delayUntil == 0)
                {
                    foundSlot = true;
                    packetPayloadOffset = packetPayloadStartOffset + m_MaxPacketSize * i;
                    break;
                }
            }

            return foundSlot;
        }

        public void StorePacketPayload(NativeSlice<byte> destinationSlice, NativeSlice<byte> sourceSlice1,
            NativeSlice<byte> sourceSlice2)
        {
            int position;
            for (position = 0; position < sourceSlice1.Length; ++position)
                destinationSlice[position] = sourceSlice1[position];
            for (int i = 0; i < sourceSlice2.Length; ++i)
                destinationSlice[position++] = sourceSlice2[i];
        }

        public unsafe bool GetDelayedPacket(ref NetworkPipelineContext ctx, ref NativeSlice<byte> delayedPacket,
            ref bool needsResume, ref bool needsUpdate, long currentTimestamp)
        {
            needsUpdate = needsResume = false;

            var dataSize = UnsafeUtility.SizeOf<DelayedPacket>();
            byte* processBufferPtr = (byte*) ctx.internalProcessBuffer.GetUnsafePtr();
            var simCtx = (Context*) ctx.internalSharedProcessBuffer.GetUnsafePtr();
            int oldestPacketIndex = -1;
            long oldestTime = long.MaxValue;
            int readyPackets = 0;
            int packetsInQueue = 0;
            for (int i = 0; i < m_PacketCount; i++)
            {
                DelayedPacket* packet = (DelayedPacket*) (processBufferPtr + dataSize * i);
                if ((int) packet->delayUntil == 0) continue;
                packetsInQueue++;

                if (packet->delayUntil > currentTimestamp) continue;
                readyPackets++;

                if (oldestTime <= packet->delayUntil) continue;
                oldestPacketIndex = i;
                oldestTime = packet->delayUntil;
            }

            simCtx->ReadyPackets = readyPackets;
            simCtx->WaitingPackets = packetsInQueue;
            simCtx->NextPacketTime = oldestTime;
            simCtx->StatsTime = currentTimestamp;

            // If more than one item has expired timer we need to resume this pipeline stage
            if (readyPackets > 1)
            {
                needsUpdate = false;
                needsResume = true;
            }
            // If more than one item is present (but doesn't have expired timer) we need to re-run the pipeline
            // in a later update call
            else if (packetsInQueue > 0)
            {
                needsUpdate = true;
                needsResume = false;
            }

            if (oldestPacketIndex >= 0)
            {
                DelayedPacket* packet = (DelayedPacket*) (processBufferPtr + dataSize * oldestPacketIndex);
                packet->delayUntil = 0;

                delayedPacket = new NativeSlice<byte>(ctx.internalProcessBuffer, packet->processBufferOffset,
                    packet->packetSize);
                return true;
            }

            return false;
        }

        public unsafe bool DelayPacket(ref NetworkPipelineContext ctx, InboundBufferVec inboundBuffer,
            ref bool needsUpdate,
            long timestamp)
        {
            // Find empty slot in bookkeeping data space to track this packet
            int packetPayloadOffset = 0;
            int packetDataOffset = 0;
            var processBufferPtr = (byte*) ctx.internalProcessBuffer.GetUnsafePtr();
            bool foundSlot = GetEmptyDataSlot(processBufferPtr, ref packetPayloadOffset, ref packetDataOffset);

            if (!foundSlot)
            {
                //UnityEngine.Debug.LogWarning("No space left for delaying packet (" + m_PacketCount + " packets in queue)");
                return false;
            }

            NativeSlice<byte> packetPayload =
                new NativeSlice<byte>(ctx.internalProcessBuffer, packetPayloadOffset,
                    inboundBuffer.buffer1.Length + inboundBuffer.buffer2.Length);

            StorePacketPayload(packetPayload, inboundBuffer.buffer1, inboundBuffer.buffer2);

            // Add tracking for this packet so we can resurrect later
            DelayedPacket packet;
            packet.delayUntil = timestamp + m_PacketDelayMs;
            packet.processBufferOffset = packetPayloadOffset;
            packet.packetSize = inboundBuffer.buffer1.Length + inboundBuffer.buffer2.Length;
            byte* packetPtr = (byte*) &packet;
            UnsafeUtility.MemCpy(processBufferPtr + packetDataOffset, packetPtr, UnsafeUtility.SizeOf<DelayedPacket>());

            // Schedule an update call so packet can be resurrected later
            needsUpdate = true;

            return true;
        }

        public unsafe bool ShouldDropPacket(Context* ctx, Parameters param, long timestamp)
        {
            if (param.PacketDropInterval > 0 && (ctx->PacketCount - 1) % param.PacketDropInterval == 0)
                return true;
            if (param.PacketDropPercentage > 0)
            {
                //var packetLoss = new System.Random().NextDouble() * 100;
                var packetLoss = ctx->Random.NextDouble() * 100;
                if (packetLoss < param.PacketDropPercentage)
                    return true;
            }

            return false;
        }
    }
}
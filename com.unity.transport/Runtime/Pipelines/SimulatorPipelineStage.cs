using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Networking.Transport.Utilities;

namespace Unity.Networking.Transport
{
    [NetworkPipelineInitilize(typeof(SimulatorUtility.Parameters))]
    public struct SimulatorPipelineStage : INetworkPipelineStage
    {
        private SimulatorUtility.Parameters m_SimulatorParams;

        // Setup simulation parameters which get capacity function depends on, so buffer size can be correctly allocated
        public void Initialize(SimulatorUtility.Parameters param)
        {
            m_SimulatorParams = param;
        }

        public unsafe NativeSlice<byte> Receive(NetworkPipelineContext ctx, NativeSlice<byte> inboundBuffer, ref bool needsResume, ref bool needsUpdate, ref bool needsSendUpdate)
        {
            var param = (SimulatorUtility.Context*) ctx.internalSharedProcessBuffer.GetUnsafePtr();
            var simulator = new SimulatorUtility(m_SimulatorParams.MaxPacketCount, m_SimulatorParams.MaxPacketSize, m_SimulatorParams.PacketDelayMs);
            if (inboundBuffer.Length > m_SimulatorParams.MaxPacketSize)
            {
                //UnityEngine.Debug.LogWarning("Incoming packet too large for internal storage buffer. Passing through. [buffer=" + inboundBuffer.Length + " packet=" + param->MaxPacketSize + "]");
                // TODO: Add error code for this
                return inboundBuffer;
            }

            var timestamp = ctx.timestamp;

            if (inboundBuffer.Length > 0)
                param->PacketCount++;

            bool delayPacket = param->PacketDelayMs > 0;

            // Inbound buffer is empty if this is a resumed receive
            if (delayPacket && inboundBuffer.Length > 0)
            {
                var bufferVec = default(InboundBufferVec);
                bufferVec.buffer1 = inboundBuffer;
                if (!simulator.DelayPacket(ref ctx, bufferVec, ref needsUpdate, timestamp))
                {
                    return inboundBuffer;
                }
            }

            if (simulator.ShouldDropPacket(param, m_SimulatorParams, timestamp))
            {
                param->PacketDropCount++;
                return new NativeSlice<byte>();
            }

            NativeSlice<byte> returnPacket = default(NativeSlice<byte>);
            if (simulator.GetDelayedPacket(ref ctx, ref returnPacket, ref needsResume, ref needsUpdate, timestamp))
                return returnPacket;

            // Pass packet through, nothing was done with it. Or return empty buffer if no inbound buffer is here (then nothing is being resurrected)
            if (!delayPacket && inboundBuffer.Length > 0)
                return inboundBuffer;
            return new NativeSlice<byte>();
        }

        public InboundBufferVec Send(NetworkPipelineContext ctx, InboundBufferVec inboundBuffer, ref bool needsResume, ref bool needsUpdate)
        {
            return inboundBuffer;
        }

        public unsafe void InitializeConnection(NativeSlice<byte> sendProcessBuffer, NativeSlice<byte> recvProcessBuffer,
            NativeSlice<byte> sharedProcessBuffer)
        {
            if (sharedProcessBuffer.Length >= UnsafeUtility.SizeOf<SimulatorUtility.Parameters>())
                SimulatorUtility.InitializeContext(m_SimulatorParams, sharedProcessBuffer);
        }

        public int ReceiveCapacity => m_SimulatorParams.MaxPacketCount * (m_SimulatorParams.MaxPacketSize+UnsafeUtility.SizeOf<SimulatorUtility.DelayedPacket>());
        public int SendCapacity => 0;
        public int HeaderCapacity => 0;
        public int SharedStateCapacity => UnsafeUtility.SizeOf<SimulatorUtility.Context>();
    }

    [NetworkPipelineInitilize(typeof(SimulatorUtility.Parameters))]
    public struct SimulatorPipelineStageInSend : INetworkPipelineStage
    {
        private SimulatorUtility.Parameters m_SimulatorParams;

        // Setup simulation parameters which get capacity function depends on, so buffer size can be correctly allocated
        public void Initialize(SimulatorUtility.Parameters param)
        {
            m_SimulatorParams = param;
        }

        public NativeSlice<byte> Receive(NetworkPipelineContext ctx, NativeSlice<byte> inboundBuffer, ref bool needsResume, ref bool needsUpdate, ref bool needsSendUpdate)
        {
            return new NativeSlice<byte>(inboundBuffer, 0, inboundBuffer.Length);
        }

        public InboundBufferVec Send(NetworkPipelineContext ctx, InboundBufferVec inboundBuffer, ref bool needsResume, ref bool needsUpdate)
        {
            var simulator = new SimulatorUtility(m_SimulatorParams.MaxPacketCount, m_SimulatorParams.MaxPacketSize, m_SimulatorParams.PacketDelayMs);
            if (inboundBuffer.buffer1.Length+inboundBuffer.buffer2.Length > m_SimulatorParams.MaxPacketSize)
            {
                //UnityEngine.Debug.LogWarning("Incoming packet too large for internal storage buffer. Passing through. [buffer=" + (inboundBuffer.buffer1.Length+inboundBuffer.buffer2.Length) + " packet=" + param->MaxPacketSize + "]");
                return inboundBuffer;
            }

            var timestamp = ctx.timestamp;

            // Packet always delayed atm
            bool delayPacket = true;

            // Inbound buffer is empty if this is a resumed receive
            if (delayPacket && inboundBuffer.buffer1.Length > 0)
            {
                if (!simulator.DelayPacket(ref ctx, inboundBuffer, ref needsUpdate, timestamp))
                {
                    return inboundBuffer;
                }
            }

            NativeSlice<byte> returnPacket = default(NativeSlice<byte>);
            if (simulator.GetDelayedPacket(ref ctx, ref returnPacket, ref needsResume, ref needsUpdate, timestamp))
            {
                inboundBuffer.buffer1 = returnPacket;
                inboundBuffer.buffer2 = default(NativeSlice<byte>);
                return inboundBuffer;
            }

            return default(InboundBufferVec);
        }

        public void InitializeConnection(NativeSlice<byte> sendProcessBuffer, NativeSlice<byte> recvProcessBuffer,
            NativeSlice<byte> sharedProcessBuffer)
        {
        }

        public int ReceiveCapacity => 0;
        public int SendCapacity => m_SimulatorParams.MaxPacketCount * (m_SimulatorParams.MaxPacketSize+UnsafeUtility.SizeOf<SimulatorUtility.DelayedPacket>());
        public int HeaderCapacity => 0;
        public int SharedStateCapacity => UnsafeUtility.SizeOf<SimulatorUtility.Context>();
    }
}

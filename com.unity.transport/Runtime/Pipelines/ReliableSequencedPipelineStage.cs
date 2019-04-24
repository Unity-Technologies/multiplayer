using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Networking.Transport.Utilities;

namespace Unity.Networking.Transport
{
    [NetworkPipelineInitilize(typeof(ReliableUtility.Parameters))]
    public struct ReliableSequencedPipelineStage : INetworkPipelineStage
    {
        private ReliableUtility.Parameters m_ReliableParams;

        public NativeSlice<byte> Receive(NetworkPipelineContext ctx, NativeSlice<byte> inboundBuffer, ref bool needsResume, ref bool needsUpdate, ref bool needsSendUpdate)
        {
            needsResume = false;
            // Request a send update to see if a queued packet needs to be resent later or if an ack packet should be sent
            needsSendUpdate = true;

            var context = default(DataStreamReader.Context);
            var header = default(ReliableUtility.PacketHeader);
            var slice = default(NativeSlice<byte>);
            unsafe
            {
                ReliableUtility.Context* reliable = (ReliableUtility.Context*) ctx.internalProcessBuffer.GetUnsafePtr();
                ReliableUtility.SharedContext* shared = (ReliableUtility.SharedContext*) ctx.internalSharedProcessBuffer.GetUnsafePtr();
                shared->errorCode = 0;
                if (reliable->Resume == ReliableUtility.NullEntry)
                {
                    if (inboundBuffer.Length <= 0)
                        return slice;
                    var reader = new DataStreamReader(inboundBuffer);
                    reader.ReadBytes(ref context, (byte*)&header, UnsafeUtility.SizeOf<ReliableUtility.PacketHeader>());

                    if (header.Type == (ushort)ReliableUtility.PacketType.Ack)
                    {
                        ReliableUtility.ReadAckPacket(ctx, header);
                        inboundBuffer = new NativeSlice<byte>();
                        return inboundBuffer;
                    }

                    var result = ReliableUtility.Read(ctx, header);

                    if (result >= 0)
                    {
                        var nextExpectedSequenceId = (ushort) (reliable->Delivered + 1);
                        if (result == nextExpectedSequenceId)
                        {
                            reliable->Delivered = result;
                            slice = inboundBuffer.Slice(UnsafeUtility.SizeOf<ReliableUtility.PacketHeader>());

                            if (needsResume = SequenceHelpers.GreaterThan16((ushort) shared->ReceivedPackets.Sequence,
                                (ushort) result))
                            {
                                reliable->Resume = (ushort)(result + 1);
                            }
                        }
                        else
                        {
                            ReliableUtility.SetPacket(ctx.internalProcessBuffer, result, inboundBuffer.Slice(UnsafeUtility.SizeOf<ReliableUtility.PacketHeader>()));
                            slice = ReliableUtility.ResumeReceive(ctx, reliable->Delivered + 1, ref needsResume);
                        }
                    }
                }
                else
                {
                    slice = ReliableUtility.ResumeReceive(ctx, reliable->Resume, ref needsResume);
                }
            }
            return slice;
        }

        public InboundBufferVec Send(NetworkPipelineContext ctx, InboundBufferVec inboundBuffer, ref bool needsResume, ref bool needsUpdate)
        {
            // Request an update to see if a queued packet needs to be resent later or if an ack packet should be sent
            needsUpdate = true;

            var header = new ReliableUtility.PacketHeader();
            unsafe
            {
                var reliable = (ReliableUtility.Context*) ctx.internalProcessBuffer.GetUnsafePtr();

                needsResume = ReliableUtility.ReleaseOrResumePackets(ctx);

                if (inboundBuffer.buffer1.Length > 0)
                {
                    reliable->LastSentTime = ctx.timestamp;

                    ReliableUtility.Write(ctx, inboundBuffer, ref header);
                    ctx.header.WriteBytes((byte*)&header, UnsafeUtility.SizeOf<ReliableUtility.PacketHeader>());
                    if (reliable->Resume != ReliableUtility.NullEntry)
                        needsResume = true;

                    reliable->PreviousTimestamp = ctx.timestamp;
                    return inboundBuffer;
                }

                if (reliable->Resume != ReliableUtility.NullEntry)
                {
                    reliable->LastSentTime = ctx.timestamp;
                    var slice = ReliableUtility.ResumeSend(ctx, out header, ref needsResume);
                    ctx.header.Clear();
                    ctx.header.WriteBytes((byte*)&header, UnsafeUtility.SizeOf<ReliableUtility.PacketHeader>());
                    inboundBuffer.buffer1 = slice;
                    inboundBuffer.buffer2 = default(NativeSlice<byte>);
                    reliable->PreviousTimestamp = ctx.timestamp;
                    return inboundBuffer;
                }

                if (ReliableUtility.ShouldSendAck(ctx))
                {
                    reliable->LastSentTime = ctx.timestamp;

                    ReliableUtility.WriteAckPacket(ctx, ref header);
                    ctx.header.WriteBytes((byte*)&header, UnsafeUtility.SizeOf<ReliableUtility.PacketHeader>());
                    reliable->PreviousTimestamp = ctx.timestamp;

                    // TODO: Sending dummy byte over since the pipeline won't send an empty payload (ignored on receive)
                    inboundBuffer.buffer1 = new NativeSlice<byte>(ctx.internalProcessBuffer, 0, 1);
                    return inboundBuffer;
                }
                reliable->PreviousTimestamp = ctx.timestamp;
                return inboundBuffer;
            }
        }

        public void InitializeConnection(NativeSlice<byte> sendProcessBuffer, NativeSlice<byte> recvProcessBuffer,
            NativeSlice<byte> sharedProcessBuffer)
        {
            if (sharedProcessBuffer.Length >= ReliableUtility.SharedCapacityNeeded(m_ReliableParams) &&
                (sendProcessBuffer.Length + recvProcessBuffer.Length) >= ReliableUtility.ProcessCapacityNeeded(m_ReliableParams) * 2)
            {
                ReliableUtility.InitializeContext(sharedProcessBuffer, sendProcessBuffer, recvProcessBuffer, m_ReliableParams);
            }
        }

        public void Initialize(ReliableUtility.Parameters param)
        {
            m_ReliableParams = param;
        }

        public int HeaderCapacity => UnsafeUtility.SizeOf<ReliableUtility.PacketHeader>();

        public int SharedStateCapacity => ReliableUtility.SharedCapacityNeeded(m_ReliableParams);
        public int ReceiveCapacity => ReliableUtility.ProcessCapacityNeeded(m_ReliableParams);
        public int SendCapacity => ReliableUtility.ProcessCapacityNeeded(m_ReliableParams);
    }
}
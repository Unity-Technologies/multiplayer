using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Networking.Transport.Utilities;

namespace Unity.Networking.Transport
{
    public struct UnreliableSequencedPipelineStage : INetworkPipelineStage
    {
        public NativeSlice<byte> Receive(NetworkPipelineContext ctx, NativeSlice<byte> inboundBuffer, ref bool needsResume, ref bool needsUpdate, ref bool needsSendUpdate)
        {
            needsResume = false;
            var reader = new DataStreamReader(inboundBuffer);
            var context = default(DataStreamReader.Context);
            unsafe
            {
                var oldSequenceId = (int*) ctx.internalProcessBuffer.GetUnsafePtr();
                ushort sequenceId = reader.ReadUShort(ref context);

                if (SequenceHelpers.GreaterThan16(sequenceId, (ushort)*oldSequenceId))
                {
                    *oldSequenceId = sequenceId;
                    // Skip over the part of the buffer which contains the header
                    return inboundBuffer.Slice(sizeof(ushort));
                }
            }
            return default(NativeSlice<byte>);
        }

        public InboundBufferVec Send(NetworkPipelineContext ctx, InboundBufferVec inboundBuffer, ref bool needsResume, ref bool needsUpdate)
        {
            needsResume = false;
            unsafe
            {
                var sequenceId = (int*) ctx.internalProcessBuffer.GetUnsafePtr();
                ctx.header.Write((ushort)*sequenceId);
                *sequenceId = (ushort)(*sequenceId + 1);
            }
            return inboundBuffer;
        }

        public void InitializeConnection(NativeSlice<byte> sendProcessBuffer, NativeSlice<byte> recvProcessBuffer, NativeSlice<byte> sharedProcessBuffer)
        {
            unsafe
            {
                if (recvProcessBuffer.Length > 0)
                {
                    // The receive processing buffer contains the current sequence ID, initialize it to -1 as it will be incremented when used.
                    *(int*) recvProcessBuffer.GetUnsafePtr() = -1;
                }
            }
        }

        public int ReceiveCapacity => sizeof(int);
        public int SendCapacity => sizeof(int);
        public int HeaderCapacity => sizeof(ushort);
        public int SharedStateCapacity { get; }
    }
}
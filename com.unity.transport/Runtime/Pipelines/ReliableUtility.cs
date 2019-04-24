using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Networking.Transport.Utilities
{
    public struct SequenceBufferContext
    {
        public int Sequence;
        public int Acked;
        public uint AckMask;
    }

    public struct ReliableUtility
    {
        public struct Statistics
        {
            public int PacketsReceived;
            public int PacketsSent;
            public int PacketsDropped;
            public int PacketsOutOfOrder;
            public int PacketsDuplicated;
            public int PacketsStale;
            public int PacketsResent;
        }

        public struct RTTInfo
        {
            public int LastRtt;
            public float SmoothedRtt;
            public float SmoothedVariance;
            public int ResendTimeout;
        }
        
        public const int NullEntry = -1;
        // The least amount of time we'll wait until a packet resend is performed
        // This is 4x16ms (assumes a 60hz update rate)
        public const int DefaultMinimumResendTime = 64;
        public const int MaximumResendTime = 200;
        
        public enum ErrorCodes
        {
            Stale_Packet = -1,
            Duplicated_Packet = -2,
            
            OutgoingQueueIsFull = -7,
            InsufficientMemory = -8
        }

        public enum PacketType : ushort
        {
            Payload = 0,
            Ack = 1
        }

        public struct SharedContext
        {
            public int WindowSize;
            public int MinimumResendTime;

            /// <summary>
            /// Context of sent packets, last sequence ID sent (-1), last ID of our sent packet acknowledged by
            /// remote peer, ackmask of acknowledged packets. This is used when determining if a resend
            /// is needed.
            /// </summary>
            public SequenceBufferContext SentPackets;
            /// <summary>
            /// Context of received packets, last sequence ID received, and ackmask of received packets. Acked is not used.
            /// This is sent back to the remote peer in the header when sending.
            /// </summary>
            public SequenceBufferContext ReceivedPackets;
            public Statistics stats;
            public ErrorCodes errorCode;

            // Timing information for calculating resend times for packets
            public RTTInfo RttInfo;
            public int TimerDataOffset;
            public int TimerDataStride;
            public int RemoteTimerDataOffset;
            public int RemoteTimerDataStride;
        }
        
        public struct Context
        {
            public int Capacity;
            public int Resume;
            public int Delivered;
            public int IndexStride;
            public int IndexPtrOffset;
            public int DataStride;
            public int DataPtrOffset;
            public long LastSentTime;
            public long PreviousTimestamp;
        }

        public struct Parameters : INetworkParameter
        {
            public int WindowSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PacketHeader
        {
            public ushort Type;
            public ushort ProcessingTime;
            public ushort SequenceId;
            public ushort AckedSequenceId;
            public uint AckMask;
        }
        
        [StructLayout(LayoutKind.Sequential)]
        public struct PacketInformation
        {
            public int SequenceId;
            public int Size;
            public long SendTime;
        }

        // Header is inside the total packet length (Buffer size)
        [StructLayout(LayoutKind.Explicit)]
        public unsafe struct Packet
        {
            internal const int Length = NetworkParameterConstants.MTU;
            [FieldOffset(0)] public PacketHeader Header;
            [FieldOffset(0)] public fixed byte Buffer[Length];
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PacketTimers
        {
            public ushort ProcessingTime;
            public ushort Padding;
            public int SequenceId;
            public long SentTime;
            public long ReceiveTime;
        }

        public static int SharedCapacityNeeded(Parameters param)
        {
            int capacityNeeded;
            unsafe
            {
                // Timers are stored for both remote packets (processing time) and local packets (round trip time)
                // The amount of timestamps needed in the queues is the same as the window size capacity
                var timerDataSize = sizeof(PacketTimers) * param.WindowSize * 2;
                capacityNeeded = sizeof(SharedContext) + timerDataSize;
            }
            return capacityNeeded;
        }
        
        public static int ProcessCapacityNeeded(Parameters param)
        {
            int capacityNeeded;
            unsafe
            {
                var infoSize = param.WindowSize * UnsafeUtility.SizeOf<PacketInformation>();
                var dataSize = param.WindowSize * Packet.Length;
                
                capacityNeeded = sizeof(Context)+ infoSize + dataSize;
            }
            return capacityNeeded;
        }

        public static unsafe SharedContext InitializeContext(NativeSlice<byte> sharedBuffer, NativeSlice<byte> sendBuffer, NativeSlice<byte> recvBuffer, Parameters param)
        {
            InitializeProcessContext(sendBuffer, param);
            InitializeProcessContext(recvBuffer, param);

            SharedContext* notifier = (SharedContext*) sharedBuffer.GetUnsafePtr();
            *notifier = new SharedContext
            {
                WindowSize = param.WindowSize,
                SentPackets = new SequenceBufferContext { Acked = NullEntry },
                MinimumResendTime = DefaultMinimumResendTime,
                ReceivedPackets = new SequenceBufferContext { Sequence = NullEntry },
                RttInfo = new RTTInfo { SmoothedVariance = 5, SmoothedRtt = 50, ResendTimeout = 50, LastRtt = 50},
                TimerDataOffset = UnsafeUtility.SizeOf<SharedContext>(),
                TimerDataStride = UnsafeUtility.SizeOf<PacketTimers>(),
                RemoteTimerDataOffset = UnsafeUtility.SizeOf<SharedContext>() + UnsafeUtility.SizeOf<PacketTimers>() * param.WindowSize,
                RemoteTimerDataStride = UnsafeUtility.SizeOf<PacketTimers>()
            };
            return *notifier;
        }

        public static unsafe int InitializeProcessContext(NativeSlice<byte> self, Parameters param)
        {
            int totalCapacity = ProcessCapacityNeeded(param);
            if (self.Length != totalCapacity)
            {
                return (int) ErrorCodes.InsufficientMemory;
            }
            Context* ctx = (Context*) self.GetUnsafePtr();

            ctx->Capacity = param.WindowSize;
            ctx->IndexStride = (UnsafeUtility.SizeOf<PacketInformation>() + 3) & ~3;
            ctx->IndexPtrOffset = sizeof(Context);
            ctx->DataStride = (Packet.Length + 3) & ~3;
            ctx->DataPtrOffset = ctx->IndexPtrOffset + (ctx->IndexStride * ctx->Capacity);
            ctx->Resume = NullEntry;
            ctx->Delivered = NullEntry;
            
            Release(self, 0, param.WindowSize);
            return 0;
        }

        public static unsafe void SetPacket(NativeSlice<byte> self, int sequence, NativeSlice<byte> data)
        {
            SetPacket(self, sequence, data.GetUnsafeReadOnlyPtr(), data.Length);
        }
        
        public static unsafe void SetPacket(NativeSlice<byte> self, int sequence, void* data, int length)
        {
            byte *ptr = (byte*)self.GetUnsafePtr();
            Context* ctx = (Context*) ptr;
            
            if (length > ctx->DataStride)
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                throw new OverflowException();
#else
                return;
#endif
            
            var index = sequence % ctx->Capacity;

            PacketInformation* info = GetPacketInformation(self, sequence); 
            info->SequenceId = sequence;
            info->Size = length;
            info->SendTime = -1;          // Not used for packets queued for resume receive

            var offset = ctx->DataPtrOffset + (index * ctx->DataStride);
            void* dataPtr = (ptr + offset);
            
            UnsafeUtility.MemCpy(dataPtr, data, length);
        }

        /// <summary>
        /// Write packet, packet header and tracking information to the given buffer space. This buffer
        /// should contain the reliability Context at the front, that contains the capacity of the buffer
        /// and pointer offsets needed to find the slots we can copy the packet to.
        /// </summary>
        /// <param name="self">Buffer space where we can store packets.</param>
        /// <param name="sequence">The sequence ID of the packet, this is used to find a slot inside the buffer.</param>
        /// <param name="header">The packet header which we'll store with the packet payload.</param>
        /// <param name="data">The packet data which we're storing.</param>
        /// <exception cref="OverflowException"></exception>
        public static unsafe void SetHeaderAndPacket(NativeSlice<byte> self, int sequence, PacketHeader header, InboundBufferVec data, long timestamp)
        {
            byte *ptr = (byte*)self.GetUnsafePtr();
            Context* ctx = (Context*) ptr;
            int totalSize = data.buffer1.Length + data.buffer2.Length;
            
            if (totalSize > ctx->DataStride)
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                throw new OverflowException();
#else
                return;
#endif
            var index = sequence % ctx->Capacity;
                
            PacketInformation* info = GetPacketInformation(self, sequence);
            info->SequenceId = sequence;
            info->Size = totalSize;
            info->SendTime = timestamp;

            Packet* packet = GetPacket(self, sequence);
            packet->Header = header;
            var offset = (ctx->DataPtrOffset + (index * ctx->DataStride)) + UnsafeUtility.SizeOf<PacketHeader>();
            void* dataPtr = (ptr + offset);

            if (data.buffer1.Length > 0)
                UnsafeUtility.MemCpy(dataPtr, data.buffer1.GetUnsafeReadOnlyPtr(), data.buffer1.Length);
            if (data.buffer2.Length > 0)
                UnsafeUtility.MemCpy(&dataPtr + data.buffer1.Length, data.buffer2.GetUnsafeReadOnlyPtr(), data.buffer2.Length);
        }

        public static unsafe PacketInformation* GetPacketInformation(NativeSlice<byte> self, int sequence)
        {
            byte *ptr = (byte*)self.GetUnsafePtr();
            Context* ctx = (Context*) ptr;
            var index = sequence % ctx->Capacity;

            return (PacketInformation*) ((ptr + ctx->IndexPtrOffset) + (index * ctx->IndexStride));
        }
        
        public static unsafe Packet* GetPacket(NativeSlice<byte> self, int sequence)
        {
            byte *ptr = (byte*)self.GetUnsafePtr();
            Context* ctx = (Context*) ptr;
            var index = sequence % ctx->Capacity;

            var offset = ctx->DataPtrOffset + (index * ctx->DataStride);
            return (Packet*) (ptr + offset);
        }

        public static unsafe bool TryAquire(NativeSlice<byte> self, int sequence)
        {
            Context* ctx = (Context*) self.GetUnsafePtr();

            var index = sequence % ctx->Capacity;

            var currentSequenceId = GetIndex(self, index);
            if (currentSequenceId == NullEntry)
            {
                SetIndex(self, index, sequence);
                return true;
            }
            return false;
        }

        public static unsafe void Release(NativeSlice<byte> self, int sequence)
        {
            Release(self, sequence, 1);
        }

        public static unsafe void Release(NativeSlice<byte> self, int start_sequence, int count)
        {
            Context* ctx = (Context*) self.GetUnsafePtr();
            var index = start_sequence % ctx->Capacity;
            for (int i = 0; i < count; i++)
            {
                SetIndex(self, index + i, NullEntry);
            }
        }
        
        static unsafe void SetIndex(NativeSlice<byte> self, int index, int sequence)
        {
            byte *ptr = (byte*)self.GetUnsafePtr();
            Context* ctx = (Context*) ptr;

            int* value = (int*) ((ptr + ctx->IndexPtrOffset) + (index * ctx->IndexStride));
            *value = sequence;
        }

        static unsafe int GetIndex(NativeSlice<byte> self, int index)
        {
            byte *ptr = (byte*)self.GetUnsafePtr();
            Context* ctx = (Context*) ptr;

            int* value = (int*) ((ptr + ctx->IndexPtrOffset) + (index * ctx->IndexStride));
            return *value;
        }

        /// <summary>
        /// Acknowledge the reception of packets which have been sent. The reliability
        /// shared context/state is updated when packets are received from the other end
        /// of the connection. The other side will update it's ackmask with which packets
        /// have been received (starting from last received sequence ID) each time it sends
        /// a packet back. This checks the resend timers on each non-acknowledged packet
        /// and notifies if it's time to resend yet.
        /// </summary>
        /// <param name="context">Pipeline context, contains the buffer slices this pipeline connection owns.</param>
        /// <returns></returns>
        public static unsafe bool ReleaseOrResumePackets(NetworkPipelineContext context)
        {
            SharedContext* reliable = (SharedContext*) context.internalSharedProcessBuffer.GetUnsafePtr();
            Context* ctx = (Context*) context.internalProcessBuffer.GetUnsafePtr();

            // Last sequence ID and ackmask we received from the remote peer, these are confirmed delivered packets
            var lastReceivedAckMask = reliable->SentPackets.AckMask;
            var lastOwnSequenceIdAckedByRemote = (ushort)reliable->SentPackets.Acked;

            // To deal with wrapping, chop off the upper half of the sequence ID and multiply by window size, it
            // will then never wrap but will map to the correct index in the packet storage, wrapping happens when
            // sending low sequence IDs (since it checks sequence IDs backwards in time).
            var sequence = (ushort)(reliable->WindowSize * ((1 - lastOwnSequenceIdAckedByRemote)>>15));

            // Check each slot in the window, starting from the sequence ID calculated above (this isn't the
            // latest sequence ID though as it was adjusted to avoid wrapping)
            for (int i = 0; i < reliable->WindowSize; i++)
            {
                var info = GetPacketInformation(context.internalProcessBuffer, sequence);
                if (info->SequenceId >= 0)
                {
                    // Check the bit for this sequence ID against the ackmask. Bit 0 in the ackmask is the latest
                    // ackedSeqId, bit 1 latest ackedSeqId - 1 (one older) and so on. If bit X is 1 then ackedSeqId-X is acknowledged
                    var ackBits = 1 << (lastOwnSequenceIdAckedByRemote - info->SequenceId);

                    // Release if this seqId has been flipped on in the ackmask (so it's acknowledged)
                    // Ignore if sequence ID is out of window range of the last acknowledged id
                    if (SequenceHelpers.AbsDistance((ushort)lastOwnSequenceIdAckedByRemote, (ushort)info->SequenceId) < reliable->WindowSize && (ackBits & lastReceivedAckMask) != 0)
                    {
                        Release(context.internalProcessBuffer, info->SequenceId);
                        info->SendTime = -1;
                        sequence = (ushort) (sequence - 1);
                        continue;
                    }
                    var timeToResend = CurrentResendTime(context.internalSharedProcessBuffer);
                    if (context.timestamp > info->SendTime + timeToResend)
                    {
                        ctx->Resume = info->SequenceId;
                    }
                }
                sequence = (ushort) (sequence - 1);
            }
            return ctx->Resume != NullEntry;
        }

        /// <summary>
        /// Resume or play back a packet we had received earlier out of order. When an out of order packet is received
        /// it is stored since we need to first return the packet with the next sequence ID. When that packet finally
        /// arrives it is returned but a pipeline resume is requested since we already have the next packet stored
        /// and it can be processed immediately after.
        /// </summary>
        /// <param name="context">Pipeline context, we'll use both the shared reliability context and receive context.</param>
        /// <param name="startSequence">The first packet which we need to retrieve now, there could be more after that.</param>
        /// <param name="needsResume">Indicates if we need the pipeline to resume again.</param>
        /// <returns></returns>
        public static unsafe NativeSlice<byte> ResumeReceive(NetworkPipelineContext context, int startSequence, ref bool needsResume)
        {
            if (startSequence == NullEntry) return default(NativeSlice<byte>);
            
            SharedContext* shared = (SharedContext*) context.internalSharedProcessBuffer.GetUnsafePtr();
            Context* reliable = (Context*)context.internalProcessBuffer.GetUnsafePtr();
            
            reliable->Resume = NullEntry;

            PacketInformation* info = GetPacketInformation(context.internalProcessBuffer, startSequence);
            var latestReceivedPacket = shared->ReceivedPackets.Sequence;
            if (info->SequenceId == startSequence)
            {
                var offset = reliable->DataPtrOffset + ((startSequence % reliable->Capacity) * reliable->DataStride);
                NativeSlice<byte> slice = new NativeSlice<byte>(context.internalProcessBuffer, offset, info->Size);
                reliable->Delivered = startSequence;
                
                if ((ushort)(startSequence + 1) <= latestReceivedPacket)
                {
                    reliable->Resume = (ushort)(startSequence + 1);
                    needsResume = true;
                }
                return slice;
            }
            return default(NativeSlice<byte>);
        }

        /// <summary>
        /// Resend a packet which we have not received an acknowledgement for in time. Pipeline resume
        /// will be enabled if there are more packets which we need to resend. The send reliability context
        /// will then also be updated to track the next packet we need to resume.
        /// </summary>
        /// <param name="context">Pipeline context, we'll use both the shared reliability context and send context.</param>
        /// <param name="header">Packet header for the packet payload we're resending.</param>
        /// <param name="needsResume">Indicates if a pipeline resume is needed again.</param>
        /// <returns>Buffer slice to packet payload.</returns>
        /// <exception cref="ApplicationException"></exception>
        public static unsafe NativeSlice<byte> ResumeSend(NetworkPipelineContext context, out PacketHeader header, ref bool needsResume)
        {
            SharedContext* reliable = (SharedContext*) context.internalSharedProcessBuffer.GetUnsafePtr();
            Context* ctx = (Context*)context.internalProcessBuffer.GetUnsafePtr();
            
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (ctx->Resume == NullEntry)
                throw new ApplicationException("This function should not be called unless there is data in resume");
#endif
            
            var sequence = (ushort) ctx->Resume;

            PacketInformation* information;
            information = GetPacketInformation(context.internalProcessBuffer, sequence);
            // Reset the resend timer
            information->SendTime = context.timestamp;
            
            Packet *packet = GetPacket(context.internalProcessBuffer, sequence);
            header = packet->Header;

            // Update acked/ackmask to latest values
            header.AckedSequenceId = (ushort) reliable->ReceivedPackets.Sequence;
            header.AckMask = reliable->ReceivedPackets.AckMask;

            var offset = (ctx->DataPtrOffset + ((sequence % ctx->Capacity) * ctx->DataStride)) + UnsafeUtility.SizeOf<PacketHeader>();

            NativeSlice<byte> slice = new NativeSlice<byte>(context.internalProcessBuffer, offset, information->Size);
            reliable->stats.PacketsResent++;

            needsResume = false;
            ctx->Resume = -1;

            // Check if another packet needs to be resent right after this one
            for (int i = sequence + 1; i < reliable->ReceivedPackets.Sequence + 1; i++)
            {
                var timeToResend = CurrentResendTime(context.internalSharedProcessBuffer);
                information = GetPacketInformation(context.internalProcessBuffer, i);
                if (information->SequenceId >= 0 && information->SendTime + timeToResend > context.timestamp)
                {
                    needsResume = true;
                    ctx->Resume = i;
                }
            }
            return slice;
        }

        /// <summary>
        /// Store the packet for possible later resends, and fill in the header we'll use to send it (populate with
        /// sequence ID, last acknowledged ID from remote with ackmask.
        /// </summary>
        /// <param name="context">Pipeline context, the reliability shared state is used here.</param>
        /// <param name="inboundBuffer">Buffer with packet data.</param>
        /// <param name="header">Packet header which will be populated.</param>
        /// <returns>Sequence ID assigned to this packet.</returns>
        public static unsafe int Write(NetworkPipelineContext context, InboundBufferVec inboundBuffer, ref PacketHeader header)
        {
            SharedContext* reliable = (SharedContext*) context.internalSharedProcessBuffer.GetUnsafePtr();

            var sequence = (ushort) reliable->SentPackets.Sequence;

            if (!TryAquire(context.internalProcessBuffer, sequence))
            {
                reliable->errorCode = ErrorCodes.OutgoingQueueIsFull;
                return (int)ErrorCodes.OutgoingQueueIsFull;
            }
            reliable->stats.PacketsSent++;

            header.SequenceId = sequence;
            header.AckedSequenceId = (ushort) reliable->ReceivedPackets.Sequence;
            header.AckMask = reliable->ReceivedPackets.AckMask;

            reliable->ReceivedPackets.Acked = reliable->ReceivedPackets.Sequence;

            // Attach our processing time of the packet we're acknowledging (time between receiving it and sending this ack)
            header.ProcessingTime =
                CalculateProcessingTime(context.internalSharedProcessBuffer, header.AckedSequenceId, context.timestamp);

            reliable->SentPackets.Sequence = (ushort) (reliable->SentPackets.Sequence + 1);
            SetHeaderAndPacket(context.internalProcessBuffer, sequence, header, inboundBuffer, context.timestamp);

            StoreTimestamp(context.internalSharedProcessBuffer, sequence, context.timestamp);

            return sequence;
        }

        /// <summary>
        /// Write an ack packet, only the packet header is used and this doesn't advance the sequence ID.
        /// The packet is not stored away for resend routine.
        /// </summary>
        /// <param name="context">Pipeline context, the reliability shared state is used here.</param>
        /// <param name="header">Packet header which will be populated.</param>
        /// <returns></returns>
        public static unsafe void WriteAckPacket(NetworkPipelineContext context, ref PacketHeader header)
        {
            SharedContext* reliable = (SharedContext*) context.internalSharedProcessBuffer.GetUnsafePtr();

            header.Type = (ushort)PacketType.Ack;
            header.AckedSequenceId = (ushort) reliable->ReceivedPackets.Sequence;
            header.AckMask = reliable->ReceivedPackets.AckMask;
            header.ProcessingTime =
                CalculateProcessingTime(context.internalSharedProcessBuffer, header.AckedSequenceId, context.timestamp);
            reliable->ReceivedPackets.Acked = reliable->ReceivedPackets.Sequence;
        }

        public static unsafe void StoreTimestamp(NativeSlice<byte> sharedBuffer, ushort sequenceId, long timestamp)
        {
            var timerData = GetLocalPacketTimer(sharedBuffer, sequenceId);
            timerData->SequenceId = sequenceId;
            timerData->SentTime = timestamp;
            timerData->ProcessingTime = 0;
            timerData->ReceiveTime = 0;
        }

        public static unsafe void StoreReceiveTimestamp(NativeSlice<byte> sharedBuffer, ushort sequenceId, long timestamp, ushort processingTime)
        {
            var sharedCtx = (SharedContext*) sharedBuffer.GetUnsafePtr();
            var rttInfo = sharedCtx->RttInfo;
            var timerData = GetLocalPacketTimer(sharedBuffer, sequenceId);
            if (timerData != null && timerData->SequenceId == sequenceId)
            {
                // Ignore the receive time if we've already received it (remote doesn't have new acks)
                if (timerData->ReceiveTime > 0)
                    return;
                timerData->ReceiveTime = timestamp;
                timerData->ProcessingTime = processingTime;

                rttInfo.LastRtt = (int)Math.Max(timerData->ReceiveTime - timerData->SentTime - timerData->ProcessingTime, 1);
                var delta = rttInfo.LastRtt - rttInfo.SmoothedRtt;
                rttInfo.SmoothedRtt += delta / 8;
                rttInfo.SmoothedVariance += (Math.Abs(delta) - rttInfo.SmoothedVariance) / 4;
                rttInfo.ResendTimeout = (int)(rttInfo.SmoothedRtt + 4 * rttInfo.SmoothedVariance);
                sharedCtx->RttInfo = rttInfo;
            }
        }

        public static unsafe void StoreRemoteReceiveTimestamp(NativeSlice<byte> sharedBuffer, ushort sequenceId, long timestamp)
        {
            var timerData = GetRemotePacketTimer(sharedBuffer, sequenceId);
            timerData->SequenceId = sequenceId;
            timerData->ReceiveTime = timestamp;
        }

        static unsafe int CurrentResendTime(NativeSlice<byte> sharedBuffer)
        {
            var sharedCtx = (SharedContext*) sharedBuffer.GetUnsafePtr();
            if (sharedCtx->RttInfo.ResendTimeout > MaximumResendTime)
                return MaximumResendTime;
            return Math.Max(sharedCtx->RttInfo.ResendTimeout, sharedCtx->MinimumResendTime);
        }

        public static unsafe ushort CalculateProcessingTime(NativeSlice<byte> sharedBuffer, ushort sequenceId, long timestamp)
        {
            // Look up previously recorded receive timestamp, subtract that from current timestamp and return as processing time
            var timerData = GetRemotePacketTimer(sharedBuffer, sequenceId);
            if (timerData != null && timerData->SequenceId == sequenceId)
                return Math.Min((ushort) (timestamp - timerData->ReceiveTime), ushort.MaxValue);
            return 0;
        }

        public static unsafe PacketTimers* GetLocalPacketTimer(NativeSlice<byte> sharedBuffer, ushort sequenceId)
        {
            var sharedCtx = (SharedContext*) sharedBuffer.GetUnsafePtr();
            var index = sequenceId % sharedCtx->WindowSize;
            var timerPtr = (long)sharedBuffer.GetUnsafePtr() + sharedCtx->TimerDataOffset + sharedCtx->TimerDataStride * index;
            return (PacketTimers*) timerPtr;
        }

        public static unsafe PacketTimers* GetRemotePacketTimer(NativeSlice<byte> sharedBuffer, ushort sequenceId)
        {
            var sharedCtx = (SharedContext*) sharedBuffer.GetUnsafePtr();
            var index = sequenceId % sharedCtx->WindowSize;
            var timerPtr = (long)sharedBuffer.GetUnsafePtr() + sharedCtx->RemoteTimerDataOffset + sharedCtx->RemoteTimerDataStride * index;
            return (PacketTimers*) timerPtr;
        }

        /// <summary>
        /// Read header data and update reliability tracking information in the shared context.
        /// - If the packets sequence ID is lower than the last received ID+1, then it's stale
        /// - If the packets sequence ID is higher, then we'll process it and update tracking info in the shared context
        /// </summary>
        /// <param name="context">Pipeline context, the reliability shared state is used here.</param>
        /// <param name="header">Packet header of a new received packet.</param>
        /// <returns>Sequence ID of the received packet.</returns>
        public static unsafe int Read(NetworkPipelineContext context, PacketHeader header)
        {
            SharedContext* reliable = (SharedContext*) context.internalSharedProcessBuffer.GetUnsafePtr();

            reliable->stats.PacketsReceived++;
            if (SequenceHelpers.StalePacket(
                header.SequenceId,
                (ushort) (reliable->ReceivedPackets.Sequence + 1),
                (ushort) reliable->WindowSize))
            {
                reliable->stats.PacketsStale++;
                return (int) ErrorCodes.Stale_Packet;
            }

            var window = reliable->WindowSize - 1;
            if (SequenceHelpers.GreaterThan16((ushort) (header.SequenceId + 1), (ushort) reliable->ReceivedPackets.Sequence))
            {
                int distance = SequenceHelpers.AbsDistance(header.SequenceId, (ushort)reliable->ReceivedPackets.Sequence);

                for (var i = 0; i < Math.Min(distance, window); ++i)
                {
                    if ((reliable->ReceivedPackets.AckMask & 1 << (window - i)) == 0)
                    {
                        reliable->stats.PacketsDropped++;
                    }
                }

                if (distance > window)
                {
                    reliable->stats.PacketsDropped += distance - window;
                    reliable->ReceivedPackets.AckMask = 1;
                }
                else
                {
                    reliable->ReceivedPackets.AckMask <<= distance;
                    reliable->ReceivedPackets.AckMask |= 1;
                }

                reliable->ReceivedPackets.Sequence = header.SequenceId;
            }
            else if (SequenceHelpers.LessThan16(header.SequenceId, (ushort) reliable->ReceivedPackets.Sequence))
            {
                int distance = SequenceHelpers.AbsDistance(header.SequenceId, (ushort)reliable->ReceivedPackets.Sequence);
                // If this is a resent packet the distance will seem very big and needs to be calculated again with adjustment for wrapping
                if (distance >= ushort.MaxValue - reliable->WindowSize)
                    distance = reliable->ReceivedPackets.Sequence - header.SequenceId;

                var ackBit = 1 << distance;
                if ((ackBit & reliable->ReceivedPackets.AckMask) != 0)
                {
                    reliable->stats.PacketsDuplicated++;
                    return (int) ErrorCodes.Duplicated_Packet;
                }

                reliable->stats.PacketsOutOfOrder++;
                reliable->ReceivedPackets.AckMask |= (uint) ackBit;
            }

            // Store receive timestamp for remote sequence ID we just received
            StoreRemoteReceiveTimestamp(context.internalSharedProcessBuffer, header.SequenceId, context.timestamp);

            ReadAckPacket(context, header);

            return header.SequenceId;
        }

        public static unsafe void ReadAckPacket(NetworkPipelineContext context, PacketHeader header)
        {
            SharedContext* reliable = (SharedContext*) context.internalSharedProcessBuffer.GetUnsafePtr();

            // Store receive timestamp for our acked sequence ID with remote processing time
            StoreReceiveTimestamp(context.internalSharedProcessBuffer, header.AckedSequenceId, context.timestamp, header.ProcessingTime);

            // Check the distance of the acked seqId in the header, if it's too far away from last acked packet we
            // can't process it and add it to the ack mask
            if (!SequenceHelpers.GreaterThan16(header.AckedSequenceId, (ushort) reliable->SentPackets.Acked))
            {
                // No new acks;
                return;
            }

            reliable->SentPackets.Acked = header.AckedSequenceId;
            reliable->SentPackets.AckMask = header.AckMask;
        }

        public static unsafe bool ShouldSendAck(NetworkPipelineContext ctx)
        {
            var reliable = (Context*) ctx.internalProcessBuffer.GetUnsafePtr();
            var shared = (SharedContext*) ctx.internalSharedProcessBuffer.GetUnsafePtr();

            // If more than one full frame (timestamp - prevTimestamp = one frame) has elapsed then send ack packet
            // and if the last received sequence ID has not been acked yet
            if (reliable->LastSentTime < reliable->PreviousTimestamp &&
                shared->ReceivedPackets.Acked < shared->ReceivedPackets.Sequence)
                return true;
            return false;
        }

        public static unsafe void SetMinimumResendTime(int value, UdpNetworkDriver driver,
            NetworkPipeline pipeline, int stageId, NetworkConnection con)
        {
            NativeSlice<byte> receiveBuffer = default(NativeSlice<byte>);
            NativeSlice<byte> sendBuffer = default(NativeSlice<byte>);
            NativeSlice<byte> sharedBuffer = default(NativeSlice<byte>);
            driver.GetPipelineBuffers(pipeline, stageId, con, ref receiveBuffer, ref sendBuffer, ref sharedBuffer);
            var sharedCtx = (ReliableUtility.SharedContext*) sharedBuffer.GetUnsafePtr();
            sharedCtx->MinimumResendTime = value;
        }

        public static unsafe void SetMinimumResendTime(int value, LocalNetworkDriver driver,
            NetworkPipeline pipeline, int stageId, NetworkConnection con)
        {
            NativeSlice<byte> receiveBuffer = default(NativeSlice<byte>);
            NativeSlice<byte> sendBuffer = default(NativeSlice<byte>);
            NativeSlice<byte> sharedBuffer = default(NativeSlice<byte>);
            driver.GetPipelineBuffers(pipeline, stageId, con, ref receiveBuffer, ref sendBuffer, ref sharedBuffer);
            var sharedCtx = (ReliableUtility.SharedContext*) sharedBuffer.GetUnsafePtr();
            sharedCtx->MinimumResendTime = value;
        }
    }
 }

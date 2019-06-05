using Unity.Entities;
using Unity.Networking.Transport.Utilities;

public struct NetworkSnapshotAckComponent : IComponentData
{
    public void UpdateReceivedByRemote(uint tick, uint mask)
    {
        if (LastReceivedSnapshotByRemote == 0)
        {
            ReceivedSnapshotByRemoteMask0 = mask;
            LastReceivedSnapshotByRemote = tick;
        }
        else if (SequenceHelpers.IsNewer(tick, LastReceivedSnapshotByRemote))
        {
            // TODO: this assumes the delta between acks is less than 64
            int shamt = (int)(tick - LastReceivedSnapshotByRemote);
            ReceivedSnapshotByRemoteMask3 = (ReceivedSnapshotByRemoteMask3 << shamt) |
                                            (ReceivedSnapshotByRemoteMask2 >> (64 - shamt));
            ReceivedSnapshotByRemoteMask2 = (ReceivedSnapshotByRemoteMask2 << shamt) |
                                            (ReceivedSnapshotByRemoteMask1 >> (64 - shamt));
            ReceivedSnapshotByRemoteMask1 = (ReceivedSnapshotByRemoteMask1 << shamt) |
                                            (ReceivedSnapshotByRemoteMask0 >> (64 - shamt));
            ReceivedSnapshotByRemoteMask0 = (ReceivedSnapshotByRemoteMask0 << shamt) |
                                            mask;
            LastReceivedSnapshotByRemote = tick;
        }
    }

    public bool IsReceivedByRemote(uint tick)
    {
        if (tick == 0 || LastReceivedSnapshotByRemote == 0)
            return false;
        if (SequenceHelpers.IsNewer(tick, LastReceivedSnapshotByRemote))
            return false;
        int bit = (int)(LastReceivedSnapshotByRemote - tick);
        if (bit >= 256)
            return false;
        if (bit >= 192)
        {
            bit -= 192;
            return (ReceivedSnapshotByRemoteMask3 & (1ul << bit)) != 0;
        }
        if (bit >= 128)
        {
            bit -= 128;
            return (ReceivedSnapshotByRemoteMask2 & (1ul << bit)) != 0;
        }
        if (bit >= 64)
        {
            bit -= 64;
            return (ReceivedSnapshotByRemoteMask1 & (1ul << bit)) != 0;
        }
        return (ReceivedSnapshotByRemoteMask0 & (1ul << bit)) != 0;
    }
    public uint LastReceivedSnapshotByRemote;
    private ulong ReceivedSnapshotByRemoteMask0;
    private ulong ReceivedSnapshotByRemoteMask1;
    private ulong ReceivedSnapshotByRemoteMask2;
    private ulong ReceivedSnapshotByRemoteMask3;
    public uint LastReceivedSnapshotByLocal;
    public uint ReceivedSnapshotByLocalMask;

    public void UpdateRemoteTime(uint remoteTime, uint localTimeMinusRTT, uint localTime)
    {
        if (remoteTime != 0 && SequenceHelpers.IsNewer(remoteTime, LastReceivedRemoteTime))
        {
            LastReceivedRemoteTime = remoteTime;
            LastReceivedRTT = localTime - localTimeMinusRTT;
            LastReceiveTimestamp = localTime;
        }
    }
    public uint LastReceivedRemoteTime;
    public uint LastReceivedRTT;
    public uint LastReceiveTimestamp;
}


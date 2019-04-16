using System;
using System.Runtime.InteropServices;

namespace Unity.Networking.Transport.Protocols
{
    public enum UdpCProtocol
    {
        ConnectionRequest,
        ConnectionReject,
        ConnectionAccept,
        Disconnect,
        Data
    }

    [StructLayout(LayoutKind.Explicit)]
    public unsafe struct UdpCHeader
    {
        [Flags]
        public enum HeaderFlags : byte
        {
            HasConnectToken = 0x1,
            HasPipeline = 0x2
        }

        public const int Length = 4;
        [FieldOffset(0)] public fixed byte Data[Length];
        [FieldOffset(0)] public byte Type;
        [FieldOffset(1)] public HeaderFlags Flags;
        [FieldOffset(2)] public ushort SessionToken;
    }
}
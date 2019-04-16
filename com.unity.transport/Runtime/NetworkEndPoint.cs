using System;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Networking.Transport
{
    /// <summary>
    /// NetworkFamily indicates what type of underlying medium we are using.
    /// Currently supported are <see cref="NetworkFamily.UdpIpv4"/> and <see cref="NetworkFamily.IPC"/>
    /// </summary>
    public enum NetworkFamily
    {
        UdpIpv4 = 2,//AddressFamily.InterNetwork,
        IPC = 0//AddressFamily.Unspecified
    }

    /// <summary>
    /// The NetworkEndPoint is our representation of the <see cref="System.Net.IPEndPoint"/> type.
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    public unsafe struct NetworkEndPoint
    {
        internal const int Length = 28;
        [FieldOffset(0)] internal fixed byte data[Length];
        [FieldOffset(0)] internal sa_family_t family;
        [FieldOffset(2)] internal ushort nbo_port;
        [FieldOffset(4)] internal int ipc_handle;
        [FieldOffset(28)] internal int length;

        private static bool IsLittleEndian = true;
        static NetworkEndPoint()
        {
            uint test = 1;
            unsafe
            {
                byte* test_b = (byte*) &test;
                IsLittleEndian = test_b[0] == 1;
            }
        }

        private static ushort ByteSwap(ushort val)
        {
            return (ushort)(((val & 0xff) << 8) | (val >> 8));
        }
        private static uint ByteSwap(uint val)
        {
            return (uint)(((val & 0xff) << 24) |((val&0xff00)<<8) | ((val>>8)&0xff00) | (val >> 24));
        }
        public ushort Port
        {
            get { return IsLittleEndian ? ByteSwap(nbo_port) : nbo_port; }
            set { nbo_port = IsLittleEndian ? ByteSwap(value) : value; }
        }

        public NetworkFamily Family
        {
            get => (NetworkFamily) family.sa_family;
#if (UNITY_EDITOR_OSX || ((UNITY_STANDALONE_OSX || UNITY_IOS) && !UNITY_EDITOR))
            set => family.sa_family = (byte) value;
#else
            set => family.sa_family = (ushort)value;
#endif
        }

        public bool IsValid => Family != 0;

        private static NetworkEndPoint CreateIpv4(uint ip, ushort port)
        {
            if (IsLittleEndian)
            {
                port = ByteSwap(port);
                ip = ByteSwap(ip);
            }

            var sai = new sockaddr_in();

#if (UNITY_EDITOR_OSX || ((UNITY_STANDALONE_OSX || UNITY_IOS) && !UNITY_EDITOR))
            sai.sin_family.sa_family = (byte) NetworkFamily.UdpIpv4;
            sai.sin_family.sa_len = (byte) sizeof(sockaddr_in);
#else
            sai.sin_family.sa_family = (ushort) NetworkFamily.UdpIpv4;
#endif
            sai.sin_port = port;
            sai.sin_addr.s_addr = ip;

            var len = sizeof(sockaddr_in);
            var address = new NetworkEndPoint
            {
                length = len
            };

            UnsafeUtility.MemCpy(address.data, sai.data, len);
            return address;
        }

        public static NetworkEndPoint AnyIpv4 => CreateIpv4(0, 0);
        public static NetworkEndPoint LoopbackIpv4 => CreateIpv4((127<<24) | 1, 0);
        public static NetworkEndPoint Parse(string ip, ushort port)
        {
            uint ipaddr = 0;
            int pos = 0;
            for (int part = 0; part < 4; ++part)
            {
                if (pos >= ip.Length || ip[pos] < '0' || ip[pos] > '9')
                {
                    // Parsing failed
                    ipaddr = 0;
                    break;
                }
                uint byteVal = 0;
                while (pos < ip.Length && ip[pos] >= '0' && ip[pos] <= '9')
                {
                    byteVal = (byteVal * 10) + (uint) (ip[pos] - '0');
                    ++pos;
                }
                if (byteVal > 255)
                {
                    // Parsing failed
                    ipaddr = 0;
                    break;
                }

                ipaddr = (ipaddr << 8) | byteVal;

                if (pos < ip.Length && ip[pos] == '.')
                    ++pos;
            }

            return CreateIpv4(ipaddr, port);
        }

        public static bool operator ==(NetworkEndPoint lhs, NetworkEndPoint rhs)
        {
            return lhs.Compare(rhs);
        }

        public static bool operator !=(NetworkEndPoint lhs, NetworkEndPoint rhs)
        {
            return !lhs.Compare(rhs);
        }

        public override bool Equals(object other)
        {
            return this == (NetworkEndPoint) other;
        }

        public override int GetHashCode()
        {
            fixed (byte* p = data)
                unchecked
                {
                    var result = 0;

                    for (int i = 0; i < Length; i++)
                    {
                        result = (result * 31) ^ (int)(IntPtr) (p + 1);
                    }

                    return result;
                }
        }

        bool Compare(NetworkEndPoint other)
        {
            if (length != other.length)
                return false;

            fixed (void* p = this.data)
            {
                if (UnsafeUtility.MemCmp(p, other.data, length) == 0)
                    return true;
            }

            return false;
        }
    }
}
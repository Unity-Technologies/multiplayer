using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Networking.Transport.LowLevel.Unsafe;
using Unity.Networking.Transport.Protocols;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace Unity.Networking.Transport
{
#if UNITY_ZEROPLAYER
    public class NetworkInterfaceException : Exception
    {
        public NetworkInterfaceException(int err)
            : base("Socket error: " + err)
        {
        }
    }
#else
    public class NetworkInterfaceException : System.Net.Sockets.SocketException
    {
        public NetworkInterfaceException(int err)
            : base(err)
        {
        }
    }
#endif
    /// <summary>
    /// The INetworkPacketReceiver is an interface for handling received packets, needed by the <see cref="INetworkInterface"/>
    /// </summary>
    public interface INetworkPacketReceiver
    {
        int ReceiveCount { get; set; }
        /// <summary>
        /// AppendPacket is where we parse the data from the network into easy to handle events.
        /// </summary>
        /// <param name="address">The address of the endpoint we received data from.</param>
        /// <param name="header">The header data indicating what type of packet it is. <see cref="UdpCHeader"/> for more information.</param>
        /// <param name="dataLen">The size of the payload, if any.</param>
        /// <returns></returns>
        int AppendPacket(NetworkEndPoint address, UdpCHeader header, int dataLen);

        /// <summary>
        /// Get the datastream associated with this Receiver.
        /// </summary>
        /// <returns>Returns a <see cref="DataStreamWriter"/></returns>
        DataStreamWriter GetDataStream();
        /// <summary>
        /// Check if the the DataStreamWriter uses dynamic allocations to automatically resize the buffers or not.
        /// </summary>
        /// <returns>True if its dynamically resizing the DataStreamWriter</returns>
        bool DynamicDataStreamSize();

        int ReceiveErrorCode { set; }
    }

    public interface INetworkInterface : IDisposable
    {
        /// <summary>
        /// The Interface Family is used to indicate what type of medium we are trying to use. See <see cref="NetworkFamily"/>
        /// </summary>
        NetworkFamily Family { get; }

        NetworkEndPoint LocalEndPoint { get; }
        NetworkEndPoint RemoteEndPoint { get; }

        void Initialize();

        /// <summary>
        /// Schedule a ReceiveJob. This is used to read data from your supported medium and pass it to the AppendData function
        /// supplied by the <see cref="INetworkPacketReceiver"/>
        /// </summary>
        /// <param name="receiver">A <see cref="INetworkPacketReceiver"/> used to parse the data received.</param>
        /// <param name="dep">A <see cref="JobHandle"/> to any dependency we might have.</param>
        /// <typeparam name="T">Need to be of type <see cref="INetworkPacketReceiver"/></typeparam>
        /// <returns>A <see cref="JobHandle"/> to our newly created ScheduleReceive Job.</returns>
        JobHandle ScheduleReceive<T>(T receiver, JobHandle dep) where T : struct, INetworkPacketReceiver;

        /// <summary>
        /// Binds the medium to a specific endpoint.
        /// </summary>
        /// <param name="endpoint">
        /// A valid <see cref="NetworkEndPoint"/>, can be implicitly cast using an <see cref="System.Net.IPEndPoint"/>.
        /// </param>
        /// <returns>0 on Success</returns>
        int Bind(NetworkEndPoint endpoint);

        /// <summary>
        /// Sends a message using the underlying medium.
        /// </summary>
        /// <param name="iov">An array of <see cref="network_iovec"/>.</param>
        /// <param name="iov_len">Lenght of the iov array passed in.</param>
        /// <param name="address">The address of the remote host we want to send data to.</param>
        /// <returns></returns>
        unsafe int SendMessage(network_iovec* iov, int iov_len, ref NetworkEndPoint address);
    }

    public struct IPv4UDPSocket : INetworkInterface
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        private class SocketList
        {
            public HashSet<long> OpenSockets = new HashSet<long>();

            ~SocketList()
            {
                foreach (var socket in OpenSockets)
                {
                    long sockHand = socket;
                    int errorcode = 0;
                    NativeBindings.network_close(ref sockHand, ref errorcode);
                }
            }
        }
        private static SocketList AllSockets = new SocketList();
#endif

        private long m_SocketHandle;
        private NetworkEndPoint m_RemoteEndPoint;

        public NetworkFamily Family => NetworkFamily.UdpIpv4;

        public unsafe NetworkEndPoint LocalEndPoint
        {
            get
            {
                var localEndPoint = new NetworkEndPoint {length = sizeof(NetworkEndPoint)};
                int errorcode = 0;
                var result = NativeBindings.network_get_socket_address(m_SocketHandle, ref localEndPoint, ref errorcode);
                if (result != 0)
                {
                    throw new NetworkInterfaceException(errorcode);
                }
                return localEndPoint;
            }
        }

        public NetworkEndPoint RemoteEndPoint {
            get { return m_RemoteEndPoint; }
        }

        public void Initialize()
        {
            NativeBindings.network_initialize();
            int ret = CreateAndBindSocket(out m_SocketHandle, NetworkEndPoint.AnyIpv4);
            if (ret != 0)
                throw new NetworkInterfaceException(ret);
        }

        public void Dispose()
        {
            Close();
            NativeBindings.network_terminate();
        }

        [BurstCompile]
        struct ReceiveJob<T> : IJob where T : struct, INetworkPacketReceiver
        {
            public T receiver;
            public long socket;

            public unsafe void Execute()
            {
                var address = new NetworkEndPoint {length = sizeof(NetworkEndPoint)};
                var header = new UdpCHeader();
                var stream = receiver.GetDataStream();
                receiver.ReceiveCount = 0;
                receiver.ReceiveErrorCode = 0;

                while (true)
                {
                    if (receiver.DynamicDataStreamSize())
                    {
                        while (stream.Length+NetworkParameterConstants.MTU >= stream.Capacity)
                            stream.Capacity *= 2;
                    }
                    else if (stream.Length >= stream.Capacity)
                        return;
                    var sliceOffset = stream.Length;
                    var result = NativeReceive(ref header, stream.GetUnsafePtr() + sliceOffset,
                        Math.Min(NetworkParameterConstants.MTU, stream.Capacity - stream.Length), ref address);
                    if (result <= 0)
                        return;
                    receiver.ReceiveCount += receiver.AppendPacket(address, header, result);
                }

            }

            unsafe int NativeReceive(ref UdpCHeader header, void* data, int length, ref NetworkEndPoint address)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (length <= 0)
                    throw new ArgumentException("Can't receive into 0 bytes or less of buffer memory");
#endif
                var iov = stackalloc network_iovec[2];

                fixed (byte* ptr = header.Data)
                {
                    iov[0].buf = ptr;
                    iov[0].len = UdpCHeader.Length;

                    iov[1].buf = data;
                    iov[1].len = length;
                }

                int errorcode = 0;
                var result = NativeBindings.network_recvmsg(socket, iov, 2, ref address, ref errorcode);
                if (result == -1)
                {
                    if (errorcode == 10035 || errorcode == 35 || errorcode == 11)
                        return 0;

                    receiver.ReceiveErrorCode = errorcode;
                }
                return result;
            }
        }

        public JobHandle ScheduleReceive<T>(T receiver, JobHandle dep) where T : struct, INetworkPacketReceiver
        {
            var job = new ReceiveJob<T> {receiver = receiver, socket = m_SocketHandle};
            return job.Schedule(dep);
        }

        public int Bind(NetworkEndPoint endpoint)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (endpoint.Family != NetworkFamily.UdpIpv4)
                throw new InvalidOperationException();
#endif

            long newSocket;
            int ret = CreateAndBindSocket(out newSocket, endpoint);
            if (ret != 0)
                return ret;
            Close();

            m_RemoteEndPoint = endpoint;
            m_SocketHandle = newSocket;

            return 0;
        }

        private void Close()
        {
            if (m_SocketHandle < 0)
                return;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AllSockets.OpenSockets.Remove(m_SocketHandle);
#endif
            int errorcode = 0;
            NativeBindings.network_close(ref m_SocketHandle, ref errorcode);
            m_RemoteEndPoint = default(NetworkEndPoint);
            m_SocketHandle = -1;
        }

        public unsafe int SendMessage(network_iovec* iov, int iov_len, ref NetworkEndPoint address)
        {
            int errorcode = 0;
            return NativeBindings.network_sendmsg(m_SocketHandle, iov, iov_len, ref address, ref errorcode);
        }

        int CreateAndBindSocket(out long socket, NetworkEndPoint address)
        {
            socket = -1;
            int errorcode = 0;
            int ret = NativeBindings.network_create_and_bind(ref socket, ref address, ref errorcode);
            if (ret != 0)
                return errorcode;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AllSockets.OpenSockets.Add(socket);
#endif
            NativeBindings.network_set_nonblocking(socket);
            NativeBindings.network_set_send_buffer_size(socket, ushort.MaxValue);
            NativeBindings.network_set_receive_buffer_size(socket, ushort.MaxValue);
#if (UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN)
            // Avoid WSAECONNRESET errors when sending to an endpoint which isn't open yet (unclean connect/disconnects)
            NativeBindings.network_set_connection_reset(socket, 0);
#endif
            return 0;
        }
    }

    public struct IPCSocket : INetworkInterface
    {
        [NativeDisableContainerSafetyRestriction] private NativeQueue<IPCManager.IPCQueuedMessage> m_IPCQueue;
        private NativeQueue<IPCManager.IPCQueuedMessage>.Concurrent m_ConcurrentIPCQueue;
        [ReadOnly] private NativeArray<NetworkEndPoint> m_LocalEndPoint;

        public NetworkEndPoint LocalEndPoint => m_LocalEndPoint[0];
        public NetworkEndPoint RemoteEndPoint { get; }

        public NetworkFamily Family { get; }

        public void Initialize()
        {
            m_LocalEndPoint = new NativeArray<NetworkEndPoint>(1, Allocator.Persistent);
            m_LocalEndPoint[0] = IPCManager.Instance.CreateEndPoint();
            m_IPCQueue = new NativeQueue<IPCManager.IPCQueuedMessage>(Allocator.Persistent);
            m_ConcurrentIPCQueue = m_IPCQueue.ToConcurrent();
        }

        public void Dispose()
        {
            IPCManager.Instance.ReleaseEndPoint(m_LocalEndPoint[0]);
            m_LocalEndPoint.Dispose();
            m_IPCQueue.Dispose();
        }

        [BurstCompile]
        struct SendUpdate : IJob
        {
            public IPCManager ipcManager;
            public NativeQueue<IPCManager.IPCQueuedMessage> ipcQueue;

            public void Execute()
            {
                ipcManager.Update(ipcQueue);
            }
        }

        [BurstCompile]
        struct ReceiveJob<T> : IJob where T : struct, INetworkPacketReceiver
        {
            public T receiver;
            public IPCManager ipcManager;
            public NetworkEndPoint localEndPoint;

            public unsafe void Execute()
            {
                var address = new NetworkEndPoint {length = sizeof(NetworkEndPoint)};
                var header = new UdpCHeader();
                var stream = receiver.GetDataStream();
                receiver.ReceiveCount = 0;
                receiver.ReceiveErrorCode = 0;

                while (true)
                {
                    if (receiver.DynamicDataStreamSize())
                    {
                        while (stream.Length+NetworkParameterConstants.MTU >= stream.Capacity)
                            stream.Capacity *= 2;
                    }
                    else if (stream.Length >= stream.Capacity)
                        return;
                    var sliceOffset = stream.Length;
                    var result = NativeReceive(ref header, stream.GetUnsafePtr() + sliceOffset,
                        Math.Min(NetworkParameterConstants.MTU, stream.Capacity - stream.Length), ref address);
                    if (result <= 0)
                    {
                        // FIXME: handle error
                        if (result < 0)
                            receiver.ReceiveErrorCode = 10040;
                        return;
                    }

                    receiver.ReceiveCount += receiver.AppendPacket(address, header, result);
                }
            }

            unsafe int NativeReceive(ref UdpCHeader header, void* data, int length, ref NetworkEndPoint address)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (length <= 0)
                    throw new ArgumentException("Can't receive into 0 bytes or less of buffer memory");
#endif
                var iov = stackalloc network_iovec[2];

                fixed (byte* ptr = header.Data)
                {
                    iov[0].buf = ptr;
                    iov[0].len = UdpCHeader.Length;

                    iov[1].buf = data;
                    iov[1].len = length;
                }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (localEndPoint.Family != NetworkFamily.IPC || localEndPoint.nbo_port == 0)
                    throw new InvalidOperationException();
#endif
                return ipcManager.ReceiveMessageEx(localEndPoint, iov, 2, ref address);
            }
        }

        public JobHandle ScheduleReceive<T>(T receiver, JobHandle dep) where T : struct, INetworkPacketReceiver
        {
            var sendJob = new SendUpdate {ipcManager = IPCManager.Instance, ipcQueue = m_IPCQueue};
            var job = new ReceiveJob<T>
                {receiver = receiver, ipcManager = IPCManager.Instance, localEndPoint = m_LocalEndPoint[0]};
            dep = job.Schedule(JobHandle.CombineDependencies(dep, IPCManager.ManagerAccessHandle));
            dep = sendJob.Schedule(dep);
            IPCManager.ManagerAccessHandle = dep;
            return dep;
        }

        public int Bind(NetworkEndPoint endpoint)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (endpoint.Family != NetworkFamily.IPC || endpoint.nbo_port == 0)
                throw new InvalidOperationException();
#endif
            IPCManager.Instance.ReleaseEndPoint(m_LocalEndPoint[0]);
            m_LocalEndPoint[0] = endpoint;
            return 0;
        }

        public unsafe int SendMessage(network_iovec* iov, int iov_len, ref NetworkEndPoint address)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (m_LocalEndPoint[0].Family != NetworkFamily.IPC || m_LocalEndPoint[0].nbo_port == 0)
                throw new InvalidOperationException();
#endif
            return IPCManager.SendMessageEx(m_ConcurrentIPCQueue, m_LocalEndPoint[0], iov, iov_len, ref address);
        }
    }
}

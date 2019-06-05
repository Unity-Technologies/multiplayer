using System;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Networking.Transport;
using Unity.Networking.Transport.LowLevel.Unsafe;
using Unity.Networking.Transport.Utilities;
using UnityEngine;

namespace SQP
{
    [Flags]
    public enum SQPChunkType
    {
        ServerInfo = 1,
        ServerRules = 2,
        PlayerInfo = 4,
        TeamInfo = 8
    }

    public enum SQPMessageType
    {
        ChallangeRequest = 0,
        ChallangeResponse = 0,
        QueryRequest = 1,
        QueryResponse = 1
    }

    public interface ISQPMessage
    {
        void ToStream(ref DataStreamWriter writer);
        void FromStream(DataStreamReader reader, ref DataStreamReader.Context ctx);
    }

    public struct SQPHeader : ISQPMessage
    {
        public byte Type { get; internal set; }
        public uint ChallangeId;

        public void ToStream(ref DataStreamWriter writer)
        {
            writer.Write(Type);
            writer.WriteNetworkByteOrder(ChallangeId);
        }

        public void FromStream(DataStreamReader reader, ref DataStreamReader.Context ctx)
        {
            Type = reader.ReadByte(ref ctx);
            ChallangeId = reader.ReadUIntNetworkByteOrder(ref ctx);
        }
    }

    public struct ChallangeRequest : ISQPMessage
    {
        public SQPHeader Header;

        public void ToStream(ref DataStreamWriter writer)
        {
            Header.Type = (byte)SQPMessageType.ChallangeRequest;
            Header.ToStream(ref writer);
        }

        public void FromStream(DataStreamReader reader, ref DataStreamReader.Context ctx)
        {
            Header.FromStream(reader, ref ctx);
        }
    }

    public struct ChallangeResponse
    {
        public SQPHeader Header;

        public void ToStream(ref DataStreamWriter writer)
        {
            Header.Type = (byte)SQPMessageType.ChallangeResponse;
            Header.ToStream(ref writer);
        }

        public void FromStream(DataStreamReader reader, ref DataStreamReader.Context ctx)
        {
            Header.FromStream(reader, ref ctx);
        }
    }

    public struct QueryRequest
    {
        public SQPHeader Header;
        public ushort Version;

        public byte RequestedChunks;

        public void ToStream(ref DataStreamWriter writer)
        {
            Header.Type = (byte)SQPMessageType.QueryRequest;

            Header.ToStream(ref writer);
            writer.WriteNetworkByteOrder(Version);
            writer.Write(RequestedChunks);
        }

        public void FromStream(DataStreamReader reader, ref DataStreamReader.Context ctx)
        {
            Header.FromStream(reader, ref ctx);
            Version = reader.ReadUShortNetworkByteOrder(ref ctx);
            RequestedChunks = reader.ReadByte(ref ctx);
        }
    }

    public struct QueryResponseHeader
    {
        public SQPHeader Header;
        public ushort Version;
        public byte CurrentPacket;
        public byte LastPacket;
        public ushort Length;

        public DataStreamWriter.DeferredUShortNetworkByteOrder ToStream(ref DataStreamWriter writer)
        {
            Header.Type = (byte)SQPMessageType.QueryResponse;
            Header.ToStream(ref writer);
            writer.WriteNetworkByteOrder(Version);
            writer.Write(CurrentPacket);
            writer.Write(LastPacket);
            return writer.WriteNetworkByteOrder(Length);
        }

        public void FromStream(DataStreamReader reader, ref DataStreamReader.Context ctx)
        {
            Header.FromStream(reader, ref ctx);
            Version = reader.ReadUShortNetworkByteOrder(ref ctx);
            CurrentPacket = reader.ReadByte(ref ctx);
            LastPacket = reader.ReadByte(ref ctx);
            Length = reader.ReadUShortNetworkByteOrder(ref ctx);
        }
    }

    public class ServerInfo
    {
        public QueryResponseHeader QueryHeader;
        public uint ChunkLen;
        public Data ServerInfoData;

        public ServerInfo()
        {
            ServerInfoData = new Data();
        }

        public class Data
        {
            public ushort CurrentPlayers;
            public ushort MaxPlayers;

            public string ServerName = "";
            public string GameType = "";
            public string BuildId = "";
            public string Map = "";
            public ushort Port;


            unsafe void WriteString(DataStreamWriter writer, string value)
            {
                var encoder = encoding.GetEncoder();

                var buffer = new byte[byte.MaxValue];
                var chars = value.ToCharArray();
                int charsUsed, bytesUsed;
                bool completed;

                encoder.Convert(chars, 0, chars.Length, buffer, 0, byte.MaxValue, true, out charsUsed, out bytesUsed, out completed);
                Debug.Assert(bytesUsed <= byte.MaxValue);

                writer.Write((byte)bytesUsed);
                fixed (byte* bufferPtr = &buffer[0])
                {
                    writer.WriteBytes(bufferPtr, bytesUsed);
                }
            }
            unsafe string ReadString(DataStreamReader reader, ref DataStreamReader.Context ctx)
            {
                var length = reader.ReadByte(ref ctx);
                var buffer = new byte[byte.MaxValue];
                fixed (byte* bufferPtr = &buffer[0])
                {
                    reader.ReadBytes(ref ctx, bufferPtr, length);
                }

                return encoding.GetString(buffer, 0, length);
            }
            public void ToStream(ref DataStreamWriter writer)
            {
                writer.WriteNetworkByteOrder(CurrentPlayers);
                writer.WriteNetworkByteOrder(MaxPlayers);

                WriteString(writer, ServerName);
                WriteString(writer, GameType);
                WriteString(writer, BuildId);
                WriteString(writer, Map);

                writer.WriteNetworkByteOrder(Port);
            }

            public void FromStream(DataStreamReader reader, ref DataStreamReader.Context ctx)
            {
                CurrentPlayers = reader.ReadUShortNetworkByteOrder(ref ctx);
                MaxPlayers = reader.ReadUShortNetworkByteOrder(ref ctx);

                ServerName = ReadString(reader, ref ctx);
                GameType = ReadString(reader, ref ctx);
                BuildId = ReadString(reader, ref ctx);
                Map = ReadString(reader, ref ctx);

                Port = reader.ReadUShortNetworkByteOrder(ref ctx);
            }
        }

        public void ToStream(ref DataStreamWriter writer)
        {
            var lengthValue = QueryHeader.ToStream(ref writer);

            var start = (ushort)writer.Length;

            var chunkValue = writer.WriteNetworkByteOrder((uint)0); // ChunkLen

            var chunkStart = (uint)writer.Length;
            ServerInfoData.ToStream(ref writer);
            ChunkLen = (uint)writer.Length - chunkStart;
            QueryHeader.Length = (ushort)(writer.Length - start);

            lengthValue.Update((ushort)QueryHeader.Length);
            chunkValue.Update((uint)ChunkLen);
        }

        public void FromStream(DataStreamReader reader, ref DataStreamReader.Context ctx)
        {
            QueryHeader.FromStream(reader, ref ctx);
            ChunkLen = reader.ReadUIntNetworkByteOrder(ref ctx);

            ServerInfoData.FromStream(reader, ref ctx);
        }
        static private Encoding encoding = new UTF8Encoding();
}

    public static class UdpExtensions
    {
        public static SocketError SetupAndBind(this Socket socket, int port = 0)
        {
            SocketError error = SocketError.Success;
            socket.Blocking = false;

            var ep = new IPEndPoint(IPAddress.Any, port);
            try
            {
                socket.Bind(ep);
            }
            catch (SocketException e)
            {
                error =  e.SocketErrorCode;
                throw e;
            }
            return error;
        }
    }

    public class SQPClient
    {
        Socket m_Socket;
        SocketError m_SocketError;
        IPEndPoint m_Server;
        Timer m_Timer;

        private const int BufferSize = 1472;
        private byte[] m_Buffer = new byte[BufferSize];

        System.Net.EndPoint endpoint = new System.Net.IPEndPoint(0, 0);

        uint ChallangeId;
        long StartTime;

        public enum SQPClientState
        {
            Idle,
            WaitingForChallange,
            WaitingForResponse,
            Success,
            Failure
        }
        SQPClientState m_State;
        public SQPClientState ClientState
        {
            get { return m_State; }
        }

        public SQPClient(IPEndPoint server)
        {
            m_Socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            m_Socket.SetupAndBind(0);

            m_Server = server;

            m_State = new SQPClientState();
            m_Timer = new Timer();
        }

        public unsafe void StartInfoQuery()
        {
            Debug.Assert(m_State == SQPClientState.Idle);
            StartTime = m_Timer.ElapsedMilliseconds;

            var writer = new DataStreamWriter(BufferSize, Allocator.Temp);
            var req = new ChallangeRequest();
            req.ToStream(ref writer);

            writer.CopyTo(0, writer.Length, ref m_Buffer);
            m_Socket.SendTo(m_Buffer, writer.Length, SocketFlags.None, m_Server);
            m_State = SQPClientState.WaitingForChallange;
            writer.Dispose();
        }
        unsafe void SendServerInfoQuery()
        {
            StartTime = m_Timer.ElapsedMilliseconds;
            var req = new QueryRequest();
            req.Header.ChallangeId = ChallangeId;
            req.RequestedChunks = (byte)SQPChunkType.ServerInfo;

            var writer = new DataStreamWriter(BufferSize, Allocator.Temp);
            req.ToStream(ref writer);

            m_State = SQPClientState.WaitingForResponse;
            writer.CopyTo(0, writer.Length, ref m_Buffer);
            m_Socket.SendTo(m_Buffer, writer.Length, SocketFlags.None, m_Server);
            writer.Dispose();
        }

        public unsafe void Update()
        {
            if (m_Socket.Poll(0, SelectMode.SelectRead))
            {
                int read = m_Socket.ReceiveFrom(m_Buffer, BufferSize, SocketFlags.None, ref endpoint);
                if (read > 0)
                {
                    var buffer = new DataStreamWriter(BufferSize, Allocator.Temp);
                    buffer.Write(m_Buffer, read);
                    var reader = new DataStreamReader(buffer, 0, read);
                    var readerCtx = default(DataStreamReader.Context);
                    var header = new SQPHeader();
                    header.FromStream(reader, ref readerCtx);

                    switch (m_State)
                    {
                        case SQPClientState.Idle:
                            break;

                        case SQPClientState.WaitingForChallange:
                            if ((SQPMessageType)header.Type == SQPMessageType.ChallangeResponse)
                            {
                                if (endpoint.Equals(m_Server))
                                {
                                    ChallangeId = header.ChallangeId;
                                    SendServerInfoQuery();
                                }
                            }
                            break;

                        case SQPClientState.WaitingForResponse:
                            if ((SQPMessageType)header.Type == SQPMessageType.QueryResponse)
                            {
                                readerCtx = default(DataStreamReader.Context);
                                var rsp = new SQP.ServerInfo();
                                rsp.FromStream(reader, ref readerCtx);
                                Debug.Log(string.Format("ServerName: {0}, BuildId: {1}, Current Players: {2}, Max Players: {3}, GameType: {4}, Map: {5}, Port: {6}",
                                    rsp.ServerInfoData.ServerName,
                                    rsp.ServerInfoData.BuildId,
                                    (ushort)rsp.ServerInfoData.CurrentPlayers,
                                    (ushort)rsp.ServerInfoData.MaxPlayers,
                                    rsp.ServerInfoData.GameType,
                                    rsp.ServerInfoData.Map,
                                    (ushort)rsp.ServerInfoData.Port));
                                m_State = SQPClientState.Idle;
                                StartTime = m_Timer.ElapsedMilliseconds;
                            }
                            break;

                        default:
                            break;
                    }
                    buffer.Dispose();
                }
            }
            var now = m_Timer.ElapsedMilliseconds;
            if (now - StartTime > 1000000)
            {
                Debug.Log("Failed");
                m_State = SQPClientState.Failure;
            }
        }
    }

    public class SQPServer
    {
        Socket m_Socket;
        SocketError m_SocketError;
        System.Random m_Random;

        SQP.ServerInfo m_ServerInfo = new ServerInfo();

        public SQP.ServerInfo.Data ServerInfoData { get; set; }

        private const int BufferSize = 1472;
        private byte[] m_Buffer = new byte[BufferSize];

        System.Net.EndPoint endpoint = new System.Net.IPEndPoint(0, 0);
        Dictionary<EndPoint, uint> m_OutstandingTokens = new Dictionary<EndPoint, uint>();

        public SQPServer(int port)
        {
            m_Socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            m_Socket.SetupAndBind(port);
            m_Random = new System.Random();
            ServerInfoData = new ServerInfo.Data();
            m_ServerInfo.ServerInfoData = ServerInfoData;
        }

        public unsafe void Update()
        {
            if (m_Socket.Poll(0, SelectMode.SelectRead))
            {
                int read = m_Socket.ReceiveFrom(m_Buffer, BufferSize, SocketFlags.None, ref endpoint);
                if (read > 0)
                {
                    var buffer = new DataStreamWriter(BufferSize, Allocator.Temp);
                    buffer.Write(m_Buffer, read);
                    var reader = new DataStreamReader(buffer, 0, read);
                    var readerCtx = default(DataStreamReader.Context);
                    var header = new SQPHeader();
                    header.FromStream(reader, ref readerCtx);

                    SQPMessageType type = (SQPMessageType)header.Type;

                    switch (type)
                    {
                        case SQPMessageType.ChallangeRequest:
                            {
                                if (!m_OutstandingTokens.ContainsKey(endpoint))
                                {
                                    uint token = GetNextToken();
                                    Debug.Log("token generated: " + token);

                                    var writer = new DataStreamWriter(BufferSize, Allocator.Temp);
                                    var rsp = new ChallangeResponse();
                                    rsp.Header.ChallangeId = token;
                                    rsp.ToStream(ref writer);

                                    writer.CopyTo(0, writer.Length, ref m_Buffer);
                                    m_Socket.SendTo(m_Buffer, writer.Length, SocketFlags.None, endpoint);

                                    m_OutstandingTokens.Add(endpoint, token);
                                    writer.Dispose();
                                }

                            }
                            break;
                        case SQPMessageType.QueryRequest:
                            {
                                uint token;
                                if (!m_OutstandingTokens.TryGetValue(endpoint, out token))
                                {
                                    Debug.Log("Failed to find token!");
                                    return;
                                }
                                m_OutstandingTokens.Remove(endpoint);

                                readerCtx = default(DataStreamReader.Context);
                                var req = new QueryRequest();
                                req.FromStream(reader, ref readerCtx);

                                if ((SQPChunkType)req.RequestedChunks == SQPChunkType.ServerInfo)
                                {
                                    var rsp = m_ServerInfo;
                                    var writer = new DataStreamWriter(BufferSize, Allocator.Temp);
                                    rsp.QueryHeader.Header.ChallangeId = token;

                                    rsp.ToStream(ref writer);
                                    writer.CopyTo(0, writer.Length, ref m_Buffer);
                                    m_Socket.SendTo(m_Buffer, writer.Length, SocketFlags.None, endpoint);
                                    writer.Dispose();
                                }
                            }
                            break;
                        default:
                            break;
                    }

                    buffer.Dispose();
                }
            }
        }

        uint GetNextToken()
        {
            uint thirtyBits = (uint)m_Random.Next(1 << 30);
            uint twoBits = (uint)m_Random.Next(1 << 2);
            return (thirtyBits << 2) | twoBits;
        }
    }
}

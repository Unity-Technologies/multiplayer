#if ENABLE_UNITY_COLLECTIONS_CHECKS
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

class DebugWebSocket : IDisposable
{
    private Socket m_serverSocket;
    private Socket m_connectionSocket;
    private bool m_connectionComplete;
    private byte[] m_frameHeader;

    public DebugWebSocket(int portOffset)
    {
        try
        {
            m_serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            m_serverSocket.Blocking = false;
            m_serverSocket.Bind(new IPEndPoint(IPAddress.Any, 8787 + portOffset));
            m_serverSocket.Listen(10);
            m_frameHeader = new byte[16];

        }
        catch (SocketException e)
        {
            UnityEngine.Debug.LogError("Failed to create websocket: " + e);
            m_serverSocket = null;
        }
    }

    public bool HasConnection => m_connectionComplete && m_connectionSocket != null;

    public void Dispose()
    {
        if (m_connectionSocket != null)
            m_connectionSocket.Dispose();
        if (m_serverSocket != null)
            m_serverSocket.Dispose();
    }

    public bool AcceptNewConnection()
    {
        if (m_serverSocket == null)
            return false;
        try
        {
            if (!m_connectionComplete && m_connectionSocket != null)
            {
                // Listen for the http header, parse it and reply with another http header
                var headerBuffer = new byte[4096];
                var headerSize = m_connectionSocket.Receive(headerBuffer);
                if (headerSize > 0)
                {
                    var header = Encoding.UTF8.GetString(headerBuffer, 0, headerSize);
                    var headerFields = header.Split(new []{"\r\n"}, StringSplitOptions.None);
                    if (headerFields.Length < 3 || headerFields[headerFields.Length - 2] != "" || headerFields[headerFields.Length - 1] != "")
                    {
                        UnityEngine.Debug.LogError("Invalid header for websocket: " + header);
                        FailConnection();
                        return false;
                    }
                    var get = headerFields[0].Split(null);
                    if (get.Length != 3 || get[0] != "GET" || get[2] != "HTTP/1.1")
                    {
                        UnityEngine.Debug.LogError("Invalid GET header for websocket: " + headerFields[0]);
                        FailConnection();
                        return false;
                    }

                    var headerLookup = new Dictionary<string, string>();
                    for (var field = 1; field < headerFields.Length-2; ++field)
                    {
                        var keyval = headerFields[field].Split(new[] {':'}, StringSplitOptions.None);
                        if (keyval.Length < 2)
                        {
                            UnityEngine.Debug.LogError("Invalid header line for websocket: " + headerFields[field]);
                            FailConnection();
                            return false;
                        }

                        for (int i = 2; i < keyval.Length; ++i)
                            keyval[1] += ":" + keyval[i];
                        headerLookup.Add(keyval[0].Trim().ToLower(), keyval[1].Trim());
                    }
                    // Parse the header and reply
                    string wsKey, wsConnection, wsUpgrade, wsVer;
                    if (!headerLookup.TryGetValue("sec-websocket-key", out wsKey) ||
                        !headerLookup.TryGetValue("connection", out wsConnection) ||
                        !headerLookup.TryGetValue("upgrade", out wsUpgrade) ||
                        !headerLookup.TryGetValue("sec-websocket-version", out wsVer) ||
                        !headerLookup.ContainsKey("host"))
                    {
                        UnityEngine.Debug.LogError("Header does not contain all required fields: " + header);
                        FailConnection();
                        return false;
                    }

                    if (wsVer != "13")
                    {
                        UnityEngine.Debug.LogError("Invalid websocket version: " + wsVer);
                        FailConnection();
                        return false;
                    }
                    if (wsUpgrade.ToLower() != "websocket")
                    {
                        UnityEngine.Debug.LogError("Invalid websocket upgrade request: " + wsUpgrade);
                        FailConnection();
                        return false;
                    }

                    bool upgrade = false;
                    var upgradeTokens = wsConnection.Split(new []{','}, StringSplitOptions.None);
                    foreach (var token in upgradeTokens)
                        upgrade |= token.Trim().ToLower() == "upgrade";
                    if (!upgrade)
                    {
                        UnityEngine.Debug.LogError("Invalid websocket connection header: " + wsConnection);
                        FailConnection();
                        return false;
                    }

                    wsKey += "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
                    SHA1 sha = new SHA1CryptoServiceProvider();
                    var resultKey = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(wsKey)));
                    header =
                        "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " +
                        resultKey + "\r\n\r\n";
                    m_connectionSocket.Send(Encoding.UTF8.GetBytes(header));
                    m_connectionComplete = true;
                    return true;
                }
            }
        }
        catch (SocketException e)
        {
            if (e.SocketErrorCode != SocketError.WouldBlock)
            {
                m_connectionSocket.Dispose();
                m_connectionSocket = null;
            }
        }
        try
        {
            var sock = m_serverSocket.Accept();
            if (m_connectionSocket != null)
            {
                // FIXME: send close frame etc
                m_connectionSocket.Dispose();
            }

            m_connectionSocket = sock;
            m_connectionSocket.Blocking = false;
            m_connectionComplete = false;
        }
        catch (SocketException)
        {
        }

        return false;
    }

    void FailConnection()
    {
        m_connectionSocket.Send(Encoding.UTF8.GetBytes("HTTP/1.1 400 Bad Request\r\nContent-Length: 0\r\nConnection: Closed\r\n\r\n"));
        m_connectionSocket.Dispose();
        m_connectionSocket = null;
    }

    public void SendText(string msg)
    {
        // Fin bit + text message
        m_frameHeader[0] = 0x81;
        int headerLen = 2;
        int len = msg.Length;
        if (len < 126)
            m_frameHeader[1] = (byte)len;
        else if (len <= ushort.MaxValue)
        {
            m_frameHeader[1] = 126;
            m_frameHeader[2] = (byte)(len >> 8);
            m_frameHeader[3] = (byte)(len);
            headerLen = 4;
        }
        else
        {
            m_frameHeader[1] = 127;
            m_frameHeader[2] = 0;
            m_frameHeader[3] = 0;
            m_frameHeader[4] = 0;
            m_frameHeader[5] = 0;
            m_frameHeader[6] = (byte)(len >> 24);
            m_frameHeader[7] = (byte)(len >> 16);
            m_frameHeader[8] = (byte)(len >> 8);
            m_frameHeader[9] = (byte)(len);
            headerLen = 10;
        }

        try
        {
            m_connectionSocket.Send(m_frameHeader, headerLen, SocketFlags.None);
            m_connectionSocket.Send(Encoding.UTF8.GetBytes(msg));
        }
        catch (SocketException e)
        {
            if (e.SocketErrorCode != SocketError.WouldBlock)
            {
                m_connectionSocket.Dispose();
                m_connectionSocket = null;
            }
        }
    }
    public void SendBinary(byte[] msg, int len)
    {
        // Fin bit + binary message
        m_frameHeader[0] = 0x82;
        int headerLen = 2;
        if (len < 126)
            m_frameHeader[1] = (byte)len;
        else if (len <= ushort.MaxValue)
        {
            m_frameHeader[1] = 126;
            m_frameHeader[2] = (byte)(len >> 8);
            m_frameHeader[3] = (byte)(len);
            headerLen = 4;
        }
        else
        {
            m_frameHeader[1] = 127;
            m_frameHeader[2] = 0;
            m_frameHeader[3] = 0;
            m_frameHeader[4] = 0;
            m_frameHeader[5] = 0;
            m_frameHeader[6] = (byte)(len >> 24);
            m_frameHeader[7] = (byte)(len >> 16);
            m_frameHeader[8] = (byte)(len >> 8);
            m_frameHeader[9] = (byte)(len);
            headerLen = 10;
        }

        try
        {
            m_connectionSocket.Send(m_frameHeader, headerLen, SocketFlags.None);
            m_connectionSocket.Send(msg, len, SocketFlags.None);
        }
        catch (SocketException e)
        {
            if (e.SocketErrorCode != SocketError.WouldBlock)
            {
                m_connectionSocket.Dispose();
                m_connectionSocket = null;
            }
        }
    }
}

[UpdateInGroup(typeof(ClientSimulationSystemGroup))]
[UpdateBefore(typeof(GhostReceiveSystemGroup))]
class GhostStatsSystem : ComponentSystem
{
    private GhostReceiveSystem m_ReceiveSys;
    private uint lastFrame;
    private ComponentGroup connectionGroup;
    private DebugWebSocket socket;
    private string ghostList;
    private byte[] ghostStatData;
    protected override void OnCreateManager()
    {
        m_ReceiveSys = World.GetOrCreateManager<GhostReceiveSystem>();
        lastFrame = 0;
        connectionGroup = GetComponentGroup(ComponentType.ReadWrite<NetworkSnapshotAck>());
        socket = new DebugWebSocket(World.GetExistingManager<ClientSimulationSystemGroup>().ClientWorldIndex);

        ghostList = "Destroy";
        var list = GhostDeserializerCollection.CreateSerializerNameList();
        for (int i = 0; i < list.Length; ++i)
        {
            ghostList += ";" + list[i];
        }

        // 3 ints per ghost type, plus one for destroy
        ghostStatData = new byte[(list.Length + 1) * 3 * 4];
    }

    protected override void OnDestroyManager()
    {
        socket.Dispose();
    }

    protected override unsafe void OnUpdate()
    {
        if (socket.AcceptNewConnection())
        {
            socket.SendText(ghostList);
        }

        if (!socket.HasConnection)
            return;
        var connection = connectionGroup.ToComponentDataArray<NetworkSnapshotAck>(Allocator.TempJob);
        if (connection.Length == 0)
        {
            connection.Dispose();
            return;
        }

        var stats = m_ReceiveSys.NetStats;
        var frame = connection[0].LastReceivedSnapshotByLocal;
        connection.Dispose();
        if (frame <= lastFrame)
            return;
        if (lastFrame == 0)
            lastFrame = frame-1;
        for (lastFrame = lastFrame + 1; lastFrame < frame; ++lastFrame)
        {
            for (int i = 0; i < ghostStatData.Length; ++i)
                ghostStatData[i] = 0;
            socket.SendBinary(ghostStatData, ghostStatData.Length);
        }

        var statBytes = (byte*)stats.GetUnsafeReadOnlyPtr();
        for (int i = 0; i < ghostStatData.Length; ++i)
            ghostStatData[i] = statBytes[i];
        socket.SendBinary(ghostStatData, ghostStatData.Length);
    }
}
#endif


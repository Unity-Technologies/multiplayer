using System;
using System.Net;
using System.Security;
using UnityEngine;
using Unity.Networking.Transport;
using UnityEngine.Ucg.Matchmaking;
using UdpCNetworkDriver = Unity.Networking.Transport.BasicNetworkDriver<Unity.Networking.Transport.IPv4UDPSocket>;

/* The PingClientUIBehaviour is responsible for displaying statistics of a
 running ping client as well as for starting and stopping the ping of a
 selected ip. */
public class PingClientUIBehaviour : MonoBehaviour
{
    // The EndPoint the ping client should ping, will be a non-created end point when ping should not run.
    public static NetworkEndPoint ServerEndPoint { get; private set; }

    // Update the ping statistics displayed in the ui. Should be called from the ping client every time a new ping is complete
    public static void UpdateStats(int count, int time)
    {
        m_pingCounter = count;
        m_pingTime = time;
    }

    private static int m_pingTime;
    private static int m_pingCounter;
    private string m_CustomIp = "";

    // Matchmaking
    public string MatchmakingServer = "";
    private bool m_useMatchmaking = false;
    private Matchmaker m_matchmaker = null;

    void Start()
    {
        m_pingTime = 0;
        m_pingCounter = 0;
        ServerEndPoint = default(NetworkEndPoint);
    }

    void FixedUpdate()
    {
        if (!m_useMatchmaking)
            return;

        if (m_matchmaker == null)
        {
            m_matchmaker = new Matchmaker(MatchmakingServer);
            MatchmakingPlayerProperties playerProps = new MatchmakingPlayerProperties() {hats = 5};
            MatchmakingGroupProperties groupProps = new MatchmakingGroupProperties() {mode = 0};
            MatchmakingRequest request = Matchmaker.CreateMatchmakingRequest(Guid.NewGuid().ToString(), playerProps, groupProps);
            m_matchmaker.RequestMatch(request, OnMatchmakingSuccess, OnMatchmakingError);
        }
        else
        {
            m_matchmaker.Update();
        }
    }

    void OnGUI()
    {
        if (m_useMatchmaking)
        {
            UpdateMatchmakingUI();
        }
        else
        {
            UpdatePingClientUI();
        }
    }

    private void UpdateMatchmakingUI()
    {
        if (m_useMatchmaking && MatchmakingServer.Length > 0)
        {
            GUILayout.Label("Matchmaking");
            GUILayout.Label("Finding a server...");
            if (GUILayout.Button("Cancel"))
            {
                m_matchmaker = null;
                m_useMatchmaking = false;
            }
        }
        else
        {
            m_useMatchmaking = false;
        }
    }

    private void UpdatePingClientUI()
    {
        GUILayout.Label("PING " + m_pingCounter + ": " + m_pingTime + "ms");
        if (!ServerEndPoint.IsValid)
        {
            // Ping is not currently running, display ui for starting a ping
            if (GUILayout.Button("Start ping"))
            {
                ushort port = 9000;
                if (string.IsNullOrEmpty(m_CustomIp))
                    ServerEndPoint = new IPEndPoint(IPAddress.Loopback, port);
                else
                {
                    string[] endpoint = m_CustomIp.Split(':');
                    ushort newPort = 0;
                    if (endpoint.Length > 1 && ushort.TryParse(endpoint[1], out newPort))
                        port = newPort;

                    Debug.Log($"Connecting to PingServer at {endpoint[0]}:{port}.");
                    ServerEndPoint = new IPEndPoint(IPAddress.Parse(endpoint[0]), port);
                }
            }

            m_CustomIp = GUILayout.TextField(m_CustomIp);
            if (!string.IsNullOrEmpty(MatchmakingServer) && GUILayout.Button("Use Matchmaking"))
            {
                m_useMatchmaking = true;
            }
        }
        else
        {
            // Ping is running, display ui for stopping it
            if (GUILayout.Button("Stop ping"))
            {
                ServerEndPoint = default(NetworkEndPoint);
            }
        }
    }

    void OnMatchmakingSuccess(string connectionInfo)
    {
        Debug.Log($"Matchmaking has found a game! The server is at: {connectionInfo}.");
        m_CustomIp = connectionInfo;
        m_useMatchmaking = false;
        m_matchmaker = null;
    }

    void OnMatchmakingError(string errorInfo)
    {
        Debug.Log($"Matchmaking failed! Error is: {errorInfo}.");
        m_useMatchmaking = false;
        m_matchmaker = null;
    }
}


using System;
using UnityEngine;
using Unity.Networking.Transport;
using UnityEngine.Ucg.Matchmaking;

/* The PingClientUIBehaviour is responsible for displaying statistics of a
 running ping client as well as for starting and stopping the ping of a
 selected ip. */
public class PingClientUIBehaviour : MonoBehaviour
{
    // The EndPoint the ping client should ping, will be a non-created end point when ping should not run.
    public static NetworkEndPoint ServerEndPoint { get; private set; }

    // Ping statistics
    static int s_PingTime;
    static int s_PingCounter;

    // Matchmaking
    public string MatchmakingServer = "";
    bool m_UseMatchmaking = false;
    Matchmaker m_Matchmaker = null;

    string m_CustomIp = "";

    void Start()
    {
        s_PingTime = 0;
        s_PingCounter = 0;
        ServerEndPoint = default(NetworkEndPoint);
    }

    void FixedUpdate()
    {
        if (!m_UseMatchmaking)
            return;

        if (m_Matchmaker == null)
        {
            MatchmakingServer = MatchmakingServer.Trim();

            m_Matchmaker = new Matchmaker(MatchmakingServer, OnMatchmakingSuccess, OnMatchmakingError);
            
            var matchId = Guid.NewGuid().ToString();
            var playerProps = new MatchmakingUtilities.PlayerProperties() { hats = 5 };
            var groupProps = new MatchmakingUtilities.GroupProperties() { mode = 0 };
            var request = MatchmakingUtilities.CreateMatchmakingRequest(matchId, playerProps, groupProps);

            m_Matchmaker.RequestMatch(request);
        }
        else
        {
            m_Matchmaker.Update();
        }
    }

    void OnGUI() 
    {
        if (m_UseMatchmaking)
        {
            UpdateMatchmakingUI();
        }
        else
        {
            UpdatePingClientUI();
        }
    }

    // Update the ping statistics displayed in the ui. Should be called from the ping client every time a new ping is complete
    public static void UpdateStats(int count, int time)
    {
        s_PingCounter = count;
        s_PingTime = time;
    }

    void UpdateMatchmakingUI()
    {
        if (m_UseMatchmaking && MatchmakingServer.Length > 0)
        {
            GUILayout.Label("Matchmaking");
            GUILayout.Label("Finding a server...");
            if (GUILayout.Button("Cancel"))
            {
                m_Matchmaker = null;
                m_UseMatchmaking = false;
            }
        }
        else
        {
            m_UseMatchmaking = false;
        }
    }

    void UpdatePingClientUI()
    {
        GUILayout.Label("PING " + s_PingCounter + ": " + s_PingTime + "ms");
        if (!ServerEndPoint.IsValid)
        {
            // Ping is not currently running, display ui for starting a ping
            if (GUILayout.Button("Start ping"))
            {
                ushort port = 9000;
                if (string.IsNullOrEmpty(m_CustomIp))
                {
                    var endpoint = NetworkEndPoint.LoopbackIpv4;
                    endpoint.Port = port;
                    ServerEndPoint = endpoint;
                }
                else
                {
                    string[] endpoint = m_CustomIp.Split(':');
                    ushort newPort = 0;
                    if (endpoint.Length > 1 && ushort.TryParse(endpoint[1], out newPort))
                        port = newPort;

                    Debug.Log($"Connecting to PingServer at {endpoint[0]}:{port}.");
                    ServerEndPoint = NetworkEndPoint.Parse(endpoint[0], port);
                }
            }

            m_CustomIp = GUILayout.TextField(m_CustomIp);
            if (!string.IsNullOrEmpty(MatchmakingServer) && GUILayout.Button("Use Matchmaking"))
            {
                m_UseMatchmaking = true;
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

    void OnMatchmakingSuccess(Assignment assignment)
    {
        if (string.IsNullOrEmpty(assignment.ConnectionString))
        {
            Debug.Log("Matchmaking finished, but did not return a game server.  Ensure your server has been allocated and is running then try again.");
        }
        else
        {
            Debug.Log($"Matchmaking has found a game! The server is at {assignment.ConnectionString} with players: " + string.Join(", ", assignment.Roster));
            m_CustomIp = assignment.ConnectionString;
        }
        m_UseMatchmaking = false;
        m_Matchmaker = null;
    }

    void OnMatchmakingError(string errorInfo)
    {
        Debug.Log($"Matchmaking failed! Error is: {errorInfo}.");
        m_UseMatchmaking = false;
        m_Matchmaker = null;
    }
}


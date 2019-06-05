using SQP;
using UnityEngine;

public class SQPDriver : MonoBehaviour
{
    // SQP data
    public static ushort ServerPort { get; set; } = 0;
    public ushort CurrentPlayersCount = 0;
    public ushort MaxPlayersCount = 0;
    public string ServerMapName = "";
    public string GameType = "";
    public string ServerName = "";

    ServerInfo.Data m_SQPData;
    SQP.SQPServer m_SQPServer;

    ushort m_SQPPort = 7777;

    void Start()
    {
        ushort newPort = 0;
        if (CommandLine.TryGetCommandLineArgValue("-query_port", out newPort))
            m_SQPPort = newPort;
    }

    void Update()
    {
        if (m_SQPServer == null)
        {
            if (ServerPort == 0)
                return;

            m_SQPServer = new SQP.SQPServer(m_SQPPort);
            m_SQPData = m_SQPServer.ServerInfoData;
            Debug.Log($"SQPDriver initialized.  Responding to SQP queries on port {m_SQPPort}...");
        }
        else
        {
            // Set server data
            m_SQPData.Map = ServerMapName;
            m_SQPData.Port = ServerPort;
            m_SQPData.BuildId = Application.unityVersion;
            m_SQPData.MaxPlayers = MaxPlayersCount;
            m_SQPData.CurrentPlayers = CurrentPlayersCount;
            m_SQPData.GameType = GameType;
            m_SQPData.ServerName = ServerName;

            // Tick SQP 'server'
            m_SQPServer.Update();
        }
    }
}

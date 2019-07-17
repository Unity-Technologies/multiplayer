using System;
using Unity.Networking.Transport;
using UnityEngine;
using UnityEngine.Ucg.Matchmaking;

namespace MultiplayPingSample.Client
{
    // The PingClientUIBehaviour is responsible for displaying statistics of a
    //   running ping client as well as for starting and stopping the ping of a
    //   selected ip.
    public class MultiplayPingClientUIBehaviour : MonoBehaviour
    {
        static bool s_Headless;

        string m_CustomIp = "";
        Matchmaker m_Matchmaker;
        Matchmaker.MatchmakingState m_LastMatchmakingState;
        MultiplayPingClient m_PingClient;
        bool m_HeadlessShouldTerminateServer;
        bool m_HeadlessShouldPingServer;
        bool m_HeadlessShouldMatchmake;
        bool m_UseDefaultsIfParsingFails = false;
        ushort m_HeadlessRunTimeMs = 5000;
        float m_PingUntil;
        PingStats m_Stats;

        // Vars that can be set via the inspector
        public ushort DefaultServerPortToPing = 9000;
        public ushort ClientTargetFps = 60;
        public string MatchmakingServer = "";

        // Initialization code w/o dependencies
        void Awake()
        {
            CommandLine.PrintArgsToLog();

            s_Headless = CommandLine.HasArgument("-nographics") || CommandLine.HasArgument("-batchmode");
            m_HeadlessShouldPingServer = CommandLine.HasArgument("-p") || CommandLine.HasArgument("-ping");
            m_HeadlessShouldTerminateServer = CommandLine.HasArgument("-k") || CommandLine.HasArgument("-kill");;

            if(!CommandLine.TryUpdateVariableWithArgValue(ref m_CustomIp, "-e"))
                CommandLine.TryUpdateVariableWithArgValue(ref m_CustomIp, "-endpoint");

            CommandLine.TryUpdateVariableWithArgValue(ref m_HeadlessRunTimeMs, "-t");

            if (s_Headless)
                Debug.Log("Running Ping Client in headless mode.");
        }

        // Overwrite values from GameObject w/ args if present
        void Start()
        {
            CommandLine.TryUpdateVariableWithArgValue(ref ClientTargetFps, "-fps");
            m_HeadlessShouldMatchmake = CommandLine.TryUpdateVariableWithArgValue(ref MatchmakingServer, "-mm");

            if (ClientTargetFps > 0)
                Application.targetFrameRate = ClientTargetFps;

            // If requested FPS is different from current screen resolution, disable VSync
            if (ClientTargetFps != Screen.currentResolution.refreshRate)
                QualitySettings.vSyncCount = 0;

            ValidateArguments();
        }

        // Validate arguments are correct
        void ValidateArguments()
        {
            // Validate headless mode called with minimal required arguments
            if (s_Headless && ((!m_HeadlessShouldPingServer && !m_HeadlessShouldTerminateServer) || string.IsNullOrEmpty(m_CustomIp)))
            {
                Debug.LogError("Ping client started in headless mode, but was not provided with the proper arguments.\n" +
                    "You must specify at least an endpoint (-endpoint) and either -p (ping), -k (terminate), or both.");

                Debug.Log("-p : " + m_HeadlessShouldPingServer);
                Debug.Log("-k : " + m_HeadlessShouldTerminateServer);
                Debug.Log("-endpoint : " + m_CustomIp);

                EndProgram(1);
            }

            // Validate matchmaking
            if (s_Headless && !string.IsNullOrEmpty(m_CustomIp) && !string.IsNullOrEmpty(MatchmakingServer))
            {
                Debug.LogError("Ping client started in headless mode, but was not provided with the proper arguments.\n" +
                    "You cannot specify an endpoint and a matchmaker at the same time.");

                EndProgram(1);
            }
        }

        // Update the state machines
        void Update()
        {
            if (m_Matchmaker != null)
            {
                m_Matchmaker.Update();
                m_LastMatchmakingState = m_Matchmaker.State;
            }
                
            if (s_Headless)
                UpdateForHeadless();
            else
                UpdateForGUI();
        }

        void UpdateForHeadless()
        {
            // Don't do anything if we're waiting for matchmaking
            if (m_Matchmaker != null)
                return;

            // Start matchmaking if it was requested via command-line flags
            if (m_HeadlessShouldMatchmake)
            {
                StartNewMatchmakingRequest();
                return;
            }

            // By the time we get to here, MM has already succeeded or failed or we never needed to start it
            if (m_LastMatchmakingState == Matchmaker.MatchmakingState.Error)
            {
                Debug.Log("Matchmaking failed, aborting headless run and shutting down ping client.");
                EndProgram(1);
            }

            // Start ping client if we need to start it
            if (m_PingClient == null)
            {
                if (m_HeadlessShouldPingServer)
                {
                    StartNewPingClient();
                    var pingSeconds = m_HeadlessRunTimeMs / 1000f;
                    Debug.Log($"Pinging remote server for {pingSeconds} seconds...");
                    m_PingUntil = Time.fixedUnscaledTime + pingSeconds;
                }
                else if (m_HeadlessShouldTerminateServer)
                {
                    TerminateRemoteServer();
                    EndProgram(0);
                }
            }
            else
            {
                // Keep pinging until we run out of time
                if (Time.fixedUnscaledTime < m_PingUntil)
                {
                    m_PingClient.RunPingGenerator();
                }
                else
                {
                    // Dump stats before we dispose of the client
                    Debug.Log($"STATS: Pings sent: {m_Stats.TotalPings}" +
                        $"\nSTATS: Last ping: {m_Stats.LastPing}" +
                        $"\nSTATS: Total Average ping: {m_Stats.TotalAverage}" +
                        $"\nSTATS: Average of last 50 pings: {m_Stats.GetRollingAverage()}" +
                        $"\nSTATS: Pings per second: {m_Stats.PingsPerSecond()}");

                    if (m_HeadlessShouldTerminateServer) TerminateRemoteServer();

                    Debug.Log("Finished headless mode tasks, shutting down...");

                    EndProgram(0);
                }
            }
        }

        void UpdateForGUI()
        {
            m_PingClient?.RunPingGenerator();
        }

        // TODO - see if this is still necessary to avoid errors
        void LateUpdate()
        {
            if(m_PingClient != null && m_PingClient.updateHandle.IsCompleted)
                m_PingClient?.updateHandle.Complete();
        }

        // UI driver
        void OnGUI()
        {
            if (s_Headless)
                return;

            if (m_Matchmaker != null)
            {
                ShowMatchmakingUI();
            }
            else
            {
                if (m_PingClient == null)
                    ShowPingMainUI();
                else
                    ShowPingCancelUI();

                ShowStatsUI();
            }
        }

        // Clean up resources when this MonoBehaviour object is destroyed
        void OnDestroy()
        {
            ShutdownPingClient();
        }

        void ShowStatsUI()
        {
            GUILayout.Label(
                $"Ping number:\t {m_Stats?.TotalPings ?? 0}" +
                $"\nLatest ping:\t {m_Stats?.LastPing ?? 0} ms" +
                $"\nAverage ping:\t {m_Stats?.TotalAverage ?? 0:F} ms" +
                $"\n50-ping Average:\t {m_Stats?.GetRollingAverage() ?? 0:F} ms" +
                $"\nBest Ping: \t {m_Stats?.BestPing ?? 0} ms" +
                $"\nWorst Ping:\t {m_Stats?.WorstPing ?? 0} ms" +
                $"\nPings per second:\t {m_Stats?.PingsPerSecond()?? 0:F}" +
                $"\n\nFPS: {(1.0f / Time.smoothDeltaTime):F}"
                );
        }

        // UI for when no active pinging or matchmaking request is happening
        void ShowPingMainUI()
        {
            GUILayout.Label("Ping Server Endpoint (empty = default):");
            m_CustomIp = GUILayout.TextField(m_CustomIp);

            // [Start Ping] button
            if (GUILayout.Button("Start pinging server"))
                StartNewPingClient();

            // [Terminate Server] button
            if (GUILayout.Button("Terminate server at specified endpoint"))
                TerminateRemoteServer();

            // [Matchmaking] button
            if (GUILayout.Button("Use Matchmaking to find a server"))
            {
                if(!string.IsNullOrEmpty(MatchmakingServer))
                    StartNewMatchmakingRequest();
                else
                    Debug.Log("Cannot start matchmaking - No server endpoint was entered");
            }

        }

        // UI for when a server is being pinged
        void ShowPingCancelUI()
        {
            if (GUILayout.Button("Stop ping"))
                ShutdownPingClient();

            if (GUILayout.Button("Terminate connected server"))
            {
                Debug.Log("Terminating connected PingServer.");
                m_PingClient?.ShutdownRemoteServer();
                ShutdownPingClient();
            }
        }

        void TerminateRemoteServer()
        {
            Debug.Log("Terminating remote PingServer");
            StartNewPingClient();
            m_PingClient?.ShutdownRemoteServer();
            ShutdownPingClient();
        }

        // UI for when a match is being searched for
        void ShowMatchmakingUI()
        {
            GUILayout.Label("Matchmaking");
            GUILayout.Label("Finding a server...");

            if (GUILayout.Button("Cancel"))
                EndMatchmaking();
        }

        // Properly dispose of Ping Client
        void ShutdownPingClient()
        {
            if (m_PingClient != null)
            {
                Debug.Log("Shutting down existing MultiplayPingClient");
                m_PingClient.Dispose();
                m_Stats?.StopMeasuring();
            }

            m_PingClient = null;
        }

        // Initialize a new Ping Client by processing values input by user
        void StartNewPingClient()
        {
            // Make sure we properly shut down the old ping client before getting a new one
            ShutdownPingClient();

            // Start with a default IPv4 endpoint
            var serverEndPoint = NetworkEndPoint.LoopbackIpv4;
            serverEndPoint.Port = DefaultServerPortToPing;
            var address = "loopback";

            // Fix common formatting issues
            m_CustomIp = m_CustomIp.Trim().Replace(" ", "");
            var port = DefaultServerPortToPing;

            // Attempt to parse server endpoint string into a NetworkEndPoint
            var usedDefaultIp = true;
            var usedDefaultPort = true;

            if (!string.IsNullOrEmpty(m_CustomIp))
            {
                var endpoint = m_CustomIp.Split(':');

                switch (endpoint.Length) {
                    case 1:
                        // Try to parse IP, but use default port
                        usedDefaultIp = false;
                        break;
                    case 2:
                        // Try to parse IP
                        usedDefaultIp = false;
                        // Try to parse port
                        usedDefaultPort = !ushort.TryParse(endpoint[1].Trim(), out port);
                        break;
                    default:
                        // Any other cases automatically require both IP and Port to be defaulted
                        break;
                }

                // Try to parse the first element into an IP address
                if (!usedDefaultIp && !NetworkEndPoint.TryParse(endpoint[0], port, out serverEndPoint))
                    usedDefaultIp = true;
                else
                    address = endpoint[0];
            }

            // If we're not allowed to fall back to defaults and we need to, error
            if (!m_UseDefaultsIfParsingFails && (usedDefaultIp || usedDefaultPort))
                throw new ArgumentException("Could not fully parse endpoint");

            // Success - Spin up the new MultiplayPingClient
            Debug.Log($"Connecting to PingServer at {address}:{port}.");
            m_PingClient = new MultiplayPingClient(serverEndPoint);
            m_Stats = m_PingClient.Stats;
        }

        // Initialize a new matchmaking request and set UI state to Matchmaking In Progress
        // Precondition: MatchmakingServer is not null and contains a valid matchmaking URI
        void StartNewMatchmakingRequest()
        {
            // Abort existing matchmaker
            if (m_Matchmaker != null)
                EndMatchmaking();

            m_LastMatchmakingState = default(Matchmaker.MatchmakingState);

            MatchmakingServer = MatchmakingServer.Trim();

            m_Matchmaker = new Matchmaker(MatchmakingServer, OnMatchmakingSuccess, OnMatchmakingError);

            var matchId = Guid.NewGuid().ToString();
            var playerProps = new MatchmakingUtilities.PlayerProperties { hats = 5 };
            var groupProps = new MatchmakingUtilities.GroupProperties { mode = 0 };
            var request = MatchmakingUtilities.CreateMatchmakingRequest(matchId, playerProps, groupProps);

            m_Matchmaker.RequestMatch(request);
            m_LastMatchmakingState = m_Matchmaker.State;
        }

        void EndMatchmaking()
        {
            // Headless only supports 1 mm attempt, so if we end/abort, we're done
            m_HeadlessShouldMatchmake = false;

            // Clean up existing matchmaker
            if (m_Matchmaker != null)
            {
                m_LastMatchmakingState = m_Matchmaker.State;

                // See if we need to cancel an existing matchmaking request
                if (!m_Matchmaker.Done)
                {
                    // TODO: Add cancel functionality when matchmaking client library is updated with it
                }
            }
            
            m_Matchmaker = null;
        }

        // Callback for when matchmaking completes with SUCCESS
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

            EndMatchmaking();
        }

        // Callback for when matchmaking completes with FAILURE
        void OnMatchmakingError(string errorInfo)
        {
            Debug.Log($"Matchmaking failed! Error is: {errorInfo}.");
            EndMatchmaking();
        }

        static void EndProgram(int exitCode = 0)
        {
            Debug.Log("Shutting down Ping Client");

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit(exitCode);
#endif
        }
    }
}

using System;
using System.IO;
using System.Net;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Networking.Transport;
using Unity.Ucg.Usqp;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MultiplayPingSample.Server
{
    public class MultiplayPingServer : IDisposable
    {
        const string k_ExampleConfigPath = @"ExampleConfig.cfg";

        Config m_Config;
        NetworkEndPoint m_PingServerEndpoint;
        UsqpServer m_SqpServer;
        JobHandle m_UpdateHandle;
        bool m_ShuttingDown;

        // Disposables
        NativeArray<bool> m_ShouldShutdownServer;
        NativeList<NetworkConnection> m_Connections;
        UdpNetworkDriver m_ServerDriver;

        // Initialization data which can be edited in the inspector
        [Serializable]
        public class Config
        {
            public ServerInfo.Data Info = new ServerInfo.Data
            {
                BuildId = "Generated at run time",
                CurrentPlayers = 0,
                GameType = "Ping Game",
                Map = "Ping Map",
                MaxPlayers = 16,
                Port = 9000,
                ServerName = "Ping Server"
            };
            public string ServerIpAddress = "127.0.0.1";
            public ushort ServerSqpPort = 9010;
        }

        // Constructor
        public MultiplayPingServer(Config config)
        {
            m_Config = config ?? new Config();

            try
            {
                UpdateAndValidateConfig();
                InitializePingServer();
                InitializeSqpServer();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                ShutdownServer(1);
            }
        }

        // Load ping server overrides from command-line and do config validation
        void UpdateAndValidateConfig()
        {
            // Override config values with values passed in from command-line
            CommandLine.TryUpdateVariableWithArgValue(ref m_Config.Info.Port, "-port");
            CommandLine.TryUpdateVariableWithArgValue(ref m_Config.ServerIpAddress, "-ip");
            CommandLine.TryUpdateVariableWithArgValue(ref m_Config.ServerSqpPort, "-query_port");

            // Read or write config file depending on arguments specified
            if (CommandLine.TryGetCommandLineArgValue("-config", out string configFilePath))
                ReadConfigFile(configFilePath);
            else
                TryWriteExampleConfigFile(k_ExampleConfigPath, false);

            // Validate that ports are valid
            if (m_Config.ServerSqpPort == m_Config.Info.Port)
                throw new ArgumentException("-port and -query_port cannot be the same");

            // Try to create a valid endpoint from the included IP and port
            if (!NetworkEndPoint.TryParse(m_Config.ServerIpAddress, m_Config.Info.Port, out m_PingServerEndpoint))
                throw new ArgumentException("server IP address was not valid");

            Debug.Log("Attemping to start Ping Server with the following configuration:" +
                $"\n{nameof(m_Config.ServerIpAddress)}: {m_Config.ServerIpAddress}" +
                $"\n{nameof(m_Config.Info.Port)}: {m_Config.Info.Port}" +
                $"\n{nameof(m_Config.ServerSqpPort)}: {m_Config.ServerSqpPort}" +
                $"\n{nameof(m_Config.Info.ServerName)}: {m_Config.Info.ServerName}" +
                $"\n{nameof(m_Config.Info.BuildId)}: {m_Config.Info.BuildId}" +
                $"\n{nameof(m_Config.Info.Map)}: {m_Config.Info.Map}" +
                $"\n{nameof(m_Config.Info.GameType)}: {m_Config.Info.GameType}" +
                $"\n{nameof(m_Config.Info.MaxPlayers)}: {m_Config.Info.MaxPlayers}" +
                $"\n{nameof(m_Config.Info.CurrentPlayers)}: {m_Config.Info.CurrentPlayers}"
            );
        }

        // Create the ping server driver, bind it to a port and start listening for incoming connections
        void InitializePingServer()
        {
            m_ServerDriver = new UdpNetworkDriver(new INetworkParameter[0]);

            var bindError = m_ServerDriver.Bind(m_PingServerEndpoint);

            if (bindError != 0)
            {
                Debug.LogError($"Failed to bind to {m_Config.ServerIpAddress ?? "0.0.0.0"}:{m_Config.Info.Port}.  Bind error code: {bindError}.");
                ShutdownServer(1);
            }
            else
            {
                Debug.Log($"Network driver listening for traffic on {m_Config.ServerIpAddress ?? "0.0.0.0"}:{m_Config.Info.Port}");
                m_ServerDriver.Listen();
            }

            m_Connections = new NativeList<NetworkConnection>(m_Config.Info.MaxPlayers, Allocator.Persistent);

            m_ShouldShutdownServer = new NativeArray<bool>(1, Allocator.Persistent)
            {
                [0] = false
            };
        }

        void InitializeSqpServer()
        {
            // Spin up a new SQP server
            var address = IPAddress.Parse(m_Config.ServerIpAddress);
            var endpoint = new IPEndPoint(address, m_Config.ServerSqpPort);

            m_SqpServer = new UsqpServer(endpoint)
            {
                // Use our GameObject's SQP data as the server's data
                ServerInfoData = m_Config.Info
            };
        }

        // Try to write an example config file
        static bool TryWriteExampleConfigFile(string configFilePath, bool overwrite)
        {
            if (!overwrite && File.Exists(configFilePath))
                return false;

            try
            {
                using (var writer = new StreamWriter(configFilePath, false))
                {
                    writer.Write(JsonUtility.ToJson(new Config(), true));
                }

                Debug.Log("Wrote example config file to current directory.");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("Unable to write example configuration file due to exception.");
                Debug.LogException(e);
                return false;
            }
        }

        // Try to read a config file - will throw exceptions if it fails
        public void ReadConfigFile(string configFilePath)
        {
            try
            {
                Config configFromFile;

                using (var file = File.OpenRead(configFilePath))
                using (var reader = new StreamReader(file))
                {
                    configFromFile = JsonUtility.FromJson<Config>(reader.ReadToEnd());
                }

                if (configFromFile != null)
                {
                    // Overwrite existing configuration information with the new config data
                    m_Config = configFromFile;
                    Debug.Log($"SQP config file loaded from {configFilePath}.");
                    return;
                }

                // Trying to load a config file and failing should abort the process in production
                throw new ArgumentException("Unable to load SQP configuration file - could not deserialize data.");
            }
            catch (Exception e)
            {
                Debug.LogError("Unable to load SQP configuration file due to exception: " + e.Message);
                throw;
            }
        }

        // Turn off the server (application)
        void ShutdownServer(int exitCode = 0)
        {
            m_ShuttingDown = true;
            Debug.Log("PingServer shutting down...");
#if UNITY_EDITOR
            EditorApplication.isPlaying = false;
#else
            Application.Quit(exitCode);
#endif
        }

        // Polite update - Wait for our jobs to complete naturally
        public void Update()
        {
            if (m_Disposed || m_ShuttingDown)
                return;

            m_SqpServer?.Update();

            // Don't do anything more unless our jobs have been completed
            if (!m_UpdateHandle.IsCompleted)
                return;

            // Wait for the previous frames ping to complete before starting a new one
            m_UpdateHandle.Complete();

            // Update the SQP server
            if (m_SqpServer != null)
                m_Config.Info.CurrentPlayers = (ushort)m_Connections.Length;

            // If there is at least one client connected update the activity so the server is not shutdown
            if (m_Connections.Length > 0)
                DedicatedServerConfig.UpdateLastActivity();

            // Update the network drivers and our list of active connections
            WaitForNetworkUpdate();

            // Figure out if we should wait for (and respond to) pings or shut down the server
            if (m_ShouldShutdownServer[0])
            {
                DisconnectClientsAndShutdown();
            }
            else
            {
                SchedulePongJob();

                // Put our jobs on the stack to be processed without waiting for completion in this frame
                JobHandle.ScheduleBatchedJobs();
            }
        }

        void WaitForNetworkUpdate()
        {
            // The DriverUpdateJob which accepts new connections should be the second job in the chain,
            //  it needs to depend on the driver update job
            var updateJob = new DriverUpdateJob
            {
                driver = m_ServerDriver,
                connections = m_Connections
            };

            // Update the driver should be the first job in the chain
            m_UpdateHandle = m_ServerDriver.ScheduleUpdate(m_UpdateHandle);
            m_UpdateHandle = updateJob.Schedule(m_UpdateHandle);

            // Wait for the job to complete
            m_UpdateHandle.Complete();
        }

        // Disconnect all clients and shut down the server
        void DisconnectClientsAndShutdown()
        {
            Debug.Log("Server detected remote shutdown signal.  Disconnecting all clients and shutting down server.");

            var disconnectJob = new DisconnectAllClientsJob
            {
                driver = m_ServerDriver,
                connections = m_Connections
            };

            // Finish up all other active jobs
            m_UpdateHandle.Complete();

            // Schedule a new disconnect job
            m_UpdateHandle = disconnectJob.Schedule();

            // Update the driver after the disconnect
            m_UpdateHandle = m_ServerDriver.ScheduleUpdate(m_UpdateHandle);

            // Wait for everything to finish
            m_UpdateHandle.Complete();

            // Terminate the server
            ShutdownServer(0);
        }

        // Schedule a pong job, which looks for pings and replies to them
        void SchedulePongJob()
        {
            var pongJob = new PongJob
            {
                // Check to see if we need to shut down after processing data
                shouldShutdown = m_ShouldShutdownServer,

                // PongJob is a ParallelFor job, it must use the concurrent NetworkDriver
                driver = m_ServerDriver.ToConcurrent(),

                // IJobParallelForDeferExtensions is not working correctly with IL2CPP
                connections = m_Connections
            };

            // PongJob uses IJobParallelForDeferExtensions, we *must* schedule with a list as first parameter rather than
            // an int since the job needs to pick up new connections from DriverUpdateJob
            // The PongJob is the last job in the chain and it must depends on the DriverUpdateJob
            m_UpdateHandle = pongJob.Schedule(m_UpdateHandle);
        }

        [BurstCompile]
        struct DriverUpdateJob : IJob
        {
            public UdpNetworkDriver driver;
            public NativeList<NetworkConnection> connections;

            public void Execute()
            {
                // Remove connections which have been destroyed from the list of active connections
                for (var i = 0; i < connections.Length; ++i)
                    if (!connections[i].IsCreated)
                    {
                        connections.RemoveAtSwapBack(i);
                        // Index i is a new connection since we did a swap back, check it again
                        --i;
                    }

                // Accept all new connections
                while (true)
                {
                    var con = driver.Accept();

                    // "Nothing more to accept" is signaled by returning an invalid connection from accept
                    if (!con.IsCreated)
                        break;

                    connections.Add(con);
                }
            }
        }

        [BurstCompile]
        struct DisconnectAllClientsJob : IJob
        {
            public UdpNetworkDriver driver;
            public NativeList<NetworkConnection> connections;

            public void Execute()
            {
                for (var i = 0; i < connections.Length; ++i)
                    connections[i].Disconnect(driver);
            }
        }

        static NetworkConnection ProcessSingleConnection(UdpNetworkDriver.Concurrent driver, NetworkConnection connection, out bool terminateServer)
        {
            terminateServer = false;
            NetworkEvent.Type cmd;

            // Pop all events for the connection
            while ((cmd = driver.PopEventForConnection(connection, out var dataStreamReader)) != NetworkEvent.Type.Empty)
                if (cmd == NetworkEvent.Type.Data)
                {
                    // For ping requests we reply with a pong message
                    // A DataStreamReader.Context is required to keep track of current read position since
                    // DataStreamReader is immutable
                    var readerCtx = default(DataStreamReader.Context);
                    var id = dataStreamReader.ReadInt(ref readerCtx);

                    // Terminate server if "magic number" received
                    if (id == -1)
                        terminateServer = true;

                    // Create a temporary DataStreamWriter to keep our serialized pong message
                    var pongData = new DataStreamWriter(4, Allocator.Temp);
                    pongData.Write(id);

                    // Send the pong message with the same id as the ping
                    driver.Send(NetworkPipeline.Null, connection, pongData);
                }
                else if (cmd == NetworkEvent.Type.Disconnect)
                {
                    // When disconnected we make sure the connection return false to IsCreated so the next frames
                    // DriverUpdateJob will remove it
                    return default(NetworkConnection);
                }

            return connection;
        }

        [BurstCompile]
        struct PongJob : IJob
        {
            public UdpNetworkDriver.Concurrent driver;
            public NativeList<NetworkConnection> connections;

            [WriteOnly]
            public NativeArray<bool> shouldShutdown;

            public void Execute()
            {
                for (var i = 0; i < connections.Length; ++i)
                {
                    connections[i] = ProcessSingleConnection(driver, connections[i], out var shouldShutdownServer);

                    if (shouldShutdownServer)
                        shouldShutdown[0] = true;
                }
            }
        }
        bool m_Disposed;

        public void Dispose()
        {
            if (m_Disposed)
                return;

            // All jobs must be completed before we can dispose the data they use
            m_UpdateHandle.Complete();
            m_ServerDriver.Dispose();
            m_Connections.Dispose();
            m_ShouldShutdownServer.Dispose();
            m_SqpServer.Dispose();

            m_Disposed = true;
        }

        ~MultiplayPingServer()
        {
            Dispose();
        }
    }
}

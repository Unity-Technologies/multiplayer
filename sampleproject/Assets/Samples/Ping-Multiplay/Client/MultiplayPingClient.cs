using System;
using System.Diagnostics;
using Unity.Collections;
using Unity.Jobs;
using Unity.Networking.Transport;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace MultiplayPingSample.Client
{
    public class MultiplayPingClient : IDisposable
    {
        readonly NetworkEndPoint m_ServerEndpoint;
        UdpNetworkDriver m_ClientDriver;
        NativeArray<NetworkConnection> m_ClientToServerConnection;

        // pendingPings is an array of pings sent to the server which have not yet received a response.
        // Currently we only support one ping in-flight
        NativeArray<PendingPing> m_PendingPings;

        // The ping stats are two integers, time for last ping and number of pings
        NativeArray<uint> m_NumPings;
        NativeArray<ushort> m_LastPing;

        public JobHandle updateHandle;

        bool m_RemoteServerHasShutDown;
        const int k_PingTimeoutMs = 5000;
        const int k_ServerShutdownTimeoutMs = 5000;

        public MultiplayPingClient(NetworkEndPoint serverEndpoint)
        {
            if (serverEndpoint == null || !serverEndpoint.IsValid)
                throw new ArgumentException($"{nameof(serverEndpoint)} must be valid.");

            m_ServerEndpoint = serverEndpoint;

            // Create a NetworkDriver for the client. We could bind to a specific address but in this case we rely on the
            // implicit bind since we do not need to bing to anything special
            m_ClientDriver = new UdpNetworkDriver(new INetworkParameter[0]);
            m_ClientToServerConnection = new NativeArray<NetworkConnection>(1, Allocator.Persistent);

            // Initialize ping stats
            Stats = new PingStats(50);
            m_PendingPings = new NativeArray<PendingPing>(64, Allocator.Persistent);
            m_NumPings = new NativeArray<uint>(1, Allocator.Persistent);
            m_LastPing = new NativeArray<ushort>(1, Allocator.Persistent);
        }

        public PingStats Stats { get; }

        // Shut down a remote server by sending it a special packet
        public void ShutdownRemoteServer()
        {
            if (m_RemoteServerHasShutDown || m_Disposed)
                return;

            RunServerShutdownJob();
        }

        // Send a ping to the remote server
        public void RunPingGenerator()
        {
            if (m_RemoteServerHasShutDown || m_Disposed)
                return;

            RunPingJob();
        }

        // Non-blocking; ticks ping state machine once
        //   3+ ticks required to do a full cycle (connect, send, receive + disconnect)
        void RunPingJob()
        {
            // Wait for existing job to finish
            updateHandle.Complete();

            // Write stats from last job
            Stats.AddEntry(m_NumPings[0], m_LastPing[0]);

            // Disconnect if we exceeded our disconnect timeout
            if (m_ClientToServerConnection.Length > 0 &&
                m_ClientToServerConnection[0].IsCreated &&
                m_NumPings[0] == m_PendingPings[0].id &&
                (Time.fixedTime - m_PendingPings[0].time > k_PingTimeoutMs))
            {
                Debug.Log($"Resetting connection to ping server due to ping timeout (time waiting for response > {k_PingTimeoutMs} ms).");
                m_ClientToServerConnection[0].Disconnect(m_ClientDriver);
                m_ClientToServerConnection[0] = default(NetworkConnection);
            }

            // Schedule driver update
            updateHandle = m_ClientDriver.ScheduleUpdate();

            var pingJob = new PingJob
            {
                driver = m_ClientDriver,
                connection = m_ClientToServerConnection,
                serverEp = m_ServerEndpoint,
                pendingPings = m_PendingPings,
                numPings = m_NumPings,
                lastPing = m_LastPing
            };

            // Schedule an update chain with the driver update followed by the ping job
            updateHandle = pingJob.Schedule(updateHandle);

            JobHandle.ScheduleBatchedJobs();
        }

        // Blocks until remote server is terminated
        void RunServerShutdownJob()
        {
            // Wait for existing job to finish
            updateHandle.Complete();

            Debug.Log("Sending shutdown signal to remote server");

            // Disconnect existing connection
            if (m_ClientToServerConnection.Length > 0 && m_ClientToServerConnection[0].IsCreated)
            {
                m_ClientToServerConnection[0].Disconnect(m_ClientDriver);
                m_ClientToServerConnection[0] = default(NetworkConnection);
            }

            // Loop disconnect and driver update jobs until we actually disconnect - or time out
            var done = false;
            var timer = Stopwatch.StartNew();

            using (var disconnectedValue = new NativeArray<bool>(1, Allocator.Persistent))
                while (!done && timer.ElapsedMilliseconds < k_ServerShutdownTimeoutMs)
                {
                    var serverShutdownJob = new TerminateRemoteServerJob
                    {
                        driver = m_ClientDriver,
                        connection = m_ClientToServerConnection,
                        serverEp = m_ServerEndpoint,
                        disconnected = disconnectedValue
                    };

                    // Schedule a chain with the driver update followed by the ping job
                    updateHandle = m_ClientDriver.ScheduleUpdate();

                    // Schedule 1st run of server shutdown job (connect)
                    updateHandle = serverShutdownJob.Schedule(updateHandle);

                    // Wait for the server shutdown job to complete
                    updateHandle.Complete();

                    // Check to see if we're successfully disconnected
                    if (disconnectedValue[0])
                    {
                        done = true;
                        timer.Stop();
                    }
                }

            // Validate disconnect
            if (timer.IsRunning)
            {
                Debug.Log($"Failed to terminate remote server (Time elapsed > Timeout value ({k_ServerShutdownTimeoutMs} ms))");
            }
            else
            {
                m_RemoteServerHasShutDown = true;
                Debug.Log("Remote server successfully terminated.");
            }
        }

        struct PendingPing
        {
            public uint id;
            public long time;
        }

        struct PingJob : IJob
        {
            public NativeArray<NetworkConnection> connection;
            public NativeArray<PendingPing> pendingPings;
            public NativeArray<uint> numPings;

            [WriteOnly] public NativeArray<ushort> lastPing;

            public UdpNetworkDriver driver;
            public NetworkEndPoint serverEp;

            public void Execute()
            {
                // If we should be sending pings but we do not have an active connection we create one
                if (!connection[0].IsCreated)
                    connection[0] = driver.Connect(serverEp);

                NetworkEvent.Type cmd;

                // Process all events on the connection. If the connection is invalid it will return Empty immediately
                while ((cmd = connection[0].PopEvent(driver, out var pingStream)) != NetworkEvent.Type.Empty)
                    switch (cmd)
                    {
                        case NetworkEvent.Type.Connect:

                            // When we get the connect message we can start sending data to the server
                            // Set the ping id to a sequence number for the new ping we are about to send
                            pendingPings[0] = new PendingPing
                            {
                                id = numPings[0],
                                time = DateTime.UtcNow.Ticks
                            };

                            // Create a 4 byte data stream which we can store our ping sequence number in
                            var pingData = new DataStreamWriter(4, Allocator.Temp);
                            pingData.Write(numPings[0]);
                            connection[0].Send(driver, pingData);

                            // Update the number of sent pings
                            numPings[0] = numPings[0] + 1;
                            break;

                        case NetworkEvent.Type.Data:
                            lastPing[0] = (ushort)((DateTime.UtcNow.Ticks - pendingPings[0].time) / TimeSpan.TicksPerMillisecond);

                            // Validate data from pong matches ping
                            // TODO: Change if we ever support more than 1 pending ping
                            var readerCtx = default(DataStreamReader.Context);
                            var id = pingStream.ReadInt(ref readerCtx);
                            var pingId = numPings[0] - 1;
                            if(id != pingId)
                                Debug.LogWarning($"Received pong from server, but got wrong sequence number (Expected {pingId} but got {id})");
                            
                            connection[0].Disconnect(driver);
                            connection[0] = default(NetworkConnection);
                            break;

                        case NetworkEvent.Type.Disconnect:
                            // If the server disconnected us we clear out connection
                            connection[0] = default(NetworkConnection);
                            break;
                    }
            }
        }

        struct TerminateRemoteServerJob : IJob
        {
            public NativeArray<NetworkConnection> connection;
            public UdpNetworkDriver driver;
            public NetworkEndPoint serverEp;
            public NativeArray<bool> disconnected;

            public void Execute()
            {
                // If the client ui indicates we should be sending pings but we do not have an active connection we create one
                if (!connection[0].IsCreated)
                {
                    //Debug.Log(@"serverEP.IsValid && !connection[0].IsCreated");
                    connection[0] = driver.Connect(serverEp);
                }

                NetworkEvent.Type cmd;
                disconnected[0] = false;

                // Process all events on the connection. If the connection is invalid it will return Empty immediately
                while ((cmd = connection[0].PopEvent(driver, out var pingStream)) != NetworkEvent.Type.Empty)
                    switch (cmd)
                    {
                        case NetworkEvent.Type.Connect:
                            // Terminate the server by sending a value of -1
                            var pingData = new DataStreamWriter(4, Allocator.Temp);
                            pingData.Write(-1);
                            connection[0].Send(driver, pingData);
                            break;

                        case NetworkEvent.Type.Data:
                            // Confirm the termination code was echoed
                            var readerCtx = default(DataStreamReader.Context);
                            var id = pingStream.ReadInt(ref readerCtx);
                            if (id != -1)
                                Debug.LogWarning($"Expected server to send back termination signal, but received {id} instead");

                            // Disconnect after sending termination code
                            connection[0].Disconnect(driver);
                            connection[0] = default(NetworkConnection);
                            disconnected[0] = true;
                            break;

                        case NetworkEvent.Type.Disconnect:
                            // If the server disconnected us we clear out connection
                            connection[0] = default(NetworkConnection);
                            disconnected[0] = true;
                            break;
                    }
            }
        }

        bool m_Disposed = false; // To detect redundant calls

        public void Dispose()
        {
            if (!m_Disposed)
            {
                //All jobs must be completed before we can dispose the data they use
                updateHandle.Complete();

                // Dispose unmanaged resources
                m_ClientDriver.Dispose();
                m_PendingPings.Dispose();
                m_NumPings.Dispose();
                m_LastPing.Dispose();

                m_ClientToServerConnection.Dispose();

                m_Disposed = true;
                GC.SuppressFinalize(this);
            }
        }

        ~MultiplayPingClient()
        {
            this.Dispose();
        }

    }
}

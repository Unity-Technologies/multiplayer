using System;
using UnityEngine;

namespace MultiplayPingSample.Server
{
    // Simple monobehavior driver for the underlying MultiplayPingServer
    //  Also exposes configuration data in inspector
    public class MultiplayPingServerBehaviour : MonoBehaviour
    {
        // The underlying server this class is controlling
        MultiplayPingServer m_Server;

        [Tooltip("Server configuration.  Properties set on the GameObject will be overridden by commandline arguments.")]
        public MultiplayPingServer.Config ServerConfig;

        void Start()
        {
            var version = $"PingSample_{Application.buildGUID}_{Application.unityVersion}";
            ServerConfig.Info.BuildId = version;

            m_Server = new MultiplayPingServer(ServerConfig);
        }

        void OnDestroy()
        {
            m_Server?.Dispose();
            m_Server = null;
        }

        void Update()
        {
            // Update as fast as our framerate
            m_Server?.Update();
        }
    }
}

#if ENABLE_UNITY_COLLECTIONS_CHECKS
using System;
using System.Collections.Generic;
using Unity.Entities;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(TickClientSimulationSystem))]
[UpdateBefore(typeof(TickServerSimulationSystem))]
[NotClientServerSystem]
class GhostStatsSystem : ComponentSystem
{
    private DebugWebSocket m_Socket;
    private List<GhostStatsCollectionSystem> m_StatsCollections;
    protected override void OnCreateManager()
    {
        m_Socket = new DebugWebSocket(0);

    }
    protected override void OnDestroyManager()
    {
        m_Socket.Dispose();
    }

    protected override void OnUpdate()
    {
        if (m_Socket.AcceptNewConnection())
        {
            m_StatsCollections = new List<GhostStatsCollectionSystem>();
            foreach (var world in World.AllWorlds)
            {
                var stats = world.GetExistingSystem<GhostStatsCollectionSystem>();
                if (stats != null)
                {
                    m_StatsCollections.Add(stats);
                    stats.GhostStatCount = 0;
                }
            }

            string legend = "[";
            for (int i = 0; i < m_StatsCollections.Count; ++i)
            {
                if (i > 0)
                    legend += ",\n";
                legend += String.Format("{{\"name\":\"{0}\",\"ghosts\":{1}}}", m_StatsCollections[i].GetName(),
                    m_StatsCollections[i].GhostList);
            }

            legend += "]";
            m_Socket.SendText(legend);
        }

        if (!m_Socket.HasConnection)
            return;

        if (m_StatsCollections == null || m_StatsCollections.Count == 0)
            return;

        for (var con = 0; con < m_StatsCollections.Count; ++con)
        {
            for (var i = 0; i < m_StatsCollections[con].GhostStatCount; ++i)
            {
                m_StatsCollections[con].GhostStatData[m_StatsCollections[con].GhostStatSize * i] = (byte)con;
                m_Socket.SendBinary(m_StatsCollections[con].GhostStatData,
                    m_StatsCollections[con].GhostStatSize * i,
                    m_StatsCollections[con].GhostStatSize);
            }

            m_StatsCollections[con].GhostStatCount = 0;
        }
    }
}
#endif


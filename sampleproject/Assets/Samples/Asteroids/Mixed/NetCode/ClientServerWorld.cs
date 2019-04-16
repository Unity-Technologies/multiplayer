using System;
using System.Collections.Generic;
using Unity.Entities;

// Update loop for client and server worlds
[DisableAutoCreation]
[AlwaysUpdateSystem]
public class ServerSimulationSystemGroup : ComponentSystemGroup
{
    private BeginSimulationEntityCommandBufferSystem m_beginBarrier;
    private EndSimulationEntityCommandBufferSystem m_endBarrier;
    private uint m_ServerTick;
    public uint ServerTick => m_ServerTick;

    protected override void OnCreateManager()
    {
        m_beginBarrier = World.GetOrCreateManager<BeginSimulationEntityCommandBufferSystem>();
        m_endBarrier = World.GetOrCreateManager<EndSimulationEntityCommandBufferSystem>();
        m_ServerTick = 1;
    }

    protected List<ComponentSystemBase> m_systemsInGroup = new List<ComponentSystemBase>();

    public override IEnumerable<ComponentSystemBase> Systems => m_systemsInGroup;

    protected override void OnUpdate()
    {
        m_beginBarrier.Update();
        base.OnUpdate();
        m_endBarrier.Update();
        ++m_ServerTick;
        if (m_ServerTick == 0)
            ++m_ServerTick;
    }

    public override void SortSystemUpdateList()
    {
        base.SortSystemUpdateList();
        m_systemsInGroup = new List<ComponentSystemBase>(1 + m_systemsToUpdate.Count + 1);
        m_systemsInGroup.Add(m_beginBarrier);
        m_systemsInGroup.AddRange(m_systemsToUpdate);
        m_systemsInGroup.Add(m_endBarrier);
    }
}

[DisableAutoCreation]
[AlwaysUpdateSystem]
public class ClientSimulationSystemGroup : ComponentSystemGroup
{
    private BeginSimulationEntityCommandBufferSystem m_beginBarrier;
    private EndSimulationEntityCommandBufferSystem m_endBarrier;
    private GhostSpawnSystemGroup m_ghostSpawnGroup;
#if UNITY_EDITOR
    public int ClientWorldIndex { get; internal set; }
#endif

    protected override void OnCreateManager()
    {
        m_beginBarrier = World.GetOrCreateManager<BeginSimulationEntityCommandBufferSystem>();
        m_endBarrier = World.GetOrCreateManager<EndSimulationEntityCommandBufferSystem>();
        m_ghostSpawnGroup = World.GetOrCreateManager<GhostSpawnSystemGroup>();
    }

    protected List<ComponentSystemBase> m_systemsInGroup = new List<ComponentSystemBase>();

    public override IEnumerable<ComponentSystemBase> Systems => m_systemsInGroup;

    protected override void OnUpdate()
    {
        m_beginBarrier.Update();
        m_ghostSpawnGroup.Update();
        base.OnUpdate();
        m_endBarrier.Update();
    }

    public override void SortSystemUpdateList()
    {
        base.SortSystemUpdateList();
        m_systemsInGroup = new List<ComponentSystemBase>(1 + m_systemsToUpdate.Count + 1);
        m_systemsInGroup.Add(m_beginBarrier);
        m_systemsInGroup.Add(m_ghostSpawnGroup);
        m_systemsInGroup.AddRange(m_systemsToUpdate);
        m_systemsInGroup.Add(m_endBarrier);
    }
}

[DisableAutoCreation]
[AlwaysUpdateSystem]
public class ClientPresentationSystemGroup : ComponentSystemGroup
{
    private BeginPresentationEntityCommandBufferSystem m_beginBarrier;
    private EndPresentationEntityCommandBufferSystem m_endBarrier;

    protected override void OnCreateManager()
    {
        m_beginBarrier = World.GetOrCreateManager<BeginPresentationEntityCommandBufferSystem>();
        m_endBarrier = World.GetOrCreateManager<EndPresentationEntityCommandBufferSystem>();
    }

    protected List<ComponentSystemBase> m_systemsInGroup = new List<ComponentSystemBase>();

    public override IEnumerable<ComponentSystemBase> Systems => m_systemsInGroup;

    protected override void OnUpdate()
    {
        m_beginBarrier.Update();
        base.OnUpdate();
        m_endBarrier.Update();
    }

    public override void SortSystemUpdateList()
    {
        base.SortSystemUpdateList();
        m_systemsInGroup = new List<ComponentSystemBase>(1 + m_systemsToUpdate.Count + 1);
        m_systemsInGroup.Add(m_beginBarrier);
        m_systemsInGroup.AddRange(m_systemsToUpdate);
        m_systemsInGroup.Add(m_endBarrier);
    }
}

[DisableAutoCreation]
[AlwaysUpdateSystem]
public class ClientAndServerSimulationSystemGroup : ComponentSystemGroup
{
}

// Ticking of client and server worlds from the main world
#if !UNITY_CLIENT
[AlwaysUpdateSystem]
public class TickServerSimulationSystem : ComponentSystemGroup
{
    public override void SortSystemUpdateList()
    {
    }
}
#endif
#if !UNITY_SERVER
#if !UNITY_CLIENT
[UpdateAfter(typeof(TickServerSimulationSystem))]
#endif
[AlwaysUpdateSystem]
public class TickClientSimulationSystem : ComponentSystemGroup
{
    public override void SortSystemUpdateList()
    {
    }
}
[UpdateInGroup(typeof(PresentationSystemGroup))]
[AlwaysUpdateSystem]
public class TickClientPresentationSystem : ComponentSystemGroup
{
    public override void SortSystemUpdateList()
    {
    }
}
#endif

// Bootstrap of client and server worlds
public class ClientServerBootstrap : ICustomBootstrap
{
    public List<Type> Initialize(List<Type> systems)
    {
        // Workaround for initialization being called multiple times when using game object conversion
#if !UNITY_SERVER
        if (clientWorld != null)
            return systems;
#endif
#if !UNITY_CLIENT
        if (serverWorld != null)
            return systems;
#endif

#if !UNITY_SERVER
#if UNITY_EDITOR
        int numClientWorlds = UnityEditor.EditorPrefs.GetInt("MultiplayerPlayMode_" + UnityEngine.Application.productName + "_NumClients");
        if (numClientWorlds < 1)
            numClientWorlds = 1;
        if (numClientWorlds > 8)
            numClientWorlds = 8;
        int playModeType = UnityEditor.EditorPrefs.GetInt("MultiplayerPlayMode_" + UnityEngine.Application.productName + "_Type");
#else
        int numClientWorlds = 1;
#endif
#endif

        var defaultBootstrap = new List<Type>();
#if !UNITY_SERVER
        clientWorld = null;
        ClientSimulationSystemGroup[] clientSimulationSystemGroup = null;
        ClientPresentationSystemGroup[] clientPresentationSystemGroup = null;
#if UNITY_EDITOR
        if (playModeType != 2)
#endif
        {
            clientWorld = new World[numClientWorlds];
            clientSimulationSystemGroup = new ClientSimulationSystemGroup[clientWorld.Length];
            clientPresentationSystemGroup = new ClientPresentationSystemGroup[clientWorld.Length];
            for (int i = 0; i < clientWorld.Length; ++i)
            {
                clientWorld[i] = new World("ClientWorld" + i);
                clientSimulationSystemGroup[i] = clientWorld[i].GetOrCreateManager<ClientSimulationSystemGroup>();
#if UNITY_EDITOR
                clientSimulationSystemGroup[i].ClientWorldIndex = i;
#endif
                clientPresentationSystemGroup[i] = clientWorld[i].GetOrCreateManager<ClientPresentationSystemGroup>();
            }
        }
#endif
#if !UNITY_CLIENT
        serverWorld = null;
        ServerSimulationSystemGroup serverSimulationSystemGroup = null;
#if UNITY_EDITOR
        if (playModeType != 1)
#endif
        {
            serverWorld = new World("ServerWorld");
            serverSimulationSystemGroup = serverWorld.GetOrCreateManager<ServerSimulationSystemGroup>();
        }
#endif
        foreach (var type in systems)
        {
            var groups = type.GetCustomAttributes(typeof(UpdateInGroupAttribute), true);
            if (groups.Length == 0)
            {
                defaultBootstrap.Add(type);
            }

            foreach (var grp in groups)
            {
                var group = grp as UpdateInGroupAttribute;
                if (group.GroupType == typeof(ClientAndServerSimulationSystemGroup))
                {
#if !UNITY_CLIENT
                    if (serverWorld != null)
                        serverSimulationSystemGroup.AddSystemToUpdateList(serverWorld.GetOrCreateManager(type) as ComponentSystemBase);
#endif
#if !UNITY_SERVER
                    if (clientWorld != null)
                    {
                        for (int i = 0; i < clientSimulationSystemGroup.Length; ++i)
                            clientSimulationSystemGroup[i]
                                .AddSystemToUpdateList(clientWorld[i].GetOrCreateManager(type) as ComponentSystemBase);
                    }
#endif
                }
                else if (group.GroupType == typeof(ServerSimulationSystemGroup))
                {
#if !UNITY_CLIENT
                    if (serverWorld != null)
                        serverSimulationSystemGroup.AddSystemToUpdateList(serverWorld.GetOrCreateManager(type) as ComponentSystemBase);
#endif
                }
                else if (group.GroupType == typeof(ClientSimulationSystemGroup))
                {
#if !UNITY_SERVER
                    if (clientWorld != null)
                    {
                        for (int i = 0; i < clientSimulationSystemGroup.Length; ++i)
                            clientSimulationSystemGroup[i]
                                .AddSystemToUpdateList(clientWorld[i].GetOrCreateManager(type) as ComponentSystemBase);
                    }
#endif
                }
                else if (group.GroupType == typeof(ClientPresentationSystemGroup))
                {
#if !UNITY_SERVER
                    if (clientWorld != null)
                    {
                        for (int i = 0; i < clientPresentationSystemGroup.Length; ++i)
                            clientPresentationSystemGroup[i]
                                .AddSystemToUpdateList(clientWorld[i].GetOrCreateManager(type) as ComponentSystemBase);
                    }
#endif
                }
                else
                {
                    var mask = GetTopLevelWorldMask(group.GroupType);
                    if ((mask & WorldType.DefaultWorld) != 0)
                        defaultBootstrap.Add(type);
#if !UNITY_SERVER
                    if ((mask & WorldType.ClientWorld) != 0 && clientWorld != null)
                    {
                        for (int i = 0; i < clientWorld.Length; ++i)
                        {
                            var groupSys = clientWorld[i].GetOrCreateManager(group.GroupType) as ComponentSystemGroup;
                            groupSys.AddSystemToUpdateList(clientWorld[i].GetOrCreateManager(type) as ComponentSystemBase);
                        }
                    }
#endif
#if !UNITY_CLIENT
                    if ((mask & WorldType.ServerWorld) != 0 && serverWorld != null)
                    {
                        var groupSys = serverWorld.GetOrCreateManager(group.GroupType) as ComponentSystemGroup;
                        groupSys.AddSystemToUpdateList(serverWorld.GetOrCreateManager(type) as ComponentSystemBase);
                    }
#endif
                }
            }
        }
#if !UNITY_CLIENT
        if (serverWorld != null)
        {
            serverSimulationSystemGroup.SortSystemUpdateList();
            World.Active.GetOrCreateManager<TickServerSimulationSystem>().AddSystemToUpdateList(serverSimulationSystemGroup);
        }
#endif
#if !UNITY_SERVER
        if (clientWorld != null)
        {
            for (int i = 0; i < clientWorld.Length; ++i)
            {
                clientSimulationSystemGroup[i].SortSystemUpdateList();
                clientPresentationSystemGroup[i].SortSystemUpdateList();
                World.Active.GetOrCreateManager<TickClientSimulationSystem>().AddSystemToUpdateList(clientSimulationSystemGroup[i]);
                World.Active.GetOrCreateManager<TickClientPresentationSystem>().AddSystemToUpdateList(clientPresentationSystemGroup[i]);
            }
        }
#endif
        return defaultBootstrap;
    }

    [Flags]
    enum WorldType
    {
        NoWorld = 0,
        DefaultWorld = 1,
        ClientWorld = 2,
        ServerWorld = 4
    }
    WorldType GetTopLevelWorldMask(Type type)
    {
        var groups = type.GetCustomAttributes(typeof(UpdateInGroupAttribute), true);
        if (groups.Length == 0)
        {
            if (type == typeof(ClientAndServerSimulationSystemGroup))
                return WorldType.ClientWorld | WorldType.ServerWorld;
            if (type == typeof(ServerSimulationSystemGroup))
                return WorldType.ServerWorld;
            if (type == typeof(ClientSimulationSystemGroup) ||
                type == typeof(ClientPresentationSystemGroup))
                return WorldType.ClientWorld;
            return WorldType.DefaultWorld;
        }

        WorldType mask = WorldType.NoWorld;
        foreach (var grp in groups)
        {
            var group = grp as UpdateInGroupAttribute;
            mask |= GetTopLevelWorldMask(group.GroupType);
        }

        return mask;
    }

#if !UNITY_SERVER
    public static World[] clientWorld;
#endif
#if !UNITY_CLIENT
    public static World serverWorld;
#endif
}


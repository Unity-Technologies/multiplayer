using System;
using NUnit.Framework.Constraints;
using Unity.Collections;
using Unity.Entities;
using Unity.Networking.Transport;
using UnityEditor;
using UnityEngine;

public class MultiplayerPlayModeWindow : EditorWindow
{
    const string k_PrefsKeyPrefix = "MultiplayerPlayMode";
    [MenuItem("Multiplayer/PlayMode Tools")]
    public static void ShowWindow()
    {
        GetWindow<MultiplayerPlayModeWindow>(false, "Multiplayer PlayMode Tools", true);
    }

    private void OnGUI()
    {
        var playModeType = EditorPopup("PlayMode Type", new[] {"Client & Server", "Client", "Server"}, "Type");
        if (playModeType != 2)
        {
            var numClients = EditorInt("Num Clients", "NumClients", 1, 8);
            EditorInt("Client send/recv delay (ms)", "ClientDelay", 0);
            EditorInt("Client packet drop (percentage)", "ClientDropRate", 0, 100);
        }

        if (EditorApplication.isPlaying && ClientServerBootstrap.clientWorld != null)
        {
            EditorGUILayout.LabelField("Controls", EditorStyles.boldLabel);
            for (int i = 0; i < ClientServerBootstrap.clientWorld.Length; ++i)
            {
                EditorGUILayout.LabelField("Client World " + i);
                if (EditorGUILayout.Toggle("Present", MultiplayerPlayModeControllerSystem.PresentedClient == i))
                    MultiplayerPlayModeControllerSystem.PresentedClient = i;

                var conSystem = ClientServerBootstrap.clientWorld[i]
                    .GetExistingManager<MultiplayerPlayModeConnectionSystem>();
                if (conSystem != null)
                {
                    if (conSystem.ClientConnectionState ==
                        MultiplayerPlayModeConnectionSystem.ConnectionState.Connected)
                    {
                        EditorGUILayout.BeginHorizontal();
                        if (GUILayout.Button("Disconnect"))
                            conSystem.ClientConnectionState =
                                MultiplayerPlayModeConnectionSystem.ConnectionState.TriggerDisconnect;
                        //if (GUILayout.Button("Timeout"))
                        //    conSystem.ClientConnectionState =
                        //        MultiplayerPlayModeConnectionSystem.ConnectionState.TriggerTimeout;
                        EditorGUILayout.EndHorizontal();
                    }
                    else if (conSystem.ClientConnectionState ==
                             MultiplayerPlayModeConnectionSystem.ConnectionState.NotConnected)
                    {
                        if (GUILayout.Button("Connect"))
                            conSystem.ClientConnectionState =
                                MultiplayerPlayModeConnectionSystem.ConnectionState.TriggerConnect;
                    }
                }
            }
        }
    }

    static string GetKey(string subKey)
    {
        return k_PrefsKeyPrefix + "_" + Application.productName + "_" + subKey;
    }
    int EditorPopup(string label, string[] list, string key = null)
    {
        string prefsKey = (string.IsNullOrEmpty(key) ? GetKey(label) : GetKey(key));
        int index = EditorPrefs.GetInt(prefsKey);
        index = EditorGUILayout.Popup(label, index, list);
        EditorPrefs.SetInt(prefsKey, index);
        return index;
    }
    int EditorInt(string label, string key = null, int minValue = Int32.MinValue, int maxValue = Int32.MaxValue)
    {
        string prefsKey = (string.IsNullOrEmpty(key) ? GetKey(label) : GetKey(key));
        int value = EditorPrefs.GetInt(prefsKey);
        if (value < minValue)
            value = minValue;
        if (value > maxValue)
            value = maxValue;
        value = EditorGUILayout.IntField(label, value);
        if (value < minValue)
            value = minValue;
        if (value > maxValue)
            value = maxValue;
        EditorPrefs.SetInt(prefsKey, value);
        return value;
    }
}

[UpdateInGroup(typeof(ClientSimulationSystemGroup))]
[UpdateBefore(typeof(NetworkStreamReceiveSystem))]
[AlwaysUpdateSystem]
public class MultiplayerPlayModeConnectionSystem : ComponentSystem
{
    public enum ConnectionState
    {
        Uninitialized,
        NotConnected,
        Connected,
        TriggerDisconnect,
        TriggerTimeout,
        TriggerConnect
    }

    public ConnectionState ClientConnectionState;
    private ComponentGroup m_clientConnectionGroup;
    private NetworkEndPoint m_prevEndPoint;

    protected override void OnCreateManager()
    {
        m_clientConnectionGroup = GetComponentGroup(
            ComponentType.ReadWrite<NetworkStreamConnection>(),
            ComponentType.Exclude<NetworkStreamDisconnected>());
        ClientConnectionState = ConnectionState.Uninitialized;
    }

    protected override void OnUpdate()
    {
        bool isConnected = !m_clientConnectionGroup.IsEmptyIgnoreFilter;
        // Trigger connect / disconnect events
        if (ClientConnectionState == ConnectionState.TriggerDisconnect && isConnected)
        {
            var con = m_clientConnectionGroup.ToComponentDataArray<NetworkStreamConnection>(Allocator.TempJob);
            m_prevEndPoint = World.GetExistingManager<NetworkStreamReceiveSystem>().Driver.RemoteEndPoint(con[0].Value);
            for (int i = 0; i < con.Length; ++i)
            {
                World.GetExistingManager<NetworkStreamReceiveSystem>().Driver.Disconnect(con[i].Value);
            }

            con.Dispose();
            EntityManager.AddComponent(m_clientConnectionGroup, ComponentType.ReadWrite<NetworkStreamDisconnected>());
        }
        /*else if (ClientConnectionState == ConnectionState.TriggerTimeout && isConnected)
        {
            EntityManager.AddComponent(m_clientConnectionGroup, ComponentType.ReadWrite<NetworkStreamDisconnected>());
        }*/
        else if (ClientConnectionState == ConnectionState.TriggerConnect && !isConnected && m_prevEndPoint.IsValid)
        {
            World.GetExistingManager<NetworkStreamReceiveSystem>().Connect(m_prevEndPoint);
        }
        // Update connection status
        ClientConnectionState = isConnected ? ConnectionState.Connected : (m_prevEndPoint.IsValid ? ConnectionState.NotConnected : ConnectionState.Uninitialized);
    }
}

[UpdateBefore(typeof(TickClientSimulationSystem))]
[UpdateBefore(typeof(TickServerSimulationSystem))]
public class MultiplayerPlayModeControllerSystem : ComponentSystem
{
    public static int PresentedClient;
    private int m_currentPresentedClient;
    protected override void OnCreateManager()
    {
        PresentedClient = 0;
        m_currentPresentedClient = 0;

        if (ClientServerBootstrap.clientWorld != null)
        {
            for (int i = 1; i < ClientServerBootstrap.clientWorld.Length; ++i)
            {
                ClientServerBootstrap.clientWorld[i].GetExistingManager<ClientPresentationSystemGroup>().Enabled = false;
            }
        }
    }

    protected override void OnUpdate()
    {
        if (PresentedClient != m_currentPresentedClient)
        {
            // Change active client for presentation
            ClientServerBootstrap.clientWorld[m_currentPresentedClient].GetExistingManager<ClientPresentationSystemGroup>().Enabled = false;
            ClientServerBootstrap.clientWorld[PresentedClient].GetExistingManager<ClientPresentationSystemGroup>().Enabled = true;
            m_currentPresentedClient = PresentedClient;
        }
    }
}

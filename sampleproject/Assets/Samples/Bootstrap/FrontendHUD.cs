using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Entities;
using Unity.NetCode;
using System.Collections.Generic;
public class FrontendHUD : MonoBehaviour
{
    public string ConnectionStatus
    {
        get
        {
            return m_ConnectionLabel.text;
        }
        set
        {
            m_ConnectionLabel.text = value;
        }
    }
    public UnityEngine.UI.Text m_ConnectionLabel;
    public void ReturnToFrontend()
    {
        var clientServerWorlds = new List<World>();
        foreach (var world in World.All)
        {
            if (world.GetExistingSystem<ClientSimulationSystemGroup>() != null || world.GetExistingSystem<ServerSimulationSystemGroup>() != null)
                clientServerWorlds.Add(world);
        }
        foreach (var world in clientServerWorlds)
            world.Dispose();
        SceneManager.LoadScene("Frontend");
    }
    public void Start()
    {
        foreach (var world in World.All)
        {
            var simGroup = world.GetExistingSystem<ClientSimulationSystemGroup>();
            if (simGroup != null)
            {
                var sys = world.GetOrCreateSystem<FrontendHUDSystem>();
                sys.UIBehaviour = this;
                simGroup.AddSystemToUpdateList(sys);
            }
        }
    }
}

[DisableAutoCreation]
[AlwaysUpdateSystem]
public partial class FrontendHUDSystem : SystemBase
{
    public FrontendHUD UIBehaviour;
    protected override void OnUpdate()
    {
        if (HasSingleton<ThinClientComponent>())
        {
            Enabled = false;
            return;
        }
        if (!TryGetSingletonEntity<NetworkStreamConnection>(out var connectionEntity))
            UIBehaviour.ConnectionStatus = "Not connected";
        else if (!EntityManager.HasComponent<NetworkIdComponent>(connectionEntity))
        {
            var recvSystem = World.GetExistingSystem<NetworkStreamReceiveSystem>();
            var connection = EntityManager.GetComponentData<NetworkStreamConnection>(connectionEntity).Value;
            UIBehaviour.ConnectionStatus = $"Connecting to {recvSystem.Driver.RemoteEndPoint(connection).Address}";
        }
        else
            UIBehaviour.ConnectionStatus = "";
    }
}

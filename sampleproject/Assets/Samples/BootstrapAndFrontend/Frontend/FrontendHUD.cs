using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Entities;
using Unity.NetCode;
using System.Collections.Generic;
using UnityEngine.EventSystems;

public class FrontendHUD : MonoBehaviour
{
    [SerializeField]
    EventSystem m_EventSystem;

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
            if (world.IsClient() || world.IsServer())
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
            if (world.IsClient() && !world.IsThinClient())
            {
                var sys = world.GetOrCreateSystemManaged<FrontendHUDSystem>();
                sys.UIBehaviour = this;
                var simGroup = world.GetExistingSystemManaged<SimulationSystemGroup>();
                simGroup.AddSystemToUpdateList(sys);
            }
        }

        // We must always have an event system (DOTS-7177), but some scenes will already have one,
        // so we only enable ours if we can't find someone else's.
        if (FindObjectOfType<EventSystem>(false) == null)
            m_EventSystem.gameObject.SetActive(true);
    }
}

[DisableAutoCreation]
public partial class FrontendHUDSystem : SystemBase
{
    public FrontendHUD UIBehaviour;
    protected override void OnUpdate()
    {
        if (World.IsThinClient())
        {
            Enabled = false;
            return;
        }
        if (!TryGetSingletonEntity<NetworkStreamConnection>(out var connectionEntity))
            UIBehaviour.ConnectionStatus = "Not connected";
        else if (!EntityManager.HasComponent<NetworkIdComponent>(connectionEntity))
        {
            var connection = EntityManager.GetComponentData<NetworkStreamConnection>(connectionEntity);
            UIBehaviour.ConnectionStatus = $"Connecting to {GetSingletonRW<NetworkStreamDriver>().ValueRO.GetRemoteEndPoint(connection).Address}";
        }
        else
            UIBehaviour.ConnectionStatus = "";
    }
}

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Unity.NetCode;
using Unity.Entities;
using Unity.Networking.Transport;

public class Frontend : MonoBehaviour
{
    private const ushort networkPort = 7979;
    public InputField m_Address;
    public Dropdown m_Sample;
    public GameObject m_ClientServerButton;
    public Text m_BuildType;

    public void Start()
    {
        if (ClientServerBootstrap.RequestedPlayType != ClientServerBootstrap.PlayType.ClientAndServer)
        {
            m_ClientServerButton.SetActive(false);
        }
        m_BuildType.text = ClientServerBootstrap.RequestedPlayType == ClientServerBootstrap.PlayType.ClientAndServer
            ? "Client/Server build"
            : "Client build";
        PopulateSampleDropdown();
    }
    public void StartClientServer()
    {
        var server = ClientServerBootstrap.CreateServerWorld("ServerWorld");
        var client = ClientServerBootstrap.CreateClientWorld("ClientWorld");

        SceneManager.LoadScene("FrontendHUD");
        SceneManager.LoadSceneAsync(GetSceneName(), LoadSceneMode.Additive);

        NetworkEndpoint ep = NetworkEndpoint.AnyIpv4.WithPort(networkPort);
        {
            using var drvQuery = server.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<NetworkStreamDriver>());
            drvQuery.GetSingletonRW<NetworkStreamDriver>().ValueRW.Listen(ep);
        }

        ep = NetworkEndpoint.LoopbackIpv4.WithPort(networkPort);
        {
            using var drvQuery = client.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<NetworkStreamDriver>());
            drvQuery.GetSingletonRW<NetworkStreamDriver>().ValueRW.Connect(client.EntityManager, ep);
        }

    }

    private string GetSceneName()
    {
        return m_Sample.options[m_Sample.value].text;
    }

    public void ConnectToServer()
    {
        var client = ClientServerBootstrap.CreateClientWorld("ClientWorld");

        SceneManager.LoadScene("FrontendHUD");
        SceneManager.LoadSceneAsync(GetSceneName(), LoadSceneMode.Additive);

        var ep = NetworkEndpoint.Parse(m_Address.text, networkPort);
        {
            using var drvQuery = client.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<NetworkStreamDriver>());
            drvQuery.GetSingletonRW<NetworkStreamDriver>().ValueRW.Connect(client.EntityManager, ep);
        }
    }

    // Populate the scene dropdown list, and always skip frontend scene
    // since that's the one which is showing this menu and makes no sense to load.
    public void PopulateSampleDropdown()
    {
        var scenes = SceneManager.sceneCountInBuildSettings;
        m_Sample.ClearOptions();
        for (var i = 0; i < scenes; ++i)
        {
            var sceneName = System.IO.Path.GetFileNameWithoutExtension(SceneUtility.GetScenePathByBuildIndex(i));
            if (!sceneName.StartsWith("Frontend"))
                m_Sample.options.Add(new Dropdown.OptionData{text = sceneName});
        }
        m_Sample.RefreshShownValue();
    }
}

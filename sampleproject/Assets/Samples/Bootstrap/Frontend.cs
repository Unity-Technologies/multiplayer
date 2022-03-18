using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Unity.NetCode;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Networking.Transport;

public class Frontend : MonoBehaviour
{
    private const ushort networkPort = 7979;
    public InputField m_Address;
    public Dropdown m_Sample;
    public void Start()
    {
        var scenes = SceneManager.sceneCountInBuildSettings;
        for (var i = 0; i < scenes; ++i)
        {
            //var scene = SceneManager.GetSceneByBuildIndex(i);
            var sceneName = System.IO.Path.GetFileNameWithoutExtension(SceneUtility.GetScenePathByBuildIndex(i));
            if (!sceneName.StartsWith("Frontend"))
                m_Sample.options.Add(new Dropdown.OptionData{text = sceneName});
        }
        //m_Sample.options.Add(new Dropdown.OptionData{text = "Asteroids"});
        m_Sample.RefreshShownValue();
    }
    public void StartClientServer()
    {
        RpcSystem.DynamicAssemblyList = true;
        var server = ClientServerBootstrap.CreateServerWorld(World.DefaultGameObjectInjectionWorld, "ServerWorld");
        var client = ClientServerBootstrap.CreateClientWorld(World.DefaultGameObjectInjectionWorld, "ClientWorld");
        RpcSystem.DynamicAssemblyList = false;

        SceneManager.LoadScene("FrontendHUD");
        SceneManager.LoadSceneAsync(m_Sample.options[m_Sample.value].text, LoadSceneMode.Additive);

        NetworkEndPoint ep = NetworkEndPoint.AnyIpv4.WithPort(networkPort);
        server.GetExistingSystem<NetworkStreamReceiveSystem>().Listen(ep);

        ep = NetworkEndPoint.LoopbackIpv4.WithPort(networkPort);
        client.GetExistingSystem<NetworkStreamReceiveSystem>().Connect(ep);

    }

    public void ConnectToServer()
    {
        RpcSystem.DynamicAssemblyList = true;
        var client = ClientServerBootstrap.CreateClientWorld(World.DefaultGameObjectInjectionWorld, "ClientWorld");
        RpcSystem.DynamicAssemblyList = false;

        SceneManager.LoadScene("FrontendHUD");
        SceneManager.LoadSceneAsync(m_Sample.options[m_Sample.value].text, LoadSceneMode.Additive);

        var ep = NetworkEndPoint.Parse(m_Address.text, networkPort);
        client.GetExistingSystem<NetworkStreamReceiveSystem>().Connect(ep);
    }
}

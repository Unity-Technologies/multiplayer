using Unity.Entities;
using Unity.Networking.Transport;

#if !UNITY_CLIENT || UNITY_SERVER || UNITY_EDITOR
[UpdateBefore(typeof(TickServerSimulationSystem))]
#endif
#if !UNITY_SERVER
[UpdateBefore(typeof(TickClientSimulationSystem))]
#endif
[NotClientServerSystem]
public class AsteroidsClientServerControlSystem : ComponentSystem
{
    private const ushort networkPort = 50001;
    private bool m_initializeClientServer;

    protected override void OnCreateManager()
    {
        var initEntity = EntityManager.CreateEntity();
        var group = GetEntityQuery(ComponentType.ReadWrite<GameMainComponent>());
        RequireForUpdate(group);
        m_initializeClientServer = true;
    }

    protected override void OnUpdate()
    {
        if (!m_initializeClientServer)
            return;
        m_initializeClientServer = false;
        // Bind the server and start listening for connections
#if !UNITY_CLIENT || UNITY_SERVER || UNITY_EDITOR
        var serverWorld = ClientServerBootstrap.serverWorld;
        if (serverWorld != null)
        {
            var entityManager = serverWorld.EntityManager;
            var settings = entityManager.CreateEntity();
            var settingsData = GetSingleton<ServerSettings>();
            settingsData.InitArchetypes(entityManager);
            entityManager.AddComponentData(settings, settingsData);
            NetworkEndPoint ep = NetworkEndPoint.AnyIpv4;
            ep.Port = networkPort;
            serverWorld.GetExistingSystem<NetworkStreamReceiveSystem>().Listen(ep);
        }
#endif
#if !UNITY_SERVER
        // Auto connect all clients to the server
        if (ClientServerBootstrap.clientWorld != null)
        {
            for (int i = 0; i < ClientServerBootstrap.clientWorld.Length; ++i)
            {
                var clientWorld = ClientServerBootstrap.clientWorld[i];
                var entityManager = clientWorld.EntityManager;
                var settings = new ClientSettings(entityManager);
                var settingsEnt = entityManager.CreateEntity();
                entityManager.AddComponentData(settingsEnt, settings);

                NetworkEndPoint ep = NetworkEndPoint.LoopbackIpv4;
                ep.Port = networkPort;
                clientWorld.GetExistingSystem<NetworkStreamReceiveSystem>().Connect(ep);
            }
        }
#endif
    }
}

public struct GameMainComponent : IComponentData
{
}

public class GameMain : UnityEngine.MonoBehaviour, IConvertGameObjectToEntity
{
    public float asteroidRadius = 15f;
    public float playerRadius = 10f;
    public float bulletRadius = 1f;

    public float asteroidVelocity = 10f;
    public float playerForce = 50f;
    public float bulletVelocity = 500f;

    public int numAsteroids = 200;
    public int levelWidth = 2048;
    public int levelHeight = 2048;
    public int damageShips = 1;
    public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
    {
        var data = new GameMainComponent();
        dstManager.AddComponentData(entity, data);
#if !UNITY_CLIENT || UNITY_SERVER || UNITY_EDITOR
        var settings = default(ServerSettings);
        settings.asteroidRadius = asteroidRadius;
        settings.playerRadius = playerRadius;
        settings.bulletRadius = bulletRadius;

        settings.asteroidVelocity = asteroidVelocity;
        settings.playerForce = playerForce;
        settings.bulletVelocity = bulletVelocity;

        settings.numAsteroids = numAsteroids;
        settings.levelWidth = levelWidth;
        settings.levelHeight = levelHeight;
        settings.damageShips = damageShips;
        dstManager.AddComponentData(entity, settings);
#endif
    }
}

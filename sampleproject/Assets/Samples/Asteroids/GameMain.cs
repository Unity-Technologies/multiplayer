using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Networking.Transport;
using Unity.NetCode;
#if UNITY_EDITOR
using Unity.NetCode.Editor;
#endif

#if !UNITY_CLIENT || UNITY_SERVER || UNITY_EDITOR
[UpdateBefore(typeof(TickServerSimulationSystem))]
#endif
#if !UNITY_SERVER
[UpdateBefore(typeof(TickClientSimulationSystem))]
#endif
[UpdateInWorld(UpdateInWorld.TargetWorld.Default)]
public class AsteroidsClientServerControlSystem : ComponentSystem
{
    private const ushort networkPort = 50001;

    private struct InitializeClientServer : IComponentData
    {
    }

    protected override void OnCreate()
    {
        RequireSingletonForUpdate<InitializeClientServer>();
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "Asteroids")
            return;
        var initEntity = EntityManager.CreateEntity(typeof(InitializeClientServer));
    }

    protected override void OnUpdate()
    {
        EntityManager.DestroyEntity(GetSingletonEntity<InitializeClientServer>());
        foreach (var world in World.AllWorlds)
        {
#if !UNITY_CLIENT || UNITY_SERVER || UNITY_EDITOR
            // Bind the server and start listening for connections
            if (world.GetExistingSystem<ServerSimulationSystemGroup>() != null)
            {
                var entityManager = world.EntityManager;
                var settings = entityManager.CreateEntity();
                var settingsData = GetSingleton<ServerSettings>();
                entityManager.AddComponentData(settings, settingsData);

                var grid = entityManager.CreateEntity();
                entityManager.AddComponentData(grid, new GhostDistanceImportance
                {
                    ScaleImportanceByDistance = new PortableFunctionPointer<GhostDistanceImportance.ScaleImportanceByDistanceDelegate>(GhostDistanceImportance.DefaultScale),
                    TileSize = new int3(256, 256, 256),
                    TileCenter = new int3(0, 0, 128),
                    TileBorderWidth = new float3(1f, 1f, 1f)
                });
                NetworkEndPoint ep = NetworkEndPoint.AnyIpv4;
                ep.Port = networkPort;
                world.GetExistingSystem<NetworkStreamReceiveSystem>().Listen(ep);
            }
#endif
#if !UNITY_SERVER
            // Auto connect all clients to the server
            if (world.GetExistingSystem<ClientSimulationSystemGroup>() != null)
            {
                // Enable fixed tick rate
                world.EntityManager.CreateEntity(typeof(FixedClientTickRate));
                NetworkEndPoint ep = NetworkEndPoint.LoopbackIpv4;
                ep.Port = networkPort;
                world.GetExistingSystem<NetworkStreamReceiveSystem>().Connect(ep);
            }
#endif
        }
    }
}

public class GameMain : UnityEngine.MonoBehaviour, IConvertGameObjectToEntity
{
    public float asteroidVelocity = 10f;
    public float playerForce = 50f;
    public float bulletVelocity = 500f;

    public int numAsteroids = 200;
    public int levelWidth = 2048;
    public int levelHeight = 2048;
    public int damageShips = 1;
    public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
    {
#if !UNITY_CLIENT || UNITY_SERVER || UNITY_EDITOR
        var settings = default(ServerSettings);

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

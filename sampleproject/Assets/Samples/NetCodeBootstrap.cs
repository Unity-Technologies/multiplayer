using Unity.Entities;
using Unity.NetCode;
#if UNITY_EDITOR
using Unity.NetCode.Editor;
#endif

public class NetCodeBootstrap : ClientServerBootstrap
{
    public override bool Initialize(string defaultWorldName)
    {
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Asteroids" ||
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "NetCube")
            return base.Initialize(defaultWorldName);

        var systems = DefaultWorldInitialization.GetAllSystems(WorldSystemFilterFlags.Default);
        GenerateSystemLists(systems);

        var world = new World(defaultWorldName);
        World.DefaultGameObjectInjectionWorld = world;

        DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(world, DefaultWorldSystems);
        ScriptBehaviourUpdateOrder.UpdatePlayerLoop(world);
        return true;
    }

#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    public static void SetupGhostDefaults()
    {
        GhostAuthoringComponentEditor.DefaultRootPath = "/Samples/Asteroids";
        GhostAuthoringComponentEditor.DefaultSerializerPrefix = "Server/Generated/";
        GhostAuthoringComponentEditor.DefaultSnapshotDataPrefix = "Mixed/Generated/";
        GhostAuthoringComponentEditor.DefaultUpdateSystemPrefix = "Client/Generated/";
    }
#endif
}

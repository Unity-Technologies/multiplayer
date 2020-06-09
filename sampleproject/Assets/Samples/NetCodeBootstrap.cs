using Unity.Entities;
using Unity.NetCode;
#if UNITY_EDITOR
using Unity.NetCode.Editor;
#endif

public class NetCodeBootstrap : ClientServerBootstrap
{
    public override bool Initialize(string defaultWorldName)
    {
        var sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (sceneName == "Asteroids" ||
            sceneName == "NetCube" ||
            sceneName == "LagCompensation" ||
            sceneName.StartsWith("BasicPrespawnTest") ||
            sceneName.StartsWith("Test"))
            return base.Initialize(defaultWorldName);

        var world = new World(defaultWorldName);
        World.DefaultGameObjectInjectionWorld = world;

        var systems = DefaultWorldInitialization.GetAllSystems(WorldSystemFilterFlags.Default);
        GenerateSystemLists(systems);

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

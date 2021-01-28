using Unity.Entities;
using Unity.NetCode;

public class NetCodeBootstrap : ClientServerBootstrap
{
    public override bool Initialize(string defaultWorldName)
    {
        var sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        bool isSampleScene = (sceneName == "Asteroids" || sceneName == "NetCube" || sceneName == "LagCompensation");
        bool isTestScene = (sceneName.StartsWith("BasicPrespawnTest") || sceneName.StartsWith("Test"));
        if (isSampleScene || isTestScene)
        {
            // For the sample scenes we use a dynamic assembly list so we can build a server with a subset of the assemblies
            // (only including one of the samples instead of all)
            RpcSystem.DynamicAssemblyList = isSampleScene;
            var success = base.Initialize(defaultWorldName);
            RpcSystem.DynamicAssemblyList = false;
            return success;
        }

        var world = new World(defaultWorldName, WorldFlags.Game);
        World.DefaultGameObjectInjectionWorld = world;

        var systems = DefaultWorldInitialization.GetAllSystems(WorldSystemFilterFlags.Default);
        GenerateSystemLists(systems);

        DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(world, DefaultWorldSystems);
#if !UNITY_DOTSRUNTIME
        ScriptBehaviourUpdateOrder.AddWorldToCurrentPlayerLoop(world);
#endif
        return true;
    }
}

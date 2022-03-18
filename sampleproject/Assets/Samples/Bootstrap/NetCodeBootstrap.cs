using Unity.Entities;

namespace Unity.NetCode.Samples
{
    // This is a setup for dealing with a frontend menu which manually creates client and server worlds,
    // while still allowing play mode on a single level with auto connect.
    // If you do not need a frontend menu and just want to always auto connect it is usually enough to use
    // a simpler bootstrap like this:
    // [UnityEngine.Scripting.Preserve]
    // public class NetCodeBootstrap : ClientServerBootstrap
    // {
    //     public override bool Initialize(string defaultWorldName)
    //     {
    //         AutoConnectPort = 7979; // Enable auto connect
    //         return base.Initialize(defaultWorldName); // Use the regular bootstrap
    //     }
    // }

    // The preserve attibute is required to make sure the bootstrap is not stripped in il2cpp builds with stripping enabled
    [UnityEngine.Scripting.Preserve]
    // The bootstrap needs to extend `ClientServerBootstrap`, there can only be one class extending it in the project
    public class NetCodeBootstrap : ClientServerBootstrap
    {
        // The initialize method is what entities calls to create the default worlds
        public override bool Initialize(string defaultWorldName)
        {
#if UNITY_EDITOR
            // If we are in the editor, we check if the loaded scene is "Frontend",
            // if we are in a player we assume it is in the frontend if FRONTEND_PLAYER_BUILD
            // is set, otherwise we assume it is a single level.
            // The define FRONTEND_PLAYER_BUILD needs to be set in the build config for the frontend player.
            var sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            bool isFrontend = sceneName == "Frontend";
#elif !FRONTEND_PLAYER_BUILD
            bool isFrontend = false;
#endif

            // We use a dynamic assembly list so we can build a server with a subset of the assemblies
            // (only including one of the samples instead of all).
            // If you only have a single game in hte project you generally do not need to enable DynamicAssemblyList
            RpcSystem.DynamicAssemblyList = true;
            // Start by creating the default world with the default name. This world will be populated with all
            // systems marked as explicitly being in the default world (with [UpdateInWorld(TargetWorld.Default)])
            var world = CreateDefaultWorld(defaultWorldName);
#if UNITY_EDITOR || !FRONTEND_PLAYER_BUILD
            if (!isFrontend)
            {
                // This will enable auto connect, we only enable auto connect if we are not going through frontend.
                // The frontend will parse and validate the address beore connecting manually.
                // Using this auto connect feature will deal with the client only connect address from Multiplayer PlayMode Tools
                AutoConnectPort = 7979;
                // Create the default client and server worlds, depending on build type in a player or the Multiplayer PlayMode Tools in the editor
                CreateDefaultClientServerWorlds(world);
            }
#endif
            RpcSystem.DynamicAssemblyList = false;
            return true;
        }
    }
}

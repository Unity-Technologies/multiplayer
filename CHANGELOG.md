# 2019-07-17
## New features
* Added a prefab based workflow for specifying ghosts. A prefab can contain a `GhostAuthoringComponent` which is used to generate code for a ghost. A `GhostPrefabAuthoringComponent` can be used to instantiate the prefab when spawning ghosts on the client. This replaces the .ghost files, all projects need to be updated to the new ghost definitions.
* Added `ConvertToClientServerEntity` which can be used instead of `ConvertToEntity` to target the client / server worlds in the conversion workflow.
* Added a `ClientServerSubScene` component which can be used together with `SubScene` to trigger sub-scene streaming in the client/ server worlds.
* Added a new *Ping-Multiplay* sample based on the *Ping* sample
    * Created to be the main sample for demonstrating Multiplay compatibility and best practices (SQP usage, IP binding, etc.)
    * Contains both client and server code.  Additional details in readme in `/Assets/Samples/Ping-Multiplay/`.
* **DedicatedServerConfig**: added arguments for `-fps` and `-timeout`
* **NetworkEndPoint**: Added a `TryParse()` method which returns false if parsing fails
    * Note: The `Parse()` method returns a default IP / Endpoint if parsing fails, but a method that could report failure was needed for the Multiplay sample
* **CommandLine**:
    * Added a `HasArgument()` method which returns true if an argument is present
    * Added a `PrintArgsToLog()` method which is a simple way to print launch args to logs
    * Added a `TryUpdateVariableWithArgValue()` method which updates a ref var only if an arg was found and successfully parsed

## Changes
* Changed the default behavior for systems in the default groups to be included in the client and server worlds unless they are marked with `[NotClientServerSystem]`. This makes built-in systems work in multiplayer projects.
* Deleted existing SQP code and added reference to SQP Package (now in staging)
* Removed SQP server usage from basic *Ping* sample
    * Note: The SQP server was only needed for Multiplay compatibility, so the addition of *Ping-Multiplay* allowed us to remove SQP from *Ping*
* Made standalone player use the same network simulator settings as the editor when running a development player
* Made the Server Build option (UNITY_SERVER define) properly set up the right worlds for a dedicated server setup. Setting UNITY_CLIENT in the player settings define results in a client only build being made.
* Debugger now shows all running servers and clients.

## Fixes
* Change `World.Active` to the executing world when updating systems.
* Improve time calculations between client and server.
* **DedicatedServerConfig**: Vsync is now disabled programmatically if requesting an FPS different from the current screen refresh rate
## Upgrade guide
All ghost definitions specified in .ghost files needs to be converted to prefabs. Create a prefab containing a `GhostAuthoringComponent` and authoring components for all required components. Use the `GhostAuthoringComponent` to update the component list and generate code.

# 2019-06-05
## New features
* Added support systems for prediction and spawn prediction in the NetCode. These can be used to implement client-side prediction for networked objects.
* Added some support for generating the code required for replicated objects in the NetCode.
* Generalized input handling in the NetCode.
* New fixed timestep code custom for multiplayer worlds.
## Changes
* Split the NetCode into a separate assembly and improved the folder structure to make it easier to use it in other projects.
* Split the Asteroids sample into separate assemblies for client, server and mixed so it is easier to build dedicated servers without any client-side code.
* Moved MatchMaking to a package and supporting code to a separate folder.
* Upgraded Entities to preview 33.
## Fixes
* Fixed an issue with the reliable pipeline not resending when completely idle.
## Upgrade guide

# 2019-04-16
## New features
* Added network pipelines to enable processing of outgoing and incomming packets. The available pipeline stages are `ReliableSequencedPipelineStage` for reliable UDP messages and `SimulatorPipelineStage` for emulating network conditions such as high latency and packet loss. See [the pipeline documentation](com.unity.transport/Documentation~/pipelines-usage.md) for more information.
* Added reading and writing of packed signed and unsigned integers to `DataStream`. These new methods use huffman encoding to reduce the size of transfered data for small numbers.
* Added a new sample asteroids game which we will be using to develop the new netcode.
## Changes
* Update to Unity.Entities preview 26
* Enable Burst compilation for most jobs
* Made it possible to get the remote endpoint for a connection
* Replacing EndPoint parsing with custom code to avoid having a dependency on System.Net
* Change the ping sample command-line parameters for server to -port and -query_port
* For matchmaking - use an Assignment object containing the ConnectionString, the Roster, and an AssignmentError string instead of just the ConnectionString.
## Fixes
* Fixed an issue with building iOS on Windows
* Fixed inconsistent error handling between platforms when the network buffer is full
## Upgrade guide
Unity 2019.1 is now required.

`BasicNetworkDriver` has been renamed to `GenericNetworkDriver` and a new `UdpNetworkDriver` helper class is also available.

System.Net EndPoints can no longer be used as addresses, use the new NetworkEndpoint struct instead.
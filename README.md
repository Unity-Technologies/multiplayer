# Welcome

Welcome to the Unity Netcode for Entities samples repository!

Here you can find all the resources you need to start prototyping
your own real-time multiplayer games.

[Netcode for Entities Manual](https://docs.unity3d.com/Packages/com.unity.netcode@latest)

[Click here to visit the forum](https://forum.unity.com/forums/dots-netcode.425/)

### Unity Netcode for Entities Package
The netcode for entities package provides the multiplayer features needed to implement
world synchronization in a multiplayer game. It uses the transport package
for the socket level functionality, Unity Physics for networked physics simulation, Logging package for packet dump logs and is made for the [Entity Component System](https://docs.unity3d.com/Packages/com.unity.entities@latest).
Some higher level things it provides are

* Server authoritative synchronization model.
* RPC support, useful for control flow or network events.
* Client / server world bootstrapping so you have clear separation of logic and you can run a server with multiple clients in a single process, like the editor when testing.
* Synchronize entities with interpolation and client side prediction working by default.
* Network traffic debugging tools.
* GameObject conversion flow support, so you can use a hybrid model to add multiplayer to a GameObject/MonoBehaviour based project.

For more information about the netcode package, please see the [Netcode for Entities Documentation](https://docs.unity3d.com/Packages/com.unity.netcode@latest)

### Samples

#### Asteroids
A small game featuring the Netcode for Entities Package features.

#### LagCompensation
A sample showing a way to implement lag compensation based on Unity Physics. In a game based on Netcode for Entities the client will display an old world state, lag compensation allows the server to take this into account when performing raycasts so the player can aim at what is actually displayed on the client.

#### NetCube
A small sample featuring the Netcode for Entities Package basic features, this is the sample used in the __Getting Started__ guide in the manual.

#### PredictionSwitching
A sample using predicted physics based on Unity Physics. The sample is predicting all objects close the the player but not objects far away. The color of the spheres will change to indicate if they are predicted or interpolated.

#### PlayerList
A sample which shows how to maintain a list of connected players by using the RPC feature.

#### BootstrappingAndFontend
A sample showing how to do a frontend menu where you can launch the game in either client/server or client-only mode and select which sample/scene to load. Also shows how world bootstrapping can be configured.

## Installation

To try out samples in this repository all you need to do is open
`sampleprojects/` in Unity.

If you wish to create a new Unity project using the Netcode package that is also possible.

* Minimum supported version is Unity 2022.2.0b8 but it’s recommended to use the latest released 2022.2.0 beta version for important fixes
* Create a new **URP** Unity project
* Navigate to the **Package Manager** (Window -> Package Manager). And add the following packages using **Add package from git URL...** under the **+** menu at the top left of the Package Manager.
  * com.unity.netcode
  * com.unity.entities.graphics
* Package dependencies will automatically be pulled into the project

## Building

At the moment there are two ways of building the samples. You can use the **Build Configuration** assets or the builtin method using the **Build Settings** window. 

When using the Build Settings window the **Builtin Builds Enabled** checkbox in the Entities tab in the Editor preferences must be checked. Make sure you have the appropriate player type set int he DOTS tab in the **Player Settings**. To build the whole sample scene list (with frontend) as a client/server build select the **Client** as *Player type* and **ClientAndServer** as the *Netcode client target*. To make a client only build select both **Client** as *Player type* and *Netcode client target*. For a server only build switch to the **Dedicated Server** platform target and select **Server** as the *Player type*.
# Welcome

Welcome to the Unity Netcode for Entities samples repository!

Here you can find all the resources you need to start prototyping
your own real-time multiplayer games.

[Netcode for Entities Manual](https://docs.unity3d.com/Packages/com.unity.netcode@latest)

[Transport Manual](https://docs.unity3d.com/Packages/com.unity.transport@latest)

[Click here to visit the forum](https://forum.unity.com/forums/data-oriented-technology-stack.147/)

### Unity Transport Package
The new Unity Transport Package which will replace the UNet low-level API.
The preview of the transport package supports establishing connections and sending messages to a
remote host. It also contains utilities for serializing data streams to send
over the network.
For more information about the transport package, please see the [Unity Transport Documentation](https://docs.unity3d.com/Packages/com.unity.transport@latest)

### Unity Netcode for Entities Package
The netcode package provides the multiplayer features needed to implement
world synchronization in a multiplayer game. It uses the transport package
for the socket level functionality and is made for the [Entity Component System](https://docs.unity3d.com/Packages/com.unity.entities@latest).
Some higher level things it provides are
* Server authoritative synchronization model.
* RPC support, useful for control flow or network events.
* Client / server world bootrapping so you have clear seperation of logic and you can run a server with multiple clients in a single process, like the editor when testing.
* Synchronize entities with interpolation and client side prediction working by default.
* Network traffic debugging tools
* GameObject conversion flow support, so you can use a hybrid model to add multiplayer to a GameObject/MonoBehaviour based project.

For more information about the netcode package, please see the [Netcode for Entities Documentation](https://docs.unity3d.com/Packages/com.unity.netcode@latest)

### Samples

#### Asteroids
A small game featuring the Netcode for Entities Package features.

#### LagCompensation
A sample showing a way to implement lag compensation based on Unity Physics. In a game based on Netcode for Entities the client will display an old world state, lag compensation allows the server to take this into account when performing raycasts so the player can aim at what is actually displayed on the client.

#### NetCube
A small sample featuring the Netcode for Entities Package features. This is the code used in the [Unite presentation about NetCode](https://www.youtube.com/watch?v=P_-FoJuaYOI)

#### PredictionSwitching
A sample using predicted physics based on Unity Physics. The sample is predicting all objects close the the player but not objects far away. The color of the spheres will change to indicate if they are predicted or interpolated.

#### Fontend
A sample showing how to do a menu where you can launch the game in either client/server or client-only mode.

## Installation

To try out samples in this repository all you need to do is open
`sampleprojects/` in Unity.
If you wish to create a new Unity project using these packages that is
also possible.
* Make sure you have a supported version of Unity (2020.3)
* Create a new Unity project
* If you want to use the Netcode, add `Netcode for Entities` from the package manager.
* If you want to use the transport but not Netcode, add `Unity Transport` from the package manager.
* Package dependencies will automatically be pulled into the project

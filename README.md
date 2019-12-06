# Welcome

Welcome to the Unity Real-time Multiplayer Alpha repository!

Here you can find all the resources you need to start prototyping
your own real-time multiplayer games.

[NetCode Manual - Table Of Contents](https://docs.unity3d.com/Packages/com.unity.netcode@0.0/manual/TableOfContents.html)

[Transport Manual - Table Of Contents](https://docs.unity3d.com/Packages/com.unity.transport@0.2/manual/TableOfContents.html)

[Click here to visit the forum](https://forum.unity.com/forums/data-oriented-technology-stack.147/)

## Included content

- `sampleproject/` - Unity Project containing all the multiplayer samples.
- `sampleproject/Assets/Samples/Asteroids` - Asteroids clone demonstrating the core netcode concepts.
- `sampleproject/Assets/Samples/NetCube` - Sample showing basic netcode usage.
- `sampleproject/Assets/Samples/Ping` - Sample showing basic transport usage.
- `sampleproject/Assets/Samples/Soaker` - A soak tester for the transport, for testing typical production load.

### Unity Transport Package
The new Unity Transport Package which will replace the UNet low-level API.
The preview of the transport package supports establishing connections and sending messages to a
remote host. It also contains utilities for serializing data streams to send
over the network.
For more information about the transport package, please see the [Unity Transport Documentation](https://docs.unity3d.com/Packages/com.unity.transport@0.2)

### Unity NetCode Package
The netcode package provides the multiplayer features needed to implement
world synchronization in a multiplayer game. It uses the transport package
for the socket level functionality and is made for the [Entity Component System](https://docs.unity3d.com/Packages/com.unity.entities@0.2).
Some higher level things it provides are
* Server authoritative synchronization model.
* RPC support, useful for control flow or network events.
* Client / server world bootrapping so you have clear seperation of logic and you can run a server with multiple clients in a single process, like the editor when testing.
* Synchronize entities with interpolation and client side prediction working by default.
* Network traffic debugging tools
* GameObject conversion flow support, so you can use a hybrid model to add multiplayer to a GameObject/MonoBehaviour based project.

For more information about the netcode package, please see the [Unity NetCode Documentation](https://docs.unity3d.com/Packages/com.unity.netcode@0.0)

### Samples

#### Ping
The ping sample is a good starting point for learning about all the parts included
in the transport package. The ping client establishes a connection to the ping server,
sends a ping message and receives a pong reply. Once pong is received the client
will disconnect.
It is a simple example showing you how to use the new Unity Transport Package.
Ping consists of multiple scenes, all found in `sampleproject/Assets/Scenes/` .
- `PingMainThread.unity` - A main-thread only implementation of ping.
- `Ping.unity` - A fully jobified version of the ping client and server.
- `PingClient.unity` - The same jobified client code as `Ping.unity`, but without the server.
- `PingServer.unity` - The dedicated server version of the jobified ping. A headless (or Server Build in 2019.1) Linux 64 bit build of this scene is what should be deployed to Multiplay.
- `PingECS.unity` - An ECS version of the jobified ping sample.

#### Soaker
A stress test which will create a set number of clients and a server in the same process. Each client will send messages at the specified rate with the specified size and measure statistics.

#### Asteroids
A small game featuring the Unity NetCode Package features.

#### NetCube
A small sample featuring the Unity NetCode Package features. This is the code used in the [Unite presentation about NetCode](https://www.youtube.com/watch?v=P_-FoJuaYOI)

## Installation

To try out samples in this repository all you need to do is open
`sampleprojects/` in Unity.
If you wish to create a new Unity project using these packages that is
also possible.
* Make sure you have a supported version of Unity (2019.3 or newer)
* Create a new Unity project
* If you want to use the NetCode, add `Unity NetCode` from the package manager.
* If you want to use the transport but not NetCode, add `Unity Transport` from the package manager.
* Package dependencies will automatically be pulled into the project

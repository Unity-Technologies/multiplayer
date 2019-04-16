# Welcome

Welcome to the Unity Real-time Multiplayer Alpha repository!

Here you can find all the resources you need to start prototyping
your own real-time multiplayer games.

[Manual - Table Of Contents](com.unity.transport/Documentation/TableOfContents.md)  
  
[Click here to visit the forum](https://forum.unity.com/forums/connected-games.26/)  

## Included content

- `com.unity.transport/` - source of the Unity Transport package.
- `network.bindings/` - source of the native network bindings for the Unity Transport.
- `sampleproject/` - Unity Project containing all the `Unity.Networking.Transport` samples.
- `sampleproject/Assets/Matchmaking` - source of Unity Matchmaking.
- `sampleproject/Assets/ServerQueryProtocol` - source of Unity Server Query Protocol.

### Unity Transport Package
The new Unity Transport Package which will replace the UNet low-level API
is available in `com.unity.transport/` . This preview of the transport
package support establishing connections and sending unreliable messages to a
remote host. It also contains utilities for serializing data streams to send
over the network.
The native bindings for the transport package are available in `network.bindings/` .
You normally do not need to build the bindings yourself, but if you do
instructions are included in the installation guide.
For more information about the transport package, please see the [Unity Transport Documentation](com.unity.transport/Documentation/index.md)

### Matchmaking
The Unity matchmaking library is available in `sampleproject/Assets/Matchmaking/` .
This library will allow you to use the matchmaking service to find a server
to connect to.

### Server Query Protocol
In order to host a server runtime on Unitys Multiplay service the runtime
needs to follow the Server Query Protocol so the infrastructure can gather
information about the running process. The code required to support the
Server Query Protocol is available in `samplesproject/Assets/ServerQueryProtocol/` .

### Samples

#### Ping
The ping sample is a good starting point for learning about all the parts included
in this package. The ping client establishes a connection to the ping server,
sends a ping message and receives a pong reply. Once pong is received the client
will disconnect.
It is a simple example showing you how to use the new Unity Transport Package,
and it can also be deployed to Multiplay where it responds to the Server Query
Protocol and you can find it using our Matchmaker.
Ping consists of multiple scenes, all found in `sampleproject/Assets/Scenes/` .
- `PingMainThread.unity` - A main-thread only implementation of ping.
- `Ping.unity` - A fully jobified version of the ping client and server.
- `PingClient.unity` - The same jobified client code as `Ping.unity`, but without the server.
- `PingServer.unity` - The dedicated server version of the jobified ping. A headless (or Server Build in 2019.1) Linux 64 bit build of this scene is what should be deployed to Multiplay.
- `PingECS.unity` - An ECS version of the jobified ping sample.
#### Soaker
A stress test which will create a set number of clients and a server in the same process. Each client will send messages at the specified rate with the specified size and measure statistics.
#### Asteroids
A small game serving as the testbed for new netcode features.

## Installation

To try out samples in this repository all you need to do is open
`sampleprojects/` in Unity.
If you wish to create a new Unity project using these packages that is
also possible.
* Make sure you have a supported version of Unity (2019.1 or newer)
* Create a new Unity project
* Once the project is created then navigate in the Editor menu to: __Edit__ > __Project Settings__ > __Player__ > __Other Settings__ then set __Scripting Runtime Version__ to: __4.x equivalent__. This will cause Unity to restart.
* Copy `com.unity.transport/` from this repository to `<YourNewUnityProject>/Packages/`
* Copy `sampleproject/Assets/Matchmaking/` to somewhere inside your `Assets/` folder if you want to use matchmaking
* Copy `sampleproject/Assets/ServerQueryProtocol/` to somewhere inside your `Assets/` folder if you want to add support for the Server Query Protocol

### Building `network.bindings`

Public releases of this package contains pre-built binaries, but it is also
possible to build them yourself if you need to modify the bindings.
Source directory can be found under `network.bindings/`

#### Windows

- calling `build.bat` will build the native.bindings project and copy the
`network.binding.dll` + pdb and `network.bindings.cs` the native interface for
c#

#### Linux / MacOS / iOS

- calling `make` will build the `network.bindings` library
- calling `make install` will install the `network.bindings` library into your
  `com.unity.transport` package. On iOS it will copy the bindings source files
  to the bindings folder instead, and they'll be used directly in the generated
  XCode project.

#### Android

- only pre-built binaries are included for now.


# Workflow: Creating a minimal client and server

## Table of contents

* [Introduction](#introduction)
* [Creating a Server](#creating-a-server)
* [Creating a Client](#creating-a-client)

## Introduction

This workflow helps you create a sample project that highlights how to use the `com.unity.transport` API to:

- Configure
- Connect
- Send data
- Receive data
- Close a connection
- Disconnect
- Timeout a connection

> **Note**: This workflow covers all aspects of the Unity.Networking.Transport package. 

The goal is to make a remote `add` function. The flow will be: a client connects to the server, and sends a number, this number is then received by the server that adds another number to it and sends it back to the client. The client, upon receiving the number, disconnects and quits.

Using the `INetworkDriver` to write client and server code is pretty similar between clients and servers, there are a few subtle differences that you can see demonstrated below.

## Creating a Server

A server is an endpoint that listens for incoming connection requests and sends and receives messages.

Start by creating a C# script in the Unity Editor.

Filename: [_Assets\Scripts\ServerBehaviour.cs_](samples/serverbehaviour.cs.md)
```c#
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ServerBehaviour : MonoBehaviour {

    // Use this for initialization
    void Start () {
        
    }

    // Update is called once per frame
    void Update () {
        
    }
}
```

### Boilerplate code

As the `unity.networking.transport` package is a low level API, there is a bit of boiler plate code you might want to setup. This is an architecture design Unity chose to make sure that you always have full control. 

> **Note**: As development on the `unity.networking` package evolves, more abstractions may be created to reduce your workload on a day-to-day basis. 

The next step is to clean up the dependencies and add our boilerplate code:

**Filename**: [_Assets\Scripts\ServerBehaviour.cs_](samples/serverbehaviour.cs.md)

```c#
using System.Net;
using UnityEngine;

using Unity.Networking.Transport;
using Unity.Collections;

using UdpCNetworkDriver = Unity.Networking.Transport.BasicNetworkDriver<Unity.Networking.Transport.IPv4UDPSocket>;

...
```

#### Code walkthrough

`using System.Net;`  

This dependency is added to get easy access to the `IPEndPoint` and `IPAddress`. 

> **Note**: The networking package does not use these types internally, but it can implicitly convert them to its internal representation for ease of use.

```
using Unity.Networking.Transport; 
using Unity.Collections;
```

This code includes this package and the new `Unity.Collections` library. We will need `NativeList` for our example on the server to book-keep what active connections we have.

```
using NetworkConnection = Unity.Networking.Transport.NetworkConnection; 
using UdpCNetworkDriver = Unity.Networking.Transport.BasicNetworkDriver<Unity.Networking.Transport.IPv4UDPSocket>;
```

To reduce typing, this code sets up some type aliases.

> **Note**: Regarding the `BasicNetworkDriver<Unity.Networking.Transport.IPv4UDPSocket>;` alias: the `BasicNetworkDriver<T>` takes a `interface` to a `INetworkInterface`. This type represents the actual interface the driver should use to establish connections, send and receive data, and finally disconnect. You could opt to use the `IPCSocket` if you only wanted to use an in process interface.

### ServerBehaviour.cs

Adding the members we need the following code:

**Filename**: [_Assets\Scripts\ServerBehaviour.cs_](samples/serverbehaviour.cs.md)

```c#
using ...

public class ServerBehaviour : MonoBehaviour {

    public UdpCNetworkDriver m_Driver;
    private NativeList<NetworkConnection> m_Connections;

    void Start () {
    }
    
    void OnDestroy() {
    }

    void Update () {
    }

```

#### Code walkthrough

```
public UdpCNetworkDriver m_Driver;
private NativeList<NetworkConnection> m_Connections;
```

You need to declare a `INetworkDriver`, in this case you can use the `BasicNetworkDriver` (aliased as `UdpCNetworkDriver`). You also need to create a [NativeList](http://native-list-info) to hold our connections.

### Start method

**Filename**: [_Assets\Scripts\ServerBehaviour.cs_](samples/serverbehaviour.cs.md)

```c#
    void Start () {
        m_Driver = new UdpCNetworkDriver(new INetworkParameter[0]);
        if (m_Driver.Bind(new IPEndPoint(IPAddress.Any, 9000)) != 0)
            Debug.Log("Failed to bind to port 9000");
        else
            m_Driver.Listen();

        m_Connections = new NativeList<NetworkConnection>(16, Allocator.Persistent);
    }
```

#### Code walkthrough

The first line of code, `m_Driver = new UdpCNetworkDriver(new INetworkParameter[0]);` , just makes sure you are creating your driver without any parameters. 

```c#
if (m_Driver.Bind(new IPEndPoint(IPAddress.Any, 9000)) != 0)
            Debug.Log("Failed to bind to port 9000");
        else
            m_Driver.Listen();
```

Then we try to bind our driver to a specific network address and port, and if that does not fail, we call the `Listen` method. 

> **Important**: the call to the `Listen` method sets the `NetworkDriver` to the `Listen` state. This means that the `NetworkDriver` will now actively listen for incoming connections. 

` m_Connections = new NativeList<NetworkConnection>(16, Allocator.Persistent);`

Finally we create a `NativeList` to hold all the connections.

### OnDestroy method

Both `UdpCNetworkDriver` and `NativeList` allocate unmanaged memory and need to be disposed. To make sure this happens we can simply call the `Dispose` method when we are done with both of them.

Add the following code to the `OnDestroy` method on your [MonoBehaviour](https://docs.unity3d.com/ScriptReference/MonoBehaviour.html):

**Filename**: [_Assets\Scripts\ServerBehaviour.cs_](samples/serverbehaviour.cs.md)

```c#
    public void OnDestroy()
    {
        m_Driver.Dispose();
        m_Connections.Dispose();
    }

```

### Server Update loop

As the `unity.networking.transport` package uses the [Unity C# Job System](https://docs.unity3d.com/Manual/JobSystem.html) internally, the the `m_Driver` has a `ScheduleUpdate` method call. Inside our `Update` loop you need to make sure to call the `Complete` method on the [JobHandle](https://docs.unity3d.com/Manual/JobSystemJobDependencies.html) that is returned, in order to know when you are ready to process any updates.

```c#
    void Update () {
        
        m_Driver.ScheduleUpdate().Complete();
```
> **Note**: In this example, we are forcing a synchronization on the main thread in order to update and handle our data later in the `MonoBehaviour::Update` call. The workflow [Creating a jobified client and server](workflow-client-server-jobs.md) shows you how to use the Transport package with the C# Job System.


The first thing we want to do, after you have updated your `m_Driver`, is to handle your connections. Start by cleaning up any old stale connections from the list before processing any new ones. This cleanup ensures that, when we iterate through the list to check what new events we have gotten, we dont have any old connections laying around.

Inside the "Clean up connections" block below, we iterate through our connection list and just simply remove any stale connections.

```c#
        // Clean up connections
        for (int i = 0; i < m_Connections.Length; i++)
        {
            if (!m_Connections[i].IsCreated)
            {
                m_Connections.RemoveAtSwapBack(i);
                --i;
            }
        }
```

Under "Accept new connections" below, we add a connection while there are new connections to accept.
```c#
        // Accept new connections
        NetworkConnection c;
        while ((c = m_Driver.Accept()) != default(NetworkConnection))
        {
            m_Connections.Add(c);
            Debug.Log("Accepted a connection");
        }
```

Now we have an up-to-date connection list. You can now start querying the driver for events that might have happened since the last update.

```c#
        DataStreamReader stream;
        for (int i = 0; i < m_Connections.Length; i++)
        {
            if (!m_Connections[i].IsCreated)
                continue;
```
Begin by defining a `DataStreamReader`. This will be used in case any `Data` event was received. Then we just start looping through all our connections.

For each connection we want to call `PopEventForConnection` while there are more events still needing to get processed.

```c#
            NetworkEvent.Type cmd;
            while ((cmd = m_Driver.PopEventForConnection(m_Connections[i], out stream)) !=
                NetworkEvent.Type.Empty)
            {
```

> **Note**: There is also the `NetworkEvent.Type PopEvent(out NetworkConnection con, out DataStreamReader slice)` method call, that returns the first available event, the `NetworkConnection` that its for and possibly a `DataStreamReader`.

We are now ready to process events. Lets start with the `Data` event.

```c#
                if (cmd == NetworkEvent.Type.Data)
                {
                    var readerCtx = default(DataStreamReader.Context);
```

Inside this block, you start by defining a `readerCtx`, this is a `DataStreamReader.Context` type. This type can be seen as a set of indices into a `DataStreamReader`, to help with knowing where in the stream you are, and how much you have read.

Next, we use our context and try to read a `uint` from the stream and output what we have received:

```c#
                    uint number = stream.ReadUInt(ref readerCtx);
                    Debug.Log("Got " + number + " from the Client adding + 2 to it.");
```

When this is done we simply add two to the number we received and send it back. To send anything with the `INetworkDriver` we need a instance of a `DataStreamWriter`. A `DataStreamWriter` is a new collection that comes with the `unity.networking.transport` package. It's also a type that needs to be disposed. In this workflow, the `using` statement makes sure that you clean up after yourself.

After you have written your updated number to your stream, you call the `Send` method on the driver and off it goes:

```c#
                    number +=2;

                    using (var writer = new DataStreamWriter(4, Allocator.Temp))
                    {
                        writer.Write(number);
                        m_Driver.Send(m_Connections[i], writer);
                    }
                }
```

Finally, you need to handle the disconnect case. This is pretty straight forward, if you receive a disconnect message you need to reset that connection to a `default(NetworkConnection)`. As you might remember, the next time the `Update` loop runs you will clean up after yourself.

```c#
                else if (cmd == NetworkEvent.Type.Disconnect)
                {
                    Debug.Log("Client disconnected from server");
                    m_Connections[i] = default(NetworkConnection);
                }
            }
        }
    }

```

That's the whole server. Here is the full source code to [_ServerBehaviour.cs_](samples/serverbehaviour.cs.md).

## Creating a Client

The client code looks pretty similar to the server code at first glance, but there are a few subtle differences. This part of the workflow covers the differences between them, and not so much the similarities.

### ClientBehaviour.cs

You still define a `UdpCNetworkDriver` but instead of having a list of connections we now only have one. There is a `Done` flag to indicate when we are done, or in case you have issues with a connection, you can exit quick.

**Filename**: [_Assets\Scripts\ClientBehaviour.cs_](samples/clientbehaviour.cs.md)

```c#
using ...

public class ClientBehaviour : MonoBehaviour {

    public UdpCNetworkDriver m_Driver;
    public NetworkConnection m_Connection;
    public bool Done;
    
    void Start () { ... }
    public void OnDestroy() { ... }
    void Update() { ... }
}
```

### Creating and Connecting a Client

Start by creating a driver for the client and an address for the server.
```c#
    void Start () {
        m_Driver = new UdpCNetworkDriver(new INetworkParameter[0]);
        m_Connection = default(NetworkConnection);

        var endpoint = new IPEndPoint(IPAddress.Loopback, 9000);
        m_Connection = m_Driver.Connect(endpoint);
    }
```
Then call the `Connect` method on your driver.

Cleaning up this time is a bit easier because you don’t need a `NativeList` to hold your connections, so it simply just becomes:

```c#
    public void OnDestroy()
    {
        m_Driver.Dispose();
    }
```

### Client Update loop

You start the same way as you did in the server by calling `m_Driver.ScheduleUpdate().Complete();` and make sure that the connection worked.

```c#
    void Update()
    {
        m_Driver.ScheduleUpdate().Complete();

        if (!m_Connection.IsCreated)
        {
            if (!Done)
                Debug.Log("Something went wrong during connect");
            return;
        }
```

You should recognize the code below, but if you look closely you can see that the call to `m_Driver.PopEventForConnection` was switched out with a call to `m_Connection.PopEvent`. This is technically the same method, it just makes it a bit clearer that you are handling a single connection.

```c#
        DataStreamReader stream;
        NetworkEvent.Type cmd;
        while ((cmd = m_Connection.PopEvent(m_Driver, out stream)) != 
            NetworkEvent.Type.Empty)
        {
```

Now you encounter a new event you have not seen yet: a `NetworkEvent.Type.Connect` event.
This event tells you that you have received a `ConnectionAccept` message and you are now connected to the remote peer. 

> **Note**: In this case, the server that is listening on port `9000` on `IPAddress.Loopback` is more commonly known as `127.0.0.1`.

```
            if (cmd == NetworkEvent.Type.Connect)
            {
                Debug.Log("We are now connected to the server");
                
                var value = 1;
                using (var writer = new DataStreamWriter(4, Allocator.Temp))
                {
                    writer.Write(value);
                    m_Connection.Send(m_Driver, writer);
                }
            }
```
When you establish a connection between the client and the server, you send a number (that you want the server to increment by two). The use of the `using` pattern together with the `DataStreamWriter`, where we set `value` to one, write it into the stream, and finally send it out on the network.

When the `NetworkEvent` type is `Data`, as below, you read the `value` back that you received from the server and then call the `Disconnect` method. 

> **Note**: A good pattern is to always set your `NetworkConnection` to `default(NetworkConnection)` to avoid stale references.

```c#
            else if (cmd == NetworkEvent.Type.Data)
            {
                var readerCtx = default(DataStreamReader.Context);
                uint value = stream.ReadUInt(ref readerCtx);
                Debug.Log("Got the value = " + value + " back from the server");
                Done = true;
                m_Connection.Disconnect(m_Driver);
                m_Connection = default(NetworkConnection);
            }

```
Lastly we just want to make sure we handle the case that a server disconnects us for some reason. 

```c#

            else if (cmd == NetworkEvent.Type.Disconnect)
            {
                Debug.Log("Client got disconnected from server");
                m_Connection = default(NetworkConnection);
            }
        }
    }
```

Here is the full source code for the [_ClientBehaviour.cs_](clientbehaviour.cs.md).


## Putting it all together.

To take this for a test run, you can simply add a new empty [GameObject](https://docs.unity3d.com/ScriptReference/GameObject.html) to our **Scene**.

![GameObject Added](images/game-object.PNG)  

Add add both of our behaviours to it.  
![Inspector](images/inspector.PNG)

Now when we press __Play__ we should see five log messages show up in your __Console__ window. Similar to this:

![Console](images/console-view.PNG)



[Back to table of contents](TableOfContents.md)

# Jobyfiying our Example

We start off where we left off in the previous example [Chapter 2](manual.md), our client should look something like this  [Client Code](samples/clientbehaviour.cs.md).  A good thing to do before reading this chapter is to refresh your memory on how the [JobSystem](https://docs.unity3d.com/Manual/JobSystem.html) works.


## Creating a Jobified Client

We start off by creating a client job to handle our inputs from the network. Because we will only handle one client at a time we will use the `IJob` as our Job type.  We need to pass the driver and the connection to the job so we handle updates within the execution of the job.

```c#
struct ClientUpdateJob: IJob
{
	public UdpCNetworkDriver driver;
	public NativeArray<NetworkConnection> connection;
	public NativeArray<byte> done;
	
	public void Execute() { ... }
}
```

We need to keep in mind that the data to the ClientUpdateJob is **copied**. So if we want to use the data after the job is completed we need to have them in a shared container. This is where the [NativeContainers](https://docs.unity3d.com/Manual/JobSystemNativeContainer.html) help us out. Because we might update the `NetworkConnection`  and the `done` variables inside the job (we might receive a disconnect message). We need to make sure we can share the data between the job and the caller, *NativeContainers* can help us with that, in our case the `NativeArray` comes to our rescue. 

> One thing to keep in mind is that we can only use a native container together with [Blittable Types](https://docs.microsoft.com/en-us/dotnet/framework/interop/blittable-and-non-blittable-types). So in the case of our `bool` we chose to use a `byte` instead as its a `blittable` type.

In our execute method we now move in our code from the `Update` method we had before and we are done. We just need to change any call to `m_Connection` now to `connection[0]` to refer to the first element inside our `NativeArray` And the same goes for our `done` variable, we need to make sure we call `done[0]` when we refer to the `done` variable.

```c#
public void Execute()
{
    if (!connection[0].IsCreated)
    {
        // Remember that its not a bool anymore.
        if (done[0] != 1)
            Debug.Log("Something went wrong during connect");
        return;
    }
    DataStreamReader stream;
    NetworkEvent.Type cmd;

    while ((cmd = connection[0].PopEvent(driver, out stream)) != 
           NetworkEvent.Type.Empty)
    {
        if (cmd == NetworkEvent.Type.Connect)
        {
            Debug.Log("We are now connected to the server");

            var value = 1;
            using (var writer = new DataStreamWriter(4, Allocator.Temp))
            {
                writer.Write(value);
                connection[0].Send(driver, writer);
            }
        }
        else if (cmd == NetworkEvent.Type.Data)
        {
            var readerCtx = default(DataStreamReader.Context);
            uint value = stream.ReadUInt(ref readerCtx);
            Debug.Log("Got the value = " + value + " back from the server");
            // And finally change the `done[0]` to `1`
            done[0] = 1;
            connection[0].Disconnect(driver);
            connection[0] = default(NetworkConnection);
        }
        else if (cmd == NetworkEvent.Type.Disconnect)
        {
            Debug.Log("Client got disconnected from server");
            connection[0] = default(NetworkConnection);
        }
    }
}
```

### Updating the *MonoBehaviour*

Now when we have a job. We need to make sure that we can execute the job accordingly, to do so we need to do some tweaks in our ClientBehaviour. 


```c#
public class JobifiedClientBehaviour : MonoBehaviour {
    public UdpCNetworkDriver m_Driver;
    public NativeArray<NetworkConnection> m_Connection;
    public NativeArray<byte> m_Done;
    public JobHandle ClientJobHandle;
    
    public void OnDestroy() { ... }
    public void Start() { ... }
    public void Update() { ... }
}
```

As you can see we made both `m_Done` and `m_Connection` a `NativeArray`.  We also added a `JobHandle` so we can track our ongoing jobs.



```c#
void Start () {
    m_Driver = new UdpCNetworkDriver(new INetworkParameter[0]);
    m_Connection = new NativeArray<NetworkConnection>(1, Allocator.Persistent);
    m_Done = new NativeArray<byte>(1, Allocator.Persistent);
    
    var endpoint = new IPEndPoint(IPAddress.Loopback, 9000);
    m_Connection[0] = m_Driver.Connect(endpoint);
}
```

The `Start` method looks pretty similar to before, the major update here is to make sure we create our ``NativeArray`. 



```c#
public void OnDestroy()
{
    ClientJobHandle.Complete();
    
    m_Connection.Dispose();
    m_Driver.Dispose();
    m_Done.Dispose();
}
```

Same goes for the `OnDestroy` method, we make sure we dispose all our `NativeArray` objects and a new addition here is the `ClientJobHandle.Complete()` call. This is so we make sure to complete our jobs before cleaning up and destroying data they might be using.



Finally we update our core game loop.

```c#
void Update()
{
    ClientJobHandle.Complete();
    ...
}
```

Just as before we want to make sure that before we get started running our new frame, we sync that the last frame was finished. But instead of `m_Driver.ScheduleUpdate().Complete();` we instead use the `JobHandle` and call `ClientJobHandle.Complete()`.

Now lets chain our jobs. We start by creating a job struct.

```c#
var job = new ClientUpdateJob
{
	driver = m_Driver,
	connection = m_Connection,
	done = m_Done
};

```

 To schedule the job we need to pass the dependency `JobHandle` that was returned from the `m_Driver.ScheduleUpdate` call into the `Schedule` function of our `IJob`. We start by first invoking the `m_Driver.ScheduleUpdate` without a call to `Complete` and we pass the returning `JobHandle` to our saved `ClientJobHandle`. 

```c#
ClientJobHandle = m_Driver.ScheduleUpdate();
ClientJobHandle = job.Schedule(ClientJobHandle);
```

Then we pass the returned `ClientJobHandle` to our own job function returning a newly  updated `ClientJobHandle`. 



And that's it. We now should have a *JobifiedClientBehaviour* that looks like [this](samples/jobifiedclientbehaviour.cs.md)



## Creating a Jobified Server

We start off pretty similar on the Server side. We create jobs needed and then we update the usage code.

Before we begin let's start and think about it a little bit. We know that the `NetworkDriver` has a `ScheduleUpdate` function that returns a job handle. The job as we saw populates the internal buffers of the `NetworkDriver` and lets us call `PopEvent`/`PopEventForConnection`. So what if we create a job that will fan out and run the processing code for all connected Clients, and we do it in Parallel. Sure thing, looking at the documentation for the [JobSystem](https://docs.unity3d.com/Manual/JobSystemParallelForJobs.html), we can see that there is a `IJobParallelFor` that might do the trick so lets use that.

```c#
struct ServerUpdateJob : IJobParallelFor
{
    public void Execute(int index)
    {
        throw new System.NotImplementedException();
    }
}
```

But if we stop and think a little bit we can’t make all of our code run in parallel. In our previous example we started off by cleaning up closed connection and accepting new ones, this cant be done in parallel. So let’s do a Connection Job as well;

We start by creating a `ServerUpdateConnectionJob`, we know we will need to pass both the *driver* and the *connections* to our *ConnectionsJob* and then we want it to *CleanUpConnections* and *AcceptNewConnection*.

```c#
struct ServerUpdateConnectionsJob : IJob
{
    public UdpCNetworkDriver driver;
    public NativeList<NetworkConnection> connections;
    
    public void Execute()
    {
        // CleanUpConnections
        for (int i = 0; i < connections.Length; i++)
        {
            if (!connections[i].IsCreated)
            {
                connections.RemoveAtSwapBack(i);
                --i;
            }
        }
        // AcceptNewConnections
        NetworkConnection c;
        while ((c = driver.Accept()) != default(NetworkConnection))
        {
            connections.Add(c);
            Debug.Log("Accepted a connection");
        }
    }
}
```

The code above should be almost identical to you old non-jobified code.

With the `ServerUpdateConnectionsJob` completed lets look at how to implement the `ServerUpdateJob` using `IParallelFor`.

```c#
struct ServerUpdateJob : IJobParallelFor
{
    public UdpCNetworkDriver.Concurrent driver;
    public NativeArray<NetworkConnection> connections;

    public void Execute(int index)
    {
    	...
    }
}
```

There are **two** major differences here compared with our other job. First off we are using the `UdpCNetworkDriver.Concurrent` type, this allows us to call the `NetworkDriver` from multiple threads, precisely what we need for the `IParallelForJob`. Secondly we are now passing a `NativeArray` of `NetworkConnection` instead of a `NativeList`. This is because the `IParallelForJob` does not allow any other `Unity.Collections` type than a `NativeArray` to work on (More on this later).

Now to the body of the execute function. 

```c#
public void Execute(int index)
{
	DataStreamReader stream;
	if (!connections[index].IsCreated)
		Assert.IsTrue(true);

	NetworkEvent.Type cmd;
	while ((cmd = driver.PopEventForConnection(connections[index], out stream)) !=
	NetworkEvent.Type.Empty)
	{
		if (cmd == NetworkEvent.Type.Data)
		{
			var readerCtx = default(DataStreamReader.Context);
			uint number = stream.ReadUInt(ref readerCtx);

			Debug.Log("Got " + number + " from the Client adding + 2 to it.");
			number +=2;

			using (var writer = new DataStreamWriter(4, Allocator.Temp))
			{
				writer.Write(number);
				driver.Send(connections[index], writer);
			}
		}
		else if (cmd == NetworkEvent.Type.Disconnect)
		{
			Debug.Log("Client disconnected from server");
			connections[index] = default(NetworkConnection);
		}
	}
}
```

The only difference between our old code and our jobified example is that we removed the top level for loop we had in our code before `for (int i = 0; i < m_Connections.Length; i++)`. This is because the `Execute` function on this job will be called for each connection, and the index to that a available connection will be passed in. As you can see in the top level `while` loop.

`while ((cmd = driver.PopEventForConnection(connections[index], out stream)) !=
​	NetworkEvent.Type.Empty)` 

> **Note**: We are using the `index` that was passed into our `Execute` to iterate over the `connections`.



We now have 2 jobs.

- First Job is to Update our Connection Status.
  - Add new connections
  - Remove old / stale connections
- Second Job is to Parse `NetworkEvent` on each connected client.

With this we can now go back to our *MonoBehaviour* and start updating the Server.

```c#
public class JobifiedServerBehaviour : MonoBehaviour 
{
    public UdpCNetworkDriver m_Driver;
    public NativeList<NetworkConnection> m_Connections;
    private JobHandle ServerJobHandle;

    void Start () { ... }

    public void OnDestroy() { ... }
    
    void Update () { ... }
}
```

The only change we have made in our variable declaration is that we have once again added a `JobHandle` so we can keep track of our ongoing jobs. We do not need to change our `Start` method as it should look the same.

```c#
void Start ()
{
    m_Connections = new NativeList<NetworkConnection>(16, Allocator.Persistent);
    m_Driver = new UdpCNetworkDriver(new INetworkParameter[0]);
    
    if (m_Driver.Bind(new IPEndPoint(IPAddress.Any, 9000)) != 0)
    	Debug.Log("Failed to bind to port 9000");
    else
    	m_Driver.Listen();
}
```

But we need to remember to call `ServerJobHandle.Complete` in our `OnDestroy` method so we can properly clean up after our self.

```c#
public void OnDestroy()
{
    // Make sure we run our jobs to completion before exiting.
    ServerJobHandle.Complete();
    m_Connections.Dispose();
    m_Driver.Dispose();
}
```



In our `Update` call we start by completing the  `JobHandle`. This will force the Jobs to complete before we start a new frame of work.

```c#
void Update () 
{
	ServerJobHandle.Complete();

	var connectionJob = new ServerUpdateConnectionsJob
	{
		driver = m_Driver, 
		connections = m_Connections
	};

	var serverUpdateJob = new ServerUpdateJob
	{
		driver = m_Driver.ToConcurrent(),
		connections = m_Connections.ToDeferredJobArray()
	};
	
    ServerJobHandle = m_Driver.ScheduleUpdate();
	ServerJobHandle = connectionJob.Schedule(ServerJobHandle);
	ServerJobHandle = serverUpdateJob.Schedule(m_Connections, 1, ServerJobHandle);
}
```

Now it's just about chaining the jobs. We want to following to happen.
`NetworkDriver.Update -> ServerUpdateConnectionsJob -> ServerUpdateJob`. 

We start off populating our `ServerUpdateConnectionsJob`:

```c#
var connectionJob = new ServerUpdateConnectionsJob
{
	driver = m_Driver, 
	connections = m_Connections
};
```



Then we create our `ServerUpdateJob`, here we have to remember to use the 
`ToConcurrent()` call on our driver to make sure we are using a concurrent driver for the `IParallelForJob`.

```c#
var serverUpdateJob = new ServerUpdateJob
{
	driver = m_Driver.ToConcurrent(),
	connections = m_Connections.ToDeferredJobArray()
};
```

The final addition is to make sure that the `NativeArray` is populated to the correct size. This 
can be done using a `DeferredJobArray` basically it's just making sure that when the job gets executed the connections array is populated with the correct amount of items we have in our list. Remember we will run the `ServerUpdateConnectionsJob` first and this might change the *size* of the list.

That's it, let's create our job chain and call `Scheduele`.

```
ServerJobHandle = m_Driver.ScheduleUpdate();
ServerJobHandle = connectionJob.Schedule(ServerJobHandle);
ServerJobHandle = serverUpdateJob.Schedule(m_Connections, 1, ServerJobHandle);
```

Here we created our chain. 

- We schedule the `NetworkDriver` job.
- We add the handle we got as a dependency on the `ServerUpdateConnectionJob`.
- And the final chain is the `ServerUpdateJob` that needs to run after the `ServerUpdateConnectionsJob`. Here we do a trick to invoke the `IJobParallelForDeferExtensions` as you can see we pass our `m_Connections` `NativeList` to the `Schedule`, this makes sure to update the count of connections before starting the job. Its here we will fan out and run all the *ServerUpdateConnectionJobs* in parallel.

> **Note**: You might need to add `"com.unity.jobs": "0.0.7-preview.5"` to you `manifest.json` file, inside the `Packages/` folder. If you are having trouble with the `serverUpdateJob.Schedule(m_Connections, 1, ServerJobHandle);` call.



That's it. You should not have a fully functional [Jobified Server](samples/jobifiedserverbehaviour.cs.md).

You can download all examples from [here](https://oc.unity3d.com/index.php/s/PHaNZP79Va2YOLT).

[Back to table of contents](TableOfContents.md)
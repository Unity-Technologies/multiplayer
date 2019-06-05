# Jobyfiying our Example

In the workflow [Creating a minimal client and server](workflow-client-server.md), our client should look like this [code example](samples/clientbehaviour.cs.md).  

> **Note**: It is recommended, before reading this workflow, to refresh your memory on how the [C# Job System](https://docs.unity3d.com/Manual/JobSystem.html) works.


## Creating a Jobified Client

Start by creating a client job to handle your inputs from the network. As you only handle one client at a time we will use the [IJob](https://docs.unity3d.com/ScriptReference/Unity.Jobs.IJob.html) as our job type.  You need to pass the driver and the connection to the job so you can handle updates within the `Execute` method of the job.

```c#
struct ClientUpdateJob: IJob
{
	public UdpCNetworkDriver driver;
	public NativeArray<NetworkConnection> connection;
	public NativeArray<byte> done;
	
	public void Execute() { ... }
}
```

> **Note**: The data inside the ClientUpdateJob is **copied**. If you want to use the data after the job is completed, you need to have your data in a shared container, such as a [NativeContainer](https://docs.unity3d.com/Manual/JobSystemNativeContainer.html). 

Since you might want to update the `NetworkConnection`  and the `done` variables inside your job (we might receive a disconnect message), you need to make sure you can share the data between the job and the caller. In this case, you can use a [NativeArray](https://docs.unity3d.com/ScriptReference/Unity.Collections.NativeArray_1.html). 

> Note: You can only use [blittable types](https://docs.microsoft.com/en-us/dotnet/framework/interop/blittable-and-non-blittable-types) in a `NativeContainer`. In this case, instead of a `bool` you need  to use a `byte`, as its a blittable type.

In your `Execute` method, move over your code from the `Update` method that you have already in place from [_ClientBehaviour.cs_](samples/clientbehaviour.cs.md) and you are done. 

You need to change any call to `m_Connection` to `connection[0]` to refer to the first element inside your `NativeArray`. The same goes for your `done` variable, you need to call `done[0]` when you refer to the `done` variable. See the code below:

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

### Updating the client MonoBehaviour

When you have a job, you need to make sure that you can execute the job. To do this, you need to make some changes to your ClientBehaviour:


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

Both `m_Done` and `m_Connection` in the code above, have been changed to type `NativeArray`.  We also added a [JobHandle](https://docs.unity3d.com/Manual/JobSystemJobDependencies.html) so you can track your ongoing jobs.

#### Start method

```c#
void Start () {
    m_Driver = new UdpCNetworkDriver(new INetworkParameter[0]);
    m_Connection = new NativeArray<NetworkConnection>(1, Allocator.Persistent);
    m_Done = new NativeArray<byte>(1, Allocator.Persistent);
    
    var endpoint = new IPEndPoint(IPAddress.Loopback, 9000);
    m_Connection[0] = m_Driver.Connect(endpoint);
}
```

The `Start` method looks pretty similar to before, the major update here is to make sure you create your `NativeArray`. 

#### OnDestroy method

```c#
public void OnDestroy()
{
    ClientJobHandle.Complete();
    
    m_Connection.Dispose();
    m_Driver.Dispose();
    m_Done.Dispose();
}
```

Same goes for the `OnDestroy` method. Make sure you dispose all your `NativeArray` objects. A new addition is the `ClientJobHandle.Complete()` call. This makes sure your jobs complete before cleaning up and destroying the data they might be using.

#### Client Update loop

Finally you need to update your core game loop:

```c#
void Update()
{
    ClientJobHandle.Complete();
    ...
}
```

You want to make sure (again) that before you start running your new frame, we check that the last frame is complete. Instead of calling `m_Driver.ScheduleUpdate().Complete()`, use the `JobHandle` and call `ClientJobHandle.Complete()`.

To chain your job, start by creating a job struct:

```c#
var job = new ClientUpdateJob
{
	driver = m_Driver,
	connection = m_Connection,
	done = m_Done
};

```

 To schedule the job, you need to pass the  `JobHandle` dependency that was returned from the `m_Driver.ScheduleUpdate` call in the `Schedule` function of your `IJob`. Start by invoking the `m_Driver.ScheduleUpdate` without a call to `Complete`, and pass the returning `JobHandle` to your saved `ClientJobHandle`. 

```c#
ClientJobHandle = m_Driver.ScheduleUpdate();
ClientJobHandle = job.Schedule(ClientJobHandle);
```

As you can see in the code above, you pass the returned `ClientJobHandle` to your own job, returning a newly updated `ClientJobHandle`. 

You now have a *JobifiedClientBehaviour* that looks like [this](samples/jobifiedclientbehaviour.cs.md).



## Creating a Jobified Server

The server side is pretty similar to start with. You create the jobs you need and then you update the usage code.

Consider this: you know that the `NetworkDriver` has a `ScheduleUpdate` method that returns a `JobHandle`. The job as you saw above populates the internal buffers of the `NetworkDriver` and lets us call `PopEvent`/`PopEventForConnection` method. What if you create a job that will fan out and run the processing code for all connected clients in parallel? If you look at the documentation for the C# Job System, you can see that there is a [IJobParallelFor](https://docs.unity3d.com/Manual/JobSystemParallelForJobs.html) job type that can handle this scenario:

```c#
struct ServerUpdateJob : IJobParallelFor
{
    public void Execute(int index)
    {
        throw new System.NotImplementedException();
    }
}
```

However, we can’t run all of our code in parallel. 

In the client example above, we started off by cleaning up closed connections and accepting new ones, this can't be done in parallel. You need to create a connection job as well;

Start by creating a `ServerUpdateConnectionJob` job. You know you need to pass both the `driver` and `connections` to our connection job. Then you want your job to "Clean up connections" and "Accept new connections":

```c#
struct ServerUpdateConnectionsJob : IJob
{
    public UdpCNetworkDriver driver;
    public NativeList<NetworkConnection> connections;
    
    public void Execute()
    {
        // Clean up connections
        for (int i = 0; i < connections.Length; i++)
        {
            if (!connections[i].IsCreated)
            {
                connections.RemoveAtSwapBack(i);
                --i;
            }
        }
        // Accept new connections
        NetworkConnection c;
        while ((c = driver.Accept()) != default(NetworkConnection))
        {
            connections.Add(c);
            Debug.Log("Accepted a connection");
        }
    }
}
```

The code above should be almost identical to your old non-jobified code.

With the `ServerUpdateConnectionsJob` done, lets look at how to implement the `ServerUpdateJob` using `IJobParallelFor`.

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

There are **two** major differences here compared with our other `IParallelForJob` job. First off we are using the `UdpCNetworkDriver.Concurrent` type, this allows you to call the `NetworkDriver` from multiple threads, precisely what you need for the `IParallelForJob`. Secondly, you are now passing a `NativeArray` of type `NetworkConnection` instead of a `NativeList`. The `IParallelForJob` does not accept any other `Unity.Collections` type than a `NativeArray` (more on this later).

### Execute method

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

The only difference between our old code and our jobified example is that you remove the top level `for` loop that you had in your code: `for (int i = 0; i < m_Connections.Length; i++)`. This is removed because the `Execute` function on this job will be called for each connection, and the `index` to that a available connection will be passed in. You can see this `index` in use in the top level `while` loop:

```
while ((cmd = driver.PopEventForConnection(connections[index], out stream)) != NetworkEvent.Type.Empty`
```

> **Note**: You are using the `index` that was passed into your `Execute` method to iterate over all the `connections`.

You now have 2 jobs:

- The first job is to update your connection status.
  - Add new connections
  - Remove old / stale connections
- The second job is to parse `NetworkEvent` on each connected client.



### Updating the server MonoBehaviour

With this we can now go back to our [MonoBehaviour](https://docs.unity3d.com/ScriptReference/MonoBehaviour.html) and start updating the server.

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

The only change made in your variable declaration is that you have once again added a `JobHandle` so you can keep track of your ongoing jobs. 

#### Start method

You do not need to change your `Start` method as it should look the same:

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

#### OnDestroy method

You need to remember to call `ServerJobHandle.Complete` in your `OnDestroy` method so you can properly clean up after yourself:

```c#
public void OnDestroy()
{
    // Make sure we run our jobs to completion before exiting.
    ServerJobHandle.Complete();
    m_Connections.Dispose();
    m_Driver.Dispose();
}
```

#### Server update loop

In your `Update` method, call `Complete`on the `JobHandle`. This will force the jobs to complete before we start a new frame:

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

To chain the jobs, you want to following to happen:
`NetworkDriver.Update` -> `ServerUpdateConnectionsJob` -> `ServerUpdateJob`. 

Start by populating your `ServerUpdateConnectionsJob`:

```c#
var connectionJob = new ServerUpdateConnectionsJob
{
	driver = m_Driver, 
	connections = m_Connections
};
```

Then create your `ServerUpdateJob`. Remember to use the `ToConcurrent` call on your driver, to make sure you are using a concurrent driver for the `IParallelForJob`:

```c#
var serverUpdateJob = new ServerUpdateJob
{
	driver = m_Driver.ToConcurrent(),
	connections = m_Connections.ToDeferredJobArray()
};
```

The final step is to make sure the `NativeArray` is populated to the correct size. This 
can be done using a `DeferredJobArray`. It makes sure that, when the job is executed, that the connections array is populated with the correct number of items that you have in your list. Since we will run the `ServerUpdateConnectionsJob` first, this might change the **size** of the list.

Create your job chain and call `Scheduele` as follows:

```
ServerJobHandle = m_Driver.ScheduleUpdate();
ServerJobHandle = connectionJob.Schedule(ServerJobHandle);
ServerJobHandle = serverUpdateJob.Schedule(m_Connections, 1, ServerJobHandle);
```

In the code above, you have:

- Scheduled the `NetworkDriver` job.
- Add the `JobHandle` returned as a dependency on the `ServerUpdateConnectionJob`.
- The final link in the chain is the `ServerUpdateJob` that needs to run after the `ServerUpdateConnectionsJob`. In this line of code, there is a trick to invoke the `IJobParallelForDeferExtensions`. As you can see, `m_Connections` `NativeList` is passed to the `Schedule` method, this updates the count of connections before starting the job. It's here that it will fan out and run all the `ServerUpdateConnectionJobs` in parallel.

> **Note**: If you are having trouble with the `serverUpdateJob.Schedule(m_Connections, 1, ServerJobHandle);` call, you might need to add `"com.unity.jobs": "0.0.7-preview.5"` to your `manifest.json` file, inside the _/Packages_ folder. 



You should now have a fully functional [jobified server](samples/jobifiedserverbehaviour.cs.md).

You can download all examples from [here](https://oc.unity3d.com/index.php/s/PHaNZP79Va2YOLT).

[Back to table of contents](TableOfContents.md)
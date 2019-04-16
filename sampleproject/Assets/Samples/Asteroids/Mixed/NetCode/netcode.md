# Unity NetCode

This document is a brief description of the various parts that makes up the new netcode in Unity, it is not intended to be full documentation of how it works and what the APIs are since everything is still in development.

The new NetCode for Unity is being prototyped in a small asteroids game. We deliberately chose a very simple game for prototyping since it allows us to focus on the netcode rather than the gameplay logic.

The netcode is still in a very early stage. The focus has been on figuring out a good architecture for synchronizing entities when using ECS, there has not yet been a lot of focus on how we can make it easy to add new types of replicated entities or integrate it with the gameplay logic. These are areas we will be focusing on going forward, and areas where we want feedback.

The netcode in the asteroids sample is using a server authoratative model and is constructed from a few pieces explained below.

## Client / Server world

The first part of the netcode is a strong separation of client and server logic into separate worlds. This is based on the new hierarchical update system in ECS. The code for setting up the multiple worlds is in ClientServerWorld.cs, when using it you need to switch the UpdateGroup for your systems to one of `[UpdateInGroup(typeof(ClientSimulationSystemGroup))]`, `[UpdateInGroup(typeof(ServerSimulationSystemGroup))]`, `[UpdateInGroup(typeof(ClientAndServerSimulationSystemGroup))]` or `[UpdateInGroup(typeof(ClientPresentationSystemGroup))]`.
Using this attribute will create the system in the correct world, client, server or both and make sure it is updated in the correct pass.

In addition to the attributes there is a small inspector under `Multiplayer > PlayMode Tools` which you can use to choose what should happen when entering PlayMode, you can make PlayMode client only, server only or client with in-proc server, and you can run multiple clients in the same process when entering PlayMode - all in separate worlds. In the same inspector you can disconnect clients and decide which client should be presented if you have multiple. The switching of presented client simply stops calling update on the `ClientPresentationSystemGroup` for the worlds which are not presented, so your game code needs to be able to handle that.

### Simulation / presentation interpolation

The client presetation and separation are separate groups, and simulation needs to run at a fixed frame rate. In order to allow presentation to run at a different frame rate than simulation we have an early prototype for interpolation of transforms. The interpolation will always interpolate between two existing frames, so it does introduce one simulation tick worth of latency. The code for the interpolation is available in RenderInterpolationSystem.cs and RenderInterpolationComponents.cs .

## Network connection

The network connection uses the Unity Transport package and stores each connection as an entity. Each connection has three incoming and three outgoing buffers attached to it. In order to send a snapshot a system needs to store the snapshot data in the outgoing snapshot buffer of the connection. When a snapshot is received it is available in the incoming snapshot buffer. The same method is used for the command stream and the RPC stream. The code for receiveing data from a connection and storing it on the corresponding entity is available in NetworkStreamReceiveSystem.cs and the code for sending the data in the pending buffers is in NetworkStreamSendSystem.cs .

### RPC events

The netcode handles events by a limited form of RCP calls in RpcSystem.cs . The RPC calls can be issues from a job on the sending side, and they will execute in a job on the receiving side - which limits what you can do in an RPC.
In order to send an RPC you first need to get access to an RpcQueue for the command you want to send. This can be created in OnCreateManager - by calling `m_RpcQueue = World.GetOrCreateManager<RpcSystem>().GetRpcQueue<RpcLoadLevel>();` -  and cached through the lifetime of the game. Once you have the queue you can schedule events in it by getting the `OutgoingRpcDataStreamBufferComponent` buffer from the entity representing the connection you wish to send the event to and calling `rpcQueue.Schedule(rpcBuffer, new RpcCommand);`. That will append the correct RPC data to the outgoing RPC buffer (`OutgoingRpcDataStreamBufferComponent`) so it can be sent to the remote end by `NetworkStreamSendSystem`.
The RpcCommand interface has three methods, Serialize and Deserialize for storing the data in a packet and an Execute method which takes three parameters: `Entity connection, EntityCommandBuffer.Concurrent commandBuffer, int jobIndex` . The RPC can either modify the connection entity using the command buffer, or it can create a new request entity using the command buffer for more complex tasks, and apply the command in a separate system at a later time. This means that there is no need to do anything special to receive an RPC, it will have its `Execute` method called on the receiving end automatically.

## Command stream

The client will continously send a command stream to the server. This stream includes all inputs and acknowlegements of the last received snapshot.
The command stream is not yet generalized, but is just a hardcoded system in asteroids located in InputSystem.cs on the client and InputCommandSystem.cs on the server.

## Ghost snapshots

The ghost snapshot system is the most complex part of the netcode. It is responsible for synchonizing entities which exist on the server to all clients. In order to make it perform well the server will do all processing per ECS chunk rather than per entity. On the receiving side the processing is done per entity. The reason for this is that it is not possible to process per chunk on both sides, and the server has more connections than clients.

The way this is implemented is with a tag component on the server - GhostComponent - and a set of GhostSerializers. In order to start replicating an entity from the server you add the GhostComponent tag. When you do the system in GhostSendSystem.cs will detect it and map it to a ghost type by going through a list of ghost serializers to find the first one which can serialize that entity.

The list of serializers for asteroids is in GhostCollection.cs and the interface for a serializer is in IGhostSerializer.cs . Please note, this is not the final design on how you are supposed to write the serialization types - we will iterate on usability for it.

Once the type is mapped to a ghost type the serializer will get called to serialize the data from the entity at some time interval. The interval depends on how many entities you have and how important the ghost type is. The serializer can specify an importance factor per ECS chunk, and the importance will be scaled by the time since last serialization to ensure that no entities are starved and never update.

The server operates on a fixed bandwidth, sending a single packet with snapshot data of customizable size every frame. The packet is filled with the entities of the highest importance. Once a packet is full it is sent and remaining entities will be missing from the snapshot. Since the age influences the importance it is more likely that those entities will be included in the next snapshot.

When a new ghost is received on the client side it will be spawned by a user defined spawn system. There is no specific spawn message, receiving an unknown ghost id counts as an implicit spawn. The spawn system must add a buffer for storing snapshots to the entity. Subsequent received snapshots for the entity will add more data to this buffer and remove the oldest if the buffer is full. Finally the client will run an update system which will interpolate between the recieved snapshots according to the target interpolation time (see Time synchronization).

### Snapshot visualization

In order to reason about what is being put on the wire in the netcode we have a small prototype of a visualization tool in the Stats folder. The tool will display one vertical bar for each received snapshot with breakdown of ghost types in that snapshot. Clicking a bar will display more detailed stats about the snapshot. This tool is a prototype, in the future it will be integrated with the unity profiler to make it easier to correlate network traffic with memory usage and CPU performance.

## Time synchonization

In order to know which server tick the client should be displaying there is a small system to calculate that in NetworkTimeSystem.cs . The time system will calculate the latest server tick the client should have received based on previous data. The interpolation target is then calculated as the latest expected snapshot minus an interpolation period.

The time system also calculates at which server tick the server will process a message the client sends this frame. This time will be used as the target for client prediction.

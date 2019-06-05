# Unity NetCode

This document is a brief description of the various parts that makes up the new netcode in Unity, it is not intended to be a full documentation of how it works and what the APIs are since everything is still in development.

The new NetCode for Unity is being prototyped in a small asteroids game. We deliberately chose a very simple game for prototyping since it allows us to focus on the netcode rather than the gameplay logic.

The netcode is still in a very early stage. The focus has been on figuring out a good architecture for synchronizing entities when using ECS, there has not yet been a lot of focus on how we can make it easy to add new types of replicated entities or integrate it with the gameplay logic. These are areas we will be focusing on going forward, and areas where we want feedback.

The netcode in the asteroids sample is using a server authoritative model and is constructed from a few pieces explained below.

## Client / Server world

The first part of the netcode is a strong separation of client and server logic into separate worlds. This is based on the new hierarchical update system in ECS. The code for setting up the multiple worlds is in [ClientServerWorld.cs](ClientServerWorld.cs), when using it you need to switch the UpdateGroup for your systems to one of `[UpdateInGroup(typeof(ClientSimulationSystemGroup))]`, `[UpdateInGroup(typeof(ServerSimulationSystemGroup))]`, `[UpdateInGroup(typeof(ClientAndServerSimulationSystemGroup))]` or `[UpdateInGroup(typeof(ClientPresentationSystemGroup))]`.
Using this attribute will create the system in the correct world, client, server or both and make sure it is updated in the correct pass.

In addition to the attributes there is a small inspector under `Multiplayer > PlayMode Tools` which you can use to choose what should happen when entering PlayMode, you can make PlayMode client only, server only or client with in-proc server, and you can run multiple clients in the same process when entering PlayMode - all in separate worlds. In the same inspector you can disconnect clients and decide which client should be presented if you have multiple. The switching of presented client simply stops calling update on the `ClientPresentationSystemGroup` for the worlds which are not presented, so your game code needs to be able to handle that.

### Simulation / presentation interpolation

The client presentation and separation are separate groups, and simulation needs to run at a fixed frame rate. In order to allow presentation to run at a different frame rate than simulation we have an early prototype for interpolation of transforms. The interpolation will always interpolate between two existing frames, so it does introduce one simulation tick worth of latency. The code for the interpolation is available in [RenderInterpolationSystem.cs](RenderInterpolation/RenderInterpolationSystem.cs) and [RenderInterpolationComponents.cs](RenderInterpolation/RenderInterpolationComponents.cs).

## Network connection

The network connection uses the Unity Transport package and stores each connection as an entity. Each connection has three incoming buffers for each type of stream, command, rpc and snapshot. There is an outgoing buffer for rpcs but snapshots and commands are gathered and sent in their respective send systems. When a snapshot is received it is available in the incoming snapshot buffer. The same method is used for the command stream and the RPC stream. The code for receiving data from a connection and storing it on the corresponding entity is available in [NetworkStreamReceiveSystem.cs](Connection/NetworkStreamReceiveSystem.cs).

### RPC events

The netcode handles events by a limited form of RPC calls in [RpcSystem.cs](Rpc/RpcSystem.cs). The RPC calls can be issues from a job on the sending side, and they will execute in a job on the receiving side - which limits what you can do in an RPC.
In order to send an RPC you first need to get access to an RpcQueue for the command you want to send. This can be created in OnCreateManager - by calling `m_RpcQueue = World.GetOrCreateManager<RpcSystem>().GetRpcQueue<RpcLoadLevel>();` -  and cached through the lifetime of the game. Once you have the queue you can schedule events in it by getting the `OutgoingRpcDataStreamBufferComponent` buffer from the entity representing the connection you wish to send the event to and calling `rpcQueue.Schedule(rpcBuffer, new RpcCommand);`. That will append the correct RPC data to the outgoing RPC buffer (`OutgoingRpcDataStreamBufferComponent`) so it can be sent to the remote end by `NetworkStreamSendSystem`.
The RpcCommand interface has three methods, Serialize and Deserialize for storing the data in a packet and an Execute method which takes three parameters: `Entity connection, EntityCommandBuffer.Concurrent commandBuffer, int jobIndex` . The RPC can either modify the connection entity using the command buffer, or it can create a new request entity using the command buffer for more complex tasks, and apply the command in a separate system at a later time. This means that there is no need to do anything special to receive an RPC, it will have its `Execute` method called on the receiving end automatically.

## Command stream

The client will continuously send a command stream to the server. This stream includes all inputs and acknowledgements of the last received snapshot.
The command system is a generic system so you need to specify a specific instance to enable it. The system will look for the ```CommandTargetComponent``` component on the connection and replicate everything in the ```ICommandData``` buffer on that target entity to the server. The simulation code needs to access that ```ICommandData``` struct to read input instead of reading it directly.

## Ghost snapshots

The ghost snapshot system is the most complex part of the netcode. It is responsible for synchronizing entities which exist on the server to all clients. In order to make it perform well the server will do all processing per ECS chunk rather than per entity. On the receiving side the processing is done per entity. The reason for this is that it is not possible to process per chunk on both sides, and the server has more connections than clients.

The way this is implemented is with a tag component on the server - GhostComponent - and a set of GhostSerializers. In order to start replicating an entity from the server you add the GhostComponent tag. When you do the system in [GhostSendSystem.cs](Snapshot/GhostSendSystem.cs) will detect it and map it to a ghost type by going through a list of ghost serializers to find the first one which can serialize that entity.

The list of serializers for asteroids is in [GhostSerializerCollection.cs](../Samples/Asteroids/Server/Generated/GhostSerializerCollection.cs) and the interface for a serializer is in [IGhostSerializerCollection.cs](Snapshot/IGhostSerializerCollection.cs). Serializers for each entity type can be code generated from their ```.ghost``` files and they'll be automatically set up in these files. The deserializers work the same way and get set up in equivalent files.

Once the type is mapped to a ghost type the serializer will get called to serialize the data from the entity at some time interval. The interval depends on how many entities you have and how important the ghost type is. The serializer can specify an importance factor per ECS chunk, and the importance will be scaled by the time since last serialization to ensure that no entities are starved and never update.

The server operates on a fixed bandwidth, sending a single packet with snapshot data of customizable size every frame. The packet is filled with the entities of the highest importance. Once a packet is full it is sent and remaining entities will be missing from the snapshot. Since the age influences the importance it is more likely that those entities will be included in the next snapshot.

The code generation system handles creating the buffers needed for storing snapshots to the entity. Subsequent received snapshots for the entity will add more data to the buffer and remove the oldest if the buffer is full. Finally the client will run an update system which will interpolate between the received snapshots according to the target interpolation time (see Time synchronization).

### Entity spawning

When a new ghost is received on the client side it will be spawned by a user defined spawn system. There is no specific spawn message, receiving an unknown ghost id counts as an implicit spawn. The spawn system can be code generated along with the serializer and the logic for snapshot handling in general for he entity (snapshot updates).

Because of how snapshot data is interpolated the entity spawning/creation needs to be handled in a special manner. The entity can't be spawned immediately unless it was preemptively spawned (like with spawn prediction), since the data is not ready to be interpolated yet. Otherwise the object would appear and then not get any more updates until the interpolation is ready. Therefore normal spawns happen in a delayed manner. Spawning can be split into 3 main types.

1. Delayed or interpolated spawning. Entity is spawned when the interpolation system is ready to apply updates. This is how remote entities are handled as they are being interpolated in a straightforward manner.
2. Predicted spawning for the client predicted player object. The object is being predicted so input handling applies immediately, therefore it doesn't need to be delay spawned. As snapshot data for this object arrives the update system handles applying the data directly to the object and then playing back the local inputs which have happened since that time (correcting mistakes in prediction).
3. Predicted spawning for player spawned objects. These are objects spawned from player input, like bullets or rockets being fired by the player. The spawn code needs to run on the client, in the client prediction system, then when the first snapshot update for the entity arrives it will apply to that predict spawned object (no new entity is created). After this the snapshot updates are applied just like in case 2.

Handling the third case of predicted spawning requires some user code to be implemented to handle it. Code generation will handle creating some boilerplate code around a ```.ghost``` entity definition. Most of the handling for the spawning routines is in [DefaultGhostSpawnSystem.cs](Snapshot/DefaultGhostSpawnSystem.cs) and the entity specific spawning can be extended by implementing a partial class of the same name. A function called ```MarkPredictedGhosts``` is called so you can assign a specific ID for that type of prediction (this can be seen in asteroids in [BulletGhostSpawnSystem.cs](../Samples/Asteroids/Client/BulletGhostSpawnSystem.cs) for example)

### Snapshot visualization

In order to reason about what is being put on the wire in the netcode we have a small prototype of a visualization tool in the Stats folder. The tool will display one vertical bar for each received snapshot with breakdown of ghost types in that snapshot. Clicking a bar will display more detailed stats about the snapshot. This tool is a prototype, in the future it will be integrated with the unity profiler to make it easier to correlate network traffic with memory usage and CPU performance.

## Time synchronization

In order to know which server tick the client should be displaying there is a small system to calculate that in [NetworkTimeSystem.cs](Connection/NetworkTimeSystem.cs). The time system will calculate the latest server tick the client should have received based on previous data. The interpolation target is then calculated as the latest expected snapshot minus an interpolation period.

The time system also calculates at which server tick the server will process a message the client sends this frame. This time will be used as the target for client prediction.

# Getting started

This is a very brief list of things you have to do to create a new game using the Unity NetCode. It does not describe all the details of how to do these things, but it does tell you what to look for.

1. Split the code into client and server by adding ```[UpdateInGroup(typeof(...))]``` where `...` is one of ```ServerSimulationSystemGroup```, ```ClientSimulationSystemGroup```, ```ClientPresentationSystemGroup``` or ```ClientAndServerSimulationSystemGroup```.
2. If you want to render at a higher framerate than the fixed simulation frequency, make sure all rendered entities has a ```CurrentSimulatedPosition``` and ```CurrentSimulatedRotation``` so it is interpolated for presentation. The previous versions of the components will be added automatically.
3. Generate an RpcCollection from ```Multiplayer > CodeGen > RpcCollection Generator``` in the menu. This handles the RPC queue which stores all the RPC messages for processing. It will create a struct implementing the ```IRpcCollection``` interface and enable the RpcSystem by adding (name prefix is the application product name)

    ```c#
    public class MultiplayerSampleRpcSystem : RpcSystem<RpcCollection>
    {
    }
    ```

4. Create a struct implementing ```ICommandData``` for storing the game inputs you want to send to the server. Input must be pushed to this system every frame. Also enable the systems for sending commands to the server by adding

    ```c#
    public class AsteroidsCommandReceiveSystem : CommandReceiveSystem<ShipCommandData>
    {
    }
    public class AsteroidsCommandSendSystem : CommandSendSystem<ShipCommandData>
    {
    }
    ```

5. The network connection entity has a component called ```CommandTargetComponent```. Set the ```targetEntity``` of the connection to an entity which has a ```BufferFromEntity<ShipComponentData>``` both on the client and server to replicate the input data.
6. Generate the serialize collections from ```Multiplayer > CodeGen > GhostCollection Generator``` in the menu. This will create a struct implementing ```IGhostSerializerCollection``` and a matching struct implementing ```IGhostDeserializerCollection```.  It will also enable the ghost receive and send systems by adding

    ```c#
    public class AsteroidsGhostSendSystem : GhostSendSystem<GhostSerializerCollection>
    {
    }
    public class AsteroidsGhostReceiveSystem : GhostReceiveSystem<GhostDeserializerCollection>
    {
    }
    ```

7. The collections should contain ghost types which consists of a struct implementing ```ISnapshotData```, a serializer based on ```IGhostSerializer<T>```, a spawn system usually based on ```DefaultGhostSpawnSystem<T>```, an update system for interpolation and optionally a prediction system if the ghost type supports prediction. The code for this can be generated by creating a file with the ```.ghost``` extension in the assets folder, selecting it in Unity and clicking "Generate code" in the inspector (after editing it).
8. When the level is loaded and the client is ready to start receiving snapshots you must add the tag component ```NetworkStreamInGame``` to the connection entity both on the client and the server.


/** A connection is represented by an entity having a NetworkStreamConnection.
 * If the entity does not have a NetworkIdComponent it is to be considered connecting.
 * It is possible to add more tags to signal the state of the connection, for example
 * adding an InGame component to signal loading being complete.
 *
 * In addition to these components all connections have a set of incoming and outgoing
 * buffers associated with them.
 */

using Unity.Entities;
using Unity.Networking.Transport;

public struct NetworkStreamConnection : IComponentData
{
    public NetworkConnection Value;
}

public struct NetworkStreamInGame : IComponentData
{
}

public struct NetworkStreamDisconnected : IComponentData
{
}

public struct IncomingCommandDataStreamBufferComponent : IBufferElementData
{
    public byte Value;
}

public struct IncomingSnapshotDataStreamBufferComponent : IBufferElementData
{
    public byte Value;
}



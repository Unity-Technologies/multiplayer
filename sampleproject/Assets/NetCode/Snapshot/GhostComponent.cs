using Unity.Entities;

/// <summary>
/// Component signaling that an entity on the server should be replicated to clients
/// </summary>
public struct GhostComponent : IComponentData
{
}

/// <summary>
/// Component on client signaling that an entity has been replicated from the server
/// </summary>
public struct ReplicatedEntityComponent : IComponentData
{}
/// <summary>
/// Component on client signaling that an entity is predicted instead of interpolated
/// </summary>
public struct PredictedEntityComponent : IComponentData
{}

/// <summary>
/// Component used to request predictive spawn of a ghost. Create an entity with this
/// tag and an ISnapshotData of the ghost type you want to create. The ghost type must
/// support predictive spawning to use this.
/// </summary>
public struct PredictedSpawnRequestComponent : IComponentData
{}

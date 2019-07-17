using Unity.Entities;
using Unity.Transforms;

public partial class AsteroidGhostSpawnSystem : DefaultGhostSpawnSystem<AsteroidSnapshotData>
{
    protected override EntityArchetype GetGhostArchetype()
    {
        return EntityManager.CreateArchetype(
            ComponentType.ReadWrite<AsteroidSnapshotData>(),
            ComponentType.ReadWrite<AsteroidTagComponentData>(),
            ComponentType.ReadWrite<CurrentSimulatedPosition>(),
            ComponentType.ReadWrite<CurrentSimulatedRotation>(),
            ComponentType.ReadWrite<Rotation>(),
            ComponentType.ReadWrite<Translation>(),

            ComponentType.ReadWrite<ReplicatedEntityComponent>()
        );
    }
    protected override EntityArchetype GetPredictedGhostArchetype()
    {
        return EntityManager.CreateArchetype(
            ComponentType.ReadWrite<AsteroidSnapshotData>(),
            ComponentType.ReadWrite<AsteroidTagComponentData>(),
            ComponentType.ReadWrite<CurrentSimulatedPosition>(),
            ComponentType.ReadWrite<CurrentSimulatedRotation>(),
            ComponentType.ReadWrite<Rotation>(),
            ComponentType.ReadWrite<Translation>(),

            ComponentType.ReadWrite<ReplicatedEntityComponent>(),
            ComponentType.ReadWrite<PredictedEntityComponent>()
        );
    }
}

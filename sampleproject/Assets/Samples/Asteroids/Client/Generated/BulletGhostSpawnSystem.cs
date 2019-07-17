using Unity.Entities;
using Unity.Transforms;

public partial class BulletGhostSpawnSystem : DefaultGhostSpawnSystem<BulletSnapshotData>
{
    protected override EntityArchetype GetGhostArchetype()
    {
        return EntityManager.CreateArchetype(
            ComponentType.ReadWrite<BulletSnapshotData>(),
            ComponentType.ReadWrite<BulletTagComponentData>(),
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
            ComponentType.ReadWrite<BulletSnapshotData>(),
            ComponentType.ReadWrite<BulletTagComponentData>(),
            ComponentType.ReadWrite<CurrentSimulatedPosition>(),
            ComponentType.ReadWrite<CurrentSimulatedRotation>(),
            ComponentType.ReadWrite<Rotation>(),
            ComponentType.ReadWrite<Translation>(),
            ComponentType.ReadWrite<Velocity>(),

            ComponentType.ReadWrite<ReplicatedEntityComponent>(),
            ComponentType.ReadWrite<PredictedEntityComponent>()
        );
    }
}

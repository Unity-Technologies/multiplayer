using Unity.Entities;
using Unity.Transforms;

public partial class ShipGhostSpawnSystem : DefaultGhostSpawnSystem<ShipSnapshotData>
{
    protected override EntityArchetype GetGhostArchetype()
    {
        return EntityManager.CreateArchetype(
            ComponentType.ReadWrite<ShipSnapshotData>(),
            ComponentType.ReadWrite<CurrentSimulatedPosition>(),
            ComponentType.ReadWrite<CurrentSimulatedRotation>(),
            ComponentType.ReadWrite<ParticleEmitterComponentData>(),
            ComponentType.ReadWrite<Rotation>(),
            ComponentType.ReadWrite<ShipStateComponentData>(),
            ComponentType.ReadWrite<ShipTagComponentData>(),
            ComponentType.ReadWrite<Translation>(),

            ComponentType.ReadWrite<ReplicatedEntityComponent>()
        );
    }
    protected override EntityArchetype GetPredictedGhostArchetype()
    {
        return EntityManager.CreateArchetype(
            ComponentType.ReadWrite<ShipSnapshotData>(),
            ComponentType.ReadWrite<CurrentSimulatedPosition>(),
            ComponentType.ReadWrite<CurrentSimulatedRotation>(),
            ComponentType.ReadWrite<ParticleEmitterComponentData>(),
            ComponentType.ReadWrite<PlayerIdComponentData>(),
            ComponentType.ReadWrite<Rotation>(),
            ComponentType.ReadWrite<ShipCommandData>(),
            ComponentType.ReadWrite<ShipStateComponentData>(),
            ComponentType.ReadWrite<ShipTagComponentData>(),
            ComponentType.ReadWrite<Translation>(),
            ComponentType.ReadWrite<Velocity>(),

            ComponentType.ReadWrite<ReplicatedEntityComponent>(),
            ComponentType.ReadWrite<PredictedEntityComponent>()
        );
    }
}

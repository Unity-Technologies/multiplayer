using Unity.Entities;
using Unity.Transforms;

public partial class ShipGhostSpawnSystem : DefaultGhostSpawnSystem<ShipSnapshotData>
{
    protected override EntityArchetype GetGhostArchetype()
    {
        return EntityManager.CreateArchetype(
            ComponentType.ReadWrite<ShipSnapshotData>(),
            ComponentType.ReadWrite<Translation>(),
            ComponentType.ReadWrite<Rotation>(),
            ComponentType.ReadWrite<ShipStateComponentData>(),
            ComponentType.ReadWrite<ShipTagComponentData>(),
            ComponentType.ReadWrite<ParticleEmitterComponentData>(),
            ComponentType.ReadWrite<CurrentSimulatedPosition>(),
            ComponentType.ReadWrite<CurrentSimulatedRotation>(),

            ComponentType.ReadWrite<ReplicatedEntityComponent>()
        );
    }
    protected override EntityArchetype GetPredictedGhostArchetype()
    {
        return EntityManager.CreateArchetype(
            ComponentType.ReadWrite<ShipSnapshotData>(),
            ComponentType.ReadWrite<Translation>(),
            ComponentType.ReadWrite<Rotation>(),
            ComponentType.ReadWrite<Velocity>(),
            ComponentType.ReadWrite<ShipStateComponentData>(),
            ComponentType.ReadWrite<PlayerIdComponentData>(),
            ComponentType.ReadWrite<ShipTagComponentData>(),
            ComponentType.ReadWrite<ParticleEmitterComponentData>(),
            ComponentType.ReadWrite<CurrentSimulatedPosition>(),
            ComponentType.ReadWrite<CurrentSimulatedRotation>(),
            ComponentType.ReadWrite<ShipCommandData>(),
            ComponentType.ReadWrite<GhostShipState>(),

            ComponentType.ReadWrite<ReplicatedEntityComponent>(),
            ComponentType.ReadWrite<PredictedEntityComponent>()
        );
    }
}

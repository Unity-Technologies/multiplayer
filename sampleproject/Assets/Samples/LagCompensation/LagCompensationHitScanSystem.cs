using Unity.Entities;
using Unity.NetCode;
using Unity.Jobs;

[UpdateInGroup(typeof(GhostPredictionSystemGroup))]
public class LagCompensationHitScanSystem : SystemBase
{
    private PhysicsWorldHistory m_physicsHistory;
    private GhostPredictionSystemGroup m_predictionGroup;
    private bool m_IsServer;
    protected override void OnCreate()
    {
        m_physicsHistory = World.GetOrCreateSystem<PhysicsWorldHistory>();
        m_predictionGroup = World.GetOrCreateSystem<GhostPredictionSystemGroup>();
        m_IsServer = World.GetExistingSystem<ServerSimulationSystemGroup>() != null;
    }
    protected override void OnUpdate()
    {
        var collisionHistory = m_physicsHistory.CollisionHistory;
        uint predictingTick = m_predictionGroup.PredictingTick;
        // Do not perform hit-scan when rolling back, only when simulating the latest tick
        if (!m_predictionGroup.IsFinalPredictionTick)
            return;
        var isServer = m_IsServer;
        // Not using burst since there is a static used to update the UI
        Dependency = Entities.WithoutBurst().ForEach((DynamicBuffer<RayTraceCommand> commands, in CommandDataInterpolationDelay delay) =>
        {
            // If there is no data for the tick or a fire was not requested - do not process anything
            if (!commands.GetDataAtTick(predictingTick, out var cmd))
                return;
            if (cmd.lastFire != predictingTick)
                return;

            // Get the collision world to use given the tick currently being predicted and the interpolation delay for the connection
            collisionHistory.GetCollisionWorldFromTick(predictingTick, LagUI.EnableLagCompensation ? delay.Delay : 0, out var collWorld);
            var rayInput = new Unity.Physics.RaycastInput();
            rayInput.Start = cmd.origin;
            rayInput.End = cmd.origin + cmd.direction * 100;
            rayInput.Filter = Unity.Physics.CollisionFilter.Default;
            bool hit = collWorld.CastRay(rayInput);
            if (isServer)
            {
                LagUI.ServerTick = predictingTick;
                LagUI.ServerHit = hit;
            }
            else
            {
                LagUI.ClientTick = predictingTick;
                LagUI.ClientHit = hit;
            }
        }).Schedule(JobHandle.CombineDependencies(Dependency, m_physicsHistory.LastPhysicsJobHandle));

        m_physicsHistory.LastPhysicsJobHandle = Dependency;
    }
}

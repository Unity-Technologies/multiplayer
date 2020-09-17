using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.NetCode;

[UpdateInGroup(typeof(GhostSpawnSystemGroup))]
[UpdateAfter(typeof(GhostSpawnSystem))]
public class ComponentShipGhostSpawnSystem : SystemBase
{
    private BeginSimulationEntityCommandBufferSystem m_Barrier;
    protected override void OnCreate()
    {
        m_Barrier = World.GetExistingSystem<BeginSimulationEntityCommandBufferSystem>();
        RequireSingletonForUpdate<NetworkIdComponent>();
    }

    struct GhostShipState : ISystemStateComponentData
    {
    }

    protected override void OnUpdate()
    {
        var playerEntity = GetSingletonEntity<NetworkIdComponent>();
        var commandTargetFromEntity = GetComponentDataFromEntity<CommandTargetComponent>();

        var commandBuffer = m_Barrier.CreateCommandBuffer().AsParallelWriter();

        Entities
            .WithNone<GhostShipState>()
            .WithAll<ShipTagComponentData, PredictedGhostComponent>()
            .ForEach((Entity entity) =>{
            var state = commandTargetFromEntity[playerEntity];
            state.targetEntity = entity;
            commandTargetFromEntity[playerEntity] = state;
            commandBuffer.AddComponent(0, entity, new GhostShipState());
        }).Schedule();

        Entities.WithNone<SnapshotData>()
            .WithAll<GhostShipState>()
            .WithNativeDisableParallelForRestriction(commandTargetFromEntity)
            .ForEach((Entity ent, int entityInQueryIndex) => {
            var commandTarget = commandTargetFromEntity[playerEntity];

            if (ent == commandTarget.targetEntity)
            {
                commandTarget.targetEntity = Entity.Null;
                commandTargetFromEntity[playerEntity] = commandTarget;
            }
            commandBuffer.RemoveComponent<GhostShipState>(entityInQueryIndex, ent);
        }).ScheduleParallel();
        m_Barrier.AddJobHandleForProducer(Dependency);
    }
}

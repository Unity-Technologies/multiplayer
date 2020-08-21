using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.NetCode;

[UpdateInGroup(typeof(GhostSpawnSystemGroup))]
[UpdateAfter(typeof(GhostSpawnSystem))]
public class ComponentShipGhostSpawnSystem : SystemBase
{
    private EntityQuery m_DestroyGroup;
    private BeginSimulationEntityCommandBufferSystem m_Barrier;
    protected override void OnCreate()
    {
        m_DestroyGroup = GetEntityQuery(ComponentType.ReadWrite<GhostShipState>(),
            ComponentType.Exclude<SnapshotData>());

        m_Barrier = World.GetExistingSystem<BeginSimulationEntityCommandBufferSystem>();
        RequireSingletonForUpdate<NetworkIdComponent>();
    }

    struct GhostShipState : ISystemStateComponentData
    {
    }

    struct DestroyJob : IJobChunk
    {
        public EntityCommandBuffer.ParallelWriter commandBuffer;
        [ReadOnly] public EntityTypeHandle entityType;

        public Entity playerEntity;
        [NativeDisableParallelForRestriction] public ComponentDataFromEntity<CommandTargetComponent> commandTargetFromEntity;
        public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
        {
            var commandTarget = default(CommandTargetComponent);
            commandTarget = commandTargetFromEntity[playerEntity];
            var ents = chunk.GetNativeArray(entityType);
            for (int i = 0; i < ents.Length; ++i)
            {
                var ent = ents[i];
                if (ent == commandTarget.targetEntity)
                {
                    commandTarget.targetEntity = Entity.Null;
                    commandTargetFromEntity[playerEntity] = commandTarget;
                }
                commandBuffer.RemoveComponent<GhostShipState>(chunkIndex, ent);
            }
        }
    }

    protected override void OnUpdate()
    {
        var entityType = GetEntityTypeHandle();

        var playerEntity = GetSingletonEntity<NetworkIdComponent>();
        var commandTargetFromEntity = GetComponentDataFromEntity<CommandTargetComponent>();

        var commandBuffer = m_Barrier.CreateCommandBuffer().AsParallelWriter();

        Entities.WithNone<GhostShipState>().WithAll<ShipTagComponentData, PredictedGhostComponent>().ForEach((Entity entity) =>{
                var state = commandTargetFromEntity[playerEntity];
                state.targetEntity = entity;
                commandTargetFromEntity[playerEntity] = state;
                commandBuffer.AddComponent(0, entity, new GhostShipState());
        }).Schedule();

        var destroyJob = new DestroyJob
        {
            commandBuffer = commandBuffer,
            entityType = entityType,
            playerEntity = playerEntity,
            commandTargetFromEntity = GetComponentDataFromEntity<CommandTargetComponent>()
        };
        var inputDeps = destroyJob.Schedule(m_DestroyGroup, Dependency);
        m_Barrier.AddJobHandleForProducer(inputDeps);
        Dependency = inputDeps;
    }
}

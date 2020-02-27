using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.NetCode;

public partial class ShipGhostSpawnSystem
{
    private EntityQuery m_DestroyGroup;
    private BeginSimulationEntityCommandBufferSystem m_Barrier;
    protected override void OnCreate()
    {
        base.OnCreate();
        m_DestroyGroup = GetEntityQuery(ComponentType.ReadWrite<GhostShipState>(),
            ComponentType.Exclude<ShipSnapshotData>());

        m_Barrier = World.GetExistingSystem<BeginSimulationEntityCommandBufferSystem>();
    }

    protected override JobHandle UpdateNewInterpolatedEntities(NativeArray<Entity> entities, JobHandle inputDeps)
    {
        return UpdateNewPredictedEntities(entities, inputDeps);
    }

    protected override JobHandle UpdateNewPredictedEntities(NativeArray<Entity> entities, JobHandle inputDeps)
    {
        var job = new SetPlayerStateJob
        {
            entities = entities,
            snapshotFromEntity = GetBufferFromEntity<ShipSnapshotData>(),
            shipStateFromEntity = GetComponentDataFromEntity<ShipStateComponentData>(),
            playerEntities = m_PlayerGroup.ToEntityArray(Allocator.TempJob),
            playerIds = m_PlayerGroup.ToComponentDataArray<NetworkIdComponent>(Allocator.TempJob),
            commandTargetFromEntity = GetComponentDataFromEntity<CommandTargetComponent>(),
            commandBuffer = World.GetExistingSystem<BeginSimulationEntityCommandBufferSystem>().CreateCommandBuffer().ToConcurrent() // FIXME: need to add the dependency to this too
        };
        return job.Schedule(entities.Length, 8, inputDeps);
    }

    struct SetPlayerStateJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Entity> entities;
        [NativeDisableParallelForRestriction] public BufferFromEntity<ShipSnapshotData> snapshotFromEntity;
        [NativeDisableParallelForRestriction] public ComponentDataFromEntity<ShipStateComponentData> shipStateFromEntity;

        [DeallocateOnJobCompletion] public NativeArray<Entity> playerEntities;
        [DeallocateOnJobCompletion] public NativeArray<NetworkIdComponent> playerIds;

        [NativeDisableParallelForRestriction]
        public ComponentDataFromEntity<CommandTargetComponent> commandTargetFromEntity;

        public EntityCommandBuffer.Concurrent commandBuffer;
        public void Execute(int i)
        {
            var snapshot = snapshotFromEntity[entities[i]];

            bool selfSpawn = playerIds.Length > 0 && (snapshot[0].GetPlayerIdComponentDataPlayerId() == playerIds[0].Value);
            if (selfSpawn)
            {
                shipStateFromEntity[entities[i]] = new ShipStateComponentData
                {
                    State = 0
                };
                var state = commandTargetFromEntity[playerEntities[0]];
                state.targetEntity = entities[i];
                commandTargetFromEntity[playerEntities[0]] = state;
            }
            commandBuffer.AddComponent(i, entities[i], new GhostShipState());
        }
    }

    struct GhostShipState : ISystemStateComponentData
    {
    }

    struct DestroyJob : IJobChunk
    {
        public EntityCommandBuffer.Concurrent commandBuffer;
        [ReadOnly] public ArchetypeChunkEntityType entityType;

        [DeallocateOnJobCompletion][NativeDisableParallelForRestriction] public NativeArray<Entity> playerEntity;
        [NativeDisableParallelForRestriction] public ComponentDataFromEntity<CommandTargetComponent> commandTargetFromEntity;
        public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
        {
            var commandTarget = default(CommandTargetComponent);
            if (playerEntity.Length > 0)
            {
                commandTarget = commandTargetFromEntity[playerEntity[0]];
            }
            var ents = chunk.GetNativeArray(entityType);
            for (int i = 0; i < ents.Length; ++i)
            {
                var ent = ents[i];
                if (ent == commandTarget.targetEntity)
                {
                    commandTarget.targetEntity = Entity.Null;
                    commandTargetFromEntity[playerEntity[0]] = commandTarget;
                }
                commandBuffer.RemoveComponent<GhostShipState>(chunkIndex, ent);
            }
        }
    }

    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        inputDeps = base.OnUpdate(inputDeps);

        var entityType = GetArchetypeChunkEntityType();

        JobHandle playerHandle;
        var playerEntity = m_PlayerGroup.ToEntityArrayAsync(Allocator.TempJob, out playerHandle);

        var destroyJob = new DestroyJob
        {
            commandBuffer = m_Barrier.CreateCommandBuffer().ToConcurrent(),
            entityType = entityType,
            playerEntity = playerEntity,
            commandTargetFromEntity = GetComponentDataFromEntity<CommandTargetComponent>()
        };
        inputDeps = destroyJob.Schedule(m_DestroyGroup, JobHandle.CombineDependencies(inputDeps, playerHandle));
        m_Barrier.AddJobHandleForProducer(inputDeps);
        return inputDeps;
    }
}

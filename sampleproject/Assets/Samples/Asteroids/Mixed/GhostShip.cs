using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Networking.Transport;
using Unity.Transforms;

public struct ShipSnapshotData : ISnapshotData<ShipSnapshotData>
{
    public uint Tick => tick;
    public uint tick;
    public int playerId;
    private int posX;
    private int posY;
    private int rot;
    public int state;

    public float GetPosX()
    {
        return posX * 0.1f;
    }
    public void SetPosX(float x)
    {
        posX = (int)(x * 10.0f);
    }
    public float GetPosY()
    {
        return posY * 0.1f;
    }
    public void SetPosY(float y)
    {
        posY = (int)(y * 10.0f);
    }
    public quaternion GetRot()
    {
        var qw = rot*0.001f;
        return new quaternion(0, 0, math.abs(qw) > 1-1e-9?0:math.sqrt(1-qw*qw), qw);
    }
    public void SetRot(quaternion q)
    {
        rot = (int) ((q.value.z >= 0 ? q.value.w : -q.value.w) * 1000.0f);
    }

    public void PredictDelta(uint tick, ref ShipSnapshotData baseline1, ref ShipSnapshotData baseline2)
    {
        var predictor = new GhostDeltaPredictor(tick, this.tick, baseline1.tick,
            baseline2.tick);
        posX = predictor.PredictInt(posX, baseline1.posX, baseline2.posX);
        posY = predictor.PredictInt(posY, baseline1.posY, baseline2.posY);
        rot = predictor.PredictInt(rot, baseline1.rot, baseline2.rot);
    }

    public void Serialize(ref ShipSnapshotData baseline, DataStreamWriter writer, NetworkCompressionModel compressionModel)
    {
        writer.WritePackedUIntDelta((uint) playerId, (uint) baseline.playerId, compressionModel);
        writer.WritePackedIntDelta(posX, baseline.posX, compressionModel);
        writer.WritePackedIntDelta(posY, baseline.posY, compressionModel);
        writer.WritePackedIntDelta(rot, baseline.rot, compressionModel);
        writer.WritePackedUInt((uint) state, compressionModel);
    }

    public void Deserialize(uint tick, ref ShipSnapshotData baseline, DataStreamReader reader, ref DataStreamReader.Context ctx,
        NetworkCompressionModel compressionModel)
    {
        this.tick = tick;
        playerId = (int)reader.ReadPackedUIntDelta(ref ctx, (uint)baseline.playerId, compressionModel);
        posX = reader.ReadPackedIntDelta(ref ctx, baseline.posX, compressionModel);
        posY = reader.ReadPackedIntDelta(ref ctx, baseline.posY, compressionModel);
        rot = reader.ReadPackedIntDelta(ref ctx, baseline.rot, compressionModel);
        state = (int)reader.ReadPackedUInt(ref ctx, compressionModel);
    }
    public void Interpolate(ref ShipSnapshotData target, float factor)
    {
        SetPosX(math.lerp(GetPosX(), target.GetPosX(), factor));
        SetPosY(math.lerp(GetPosY(), target.GetPosY(), factor));
        SetRot(math.slerp(GetRot(), target.GetRot(), factor));
    }
}
public struct ShipGhostSerializer : IGhostSerializer<ShipSnapshotData>
{
    // FIXME: These disable safety since all serializers have an instance of the same type - causing aliasing. Should be fixed in a cleaner way
    [NativeDisableContainerSafetyRestriction] private ArchetypeChunkComponentType<Translation> ghostPositionType;
    [NativeDisableContainerSafetyRestriction] private ArchetypeChunkComponentType<Rotation> ghostRotationType;
    private ArchetypeChunkComponentType<ShipStateComponentData> ghostStateType;
    private ArchetypeChunkComponentType<PlayerIdComponentData> ghostPlayerType;

    public int CalculateImportance(ArchetypeChunk chunk)
    {
        return 200;
    }

    public bool WantsPredictionDelta => true;

    public int SnapshotSize => UnsafeUtility.SizeOf<ShipSnapshotData>();
    public void BeginSerialize(ComponentSystemBase system)
    {
        ghostPositionType = system.GetArchetypeChunkComponentType<Translation>();
        ghostRotationType = system.GetArchetypeChunkComponentType<Rotation>();
        ghostStateType = system.GetArchetypeChunkComponentType<ShipStateComponentData>();
        ghostPlayerType = system.GetArchetypeChunkComponentType<PlayerIdComponentData>();

        shipTagType = ComponentType.ReadWrite<ShipTagComponentData>();
        positionType = ComponentType.ReadWrite<Translation>();
        rotationType = ComponentType.ReadWrite<Rotation>();
        shipStateType = ComponentType.ReadWrite<ShipStateComponentData>();
        playerIdType = ComponentType.ReadWrite<PlayerIdComponentData>();
    }

    private ComponentType shipTagType;
    private ComponentType positionType;
    private ComponentType rotationType;
    private ComponentType shipStateType;
    private ComponentType playerIdType;

    public bool CanSerialize(EntityArchetype arch)
    {
        var components = arch.GetComponentTypes();
        int matches = 0;
        for (int i = 0; i < components.Length; ++i)
        {
            if (components[i] == shipTagType)
                ++matches;
            if (components[i] == positionType)
                ++matches;
            if (components[i] == rotationType)
                ++matches;
            if (components[i] == shipStateType)
                ++matches;
            if (components[i] == playerIdType)
                ++matches;
        }
        return (matches == 5);
    }

    public void CopyToSnapshot(ArchetypeChunk chunk, int ent, uint tick, ref ShipSnapshotData snapshot)
    {
        var player = chunk.GetNativeArray(ghostPlayerType);
        var pos = chunk.GetNativeArray(ghostPositionType);
        var rot = chunk.GetNativeArray(ghostRotationType);
        var state = chunk.GetNativeArray(ghostStateType);
        snapshot.tick = tick;
        snapshot.playerId = player[ent].PlayerId;
        snapshot.SetPosX(pos[ent].Value.x);
        snapshot.SetPosY(pos[ent].Value.y);
        snapshot.SetRot(rot[ent].Value);
        snapshot.state = state[ent].State;
    }
}

public class ShipGhostSpawnSystem : DefaultGhostSpawnSystem<ShipSnapshotData>
{
    private ComponentGroup m_PlayerGroup;
    protected override void OnCreateManager()
    {
        base.OnCreateManager();
        m_PlayerGroup = GetComponentGroup(ComponentType.ReadOnly<PlayerStateComponentData>(), ComponentType.ReadOnly<NetworkIdComponent>());
    }

    protected override EntityArchetype GetGhostArchetype()
    {
        return EntityManager.CreateArchetype(
            ComponentType.ReadWrite<ShipSnapshotData>(),
            ComponentType.ReadWrite<ReplicatedEntity>(),
            ComponentType.ReadWrite<ShipGhostUpdateSystem.GhostShipState>(),
            ComponentType.ReadWrite<ShipTagComponentData>(),
            ComponentType.ReadWrite<ShipStateComponentData>(),
            ComponentType.ReadWrite<ParticleEmitterComponentData>(), // FIXME: with value
            ComponentType.ReadWrite<CurrentSimulatedPosition>(),
            ComponentType.ReadWrite<CurrentSimulatedRotation>(),
            ComponentType.ReadWrite<Translation>(),
            ComponentType.ReadWrite<Rotation>());
    }

    protected override JobHandle UpdateNewEntities(NativeArray<Entity> entities, JobHandle inputDeps)
    {
        var job = new SetPlayerStateJob
        {
            entities = entities,
            snapshotFromEntity = GetBufferFromEntity<ShipSnapshotData>(),
            emitterFromEntity = GetComponentDataFromEntity<ParticleEmitterComponentData>(),
            shipStateFromEntity = GetComponentDataFromEntity<ShipStateComponentData>(),
            emitterData = GetSingleton<ClientSettings>().particleEmitter,
            playerEntities = m_PlayerGroup.ToEntityArray(Allocator.TempJob),
            playerIds = m_PlayerGroup.ToComponentDataArray<NetworkIdComponent>(Allocator.TempJob),
            playerStateFromEntity = GetComponentDataFromEntity<PlayerStateComponentData>()
        };
        return job.Schedule(entities.Length, 8, inputDeps);
    }

    struct SetPlayerStateJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Entity> entities;
        [NativeDisableParallelForRestriction] public BufferFromEntity<ShipSnapshotData> snapshotFromEntity;
        [NativeDisableParallelForRestriction] public ComponentDataFromEntity<ParticleEmitterComponentData> emitterFromEntity;
        [NativeDisableParallelForRestriction] public ComponentDataFromEntity<ShipStateComponentData> shipStateFromEntity;
        public ParticleEmitterComponentData emitterData;

        [DeallocateOnJobCompletion] public NativeArray<Entity> playerEntities;
        [DeallocateOnJobCompletion] public NativeArray<NetworkIdComponent> playerIds;

        [NativeDisableParallelForRestriction]
        public ComponentDataFromEntity<PlayerStateComponentData> playerStateFromEntity;
        public void Execute(int i)
        {
            emitterFromEntity[entities[i]] = emitterData;
            var snapshot = snapshotFromEntity[entities[i]];

            bool selfSpawn = playerIds.Length > 0 && (snapshot[0].playerId == playerIds[0].Value);
            if (selfSpawn)
            {
                shipStateFromEntity[entities[i]] = new ShipStateComponentData
                {
                    IsLocalPlayer = 1,
                    State = 0
                };
                var state = playerStateFromEntity[playerEntities[0]];
                state.PlayerShip = entities[i];
                playerStateFromEntity[playerEntities[0]] = state;
            }
        }
    }

}

[UpdateInGroup(typeof(GhostReceiveSystemGroup))]
public class ShipGhostUpdateSystem : JobComponentSystem
{
    private ComponentGroup destroyGroup;
    private ComponentGroup playerGroup;
    private BeginSimulationEntityCommandBufferSystem m_Barrier;

    public struct GhostShipState : ISystemStateComponentData
    {
    }

    protected override void OnCreateManager()
    {
        destroyGroup = GetComponentGroup(ComponentType.ReadWrite<GhostShipState>(),
            ComponentType.Exclude<ShipSnapshotData>()); // FIXME: should keep until there is no more available snapshot data in the case of interpolation

        playerGroup = GetComponentGroup(ComponentType.ReadWrite<PlayerStateComponentData>(), ComponentType.ReadOnly<NetworkIdComponent>());
        m_Barrier = World.GetExistingManager<BeginSimulationEntityCommandBufferSystem>();
    }
    [BurstCompile]
    [RequireComponentTag(typeof(ShipSnapshotData))]
    struct UpdateJob : IJobProcessComponentDataWithEntity<ShipStateComponentData, Translation, Rotation>
    {
        [NativeDisableParallelForRestriction] public BufferFromEntity<ShipSnapshotData> snapshotFromEntity;
        public uint targetTick;
        public void Execute(Entity entity, int index, ref ShipStateComponentData shipState, ref Translation position, ref Rotation rotation)
        {
            var snapshot = snapshotFromEntity[entity];
            ShipSnapshotData snapshotData;
            snapshot.GetDataAtTick(targetTick, out snapshotData);
            shipState.State = snapshotData.state;
            position = new Translation {Value = new float3(snapshotData.GetPosX(), snapshotData.GetPosY(), 0)};
            rotation = new Rotation {Value = snapshotData.GetRot()};
        }
    }
    struct DestroyJob : IJobChunk
    {
        public EntityCommandBuffer.Concurrent commandBuffer;
        [ReadOnly] public ArchetypeChunkEntityType entityType;

        [DeallocateOnJobCompletion][NativeDisableParallelForRestriction] public NativeArray<Entity> playerEntity;
        [NativeDisableParallelForRestriction] public ComponentDataFromEntity<PlayerStateComponentData> playerStateFromEntity;
        public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
        {
            var playerState = default(PlayerStateComponentData);
            if (playerEntity.Length > 0)
            {
                playerState = playerStateFromEntity[playerEntity[0]];
            }
            var ents = chunk.GetNativeArray(entityType);
            for (int i = 0; i < ents.Length; ++i)
            {
                var ent = ents[i];
                if (ent == playerState.PlayerShip)
                {
                    playerState.PlayerShip = Entity.Null;
                    playerStateFromEntity[playerEntity[0]] = playerState;
                }
                commandBuffer.RemoveComponent<GhostShipState>(chunkIndex, ent);
            }
        }
    }

    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        var entityType = GetArchetypeChunkEntityType();

        JobHandle playerHandle;
        var playerEntity = playerGroup.ToEntityArray(Allocator.TempJob, out playerHandle);

        var updateJob = new UpdateJob
        {
            snapshotFromEntity = GetBufferFromEntity<ShipSnapshotData>(),
            targetTick = NetworkTimeSystem.interpolateTargetTick
        };
        inputDeps = updateJob.Schedule(this, inputDeps);
        var destroyJob = new DestroyJob
        {
            commandBuffer = m_Barrier.CreateCommandBuffer().ToConcurrent(),
            entityType = entityType,
            playerEntity = playerEntity,
            playerStateFromEntity = GetComponentDataFromEntity<PlayerStateComponentData>()
        };
        inputDeps = destroyJob.Schedule(destroyGroup, JobHandle.CombineDependencies(inputDeps, playerHandle));
        m_Barrier.AddJobHandleForProducer(inputDeps);
        return inputDeps;
    }
}
